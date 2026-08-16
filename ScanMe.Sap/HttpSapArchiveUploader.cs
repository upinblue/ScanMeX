using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NAPS2.Sap;

/// <summary>
/// SAP ArchiveLink uploader for a customer-specific OData AttachmentSet service.
/// </summary>
/// <remarks>
/// Owns an <see cref="HttpClient" /> when it created one, so a caller that makes an uploader per document
/// has to dispose it. Without that, a batch leaves one connection pool per scanned document open until
/// the garbage collector gets round to it.
/// </remarks>
public class HttpSapArchiveUploader : ISapArchiveUploader, IDisposable
{
    private const string CsrfHeaderName = "x-csrf-token";

    /// <summary>
    /// Percentage reached once the CSRF token is in hand. The token round trip is a real wait against a
    /// slow gateway, so it gets a visible share of the bar rather than being folded into "starting".
    /// </summary>
    private const int AuthenticatedPercent = 20;

    /// <summary>
    /// Percentage reached once every byte has been handed to the socket. The rest is SAP's own processing,
    /// which reports nothing, so the bar stops here until the response arrives.
    /// </summary>
    private const int SentPercent = 90;

    private readonly SapConnectionConfig _connection;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly CookieContainer _cookieContainer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpSapArchiveUploader" /> class with a managed HTTP handler.
    /// </summary>
    /// <param name="connection">The SAP OData connection configuration.</param>
    public HttpSapArchiveUploader(SapConnectionConfig connection)
        : this(connection, CreateHttpClientHandler(connection), disposeHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpSapArchiveUploader" /> class with a configured HTTP client.
    /// </summary>
    /// <param name="connection">The SAP OData connection configuration.</param>
    /// <param name="httpClient">A configured HTTP client. The caller keeps ownership of it.</param>
    /// <param name="cookieContainer">An optional cookie container used to preserve the SAP Gateway session.</param>
    public HttpSapArchiveUploader(SapConnectionConfig connection, HttpClient httpClient, CookieContainer? cookieContainer = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        TrySetTimeout(_httpClient);
        _cookieContainer = cookieContainer ?? new CookieContainer();
    }

    private HttpSapArchiveUploader(SapConnectionConfig connection, HttpMessageHandler handler, bool disposeHttpClient)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _cookieContainer = new CookieContainer();
        _httpClient = new HttpClient(handler, disposeHttpClient);
        _ownsHttpClient = disposeHttpClient;
        TrySetTimeout(_httpClient);
    }

    /// <inheritdoc />
    public Task<SapUploadResult> UploadAsync(SapUploadRequest request, CancellationToken ct) =>
        UploadAsync(request, null, ct);

    /// <inheritdoc />
    public async Task<SapUploadResult> UploadAsync(SapUploadRequest request,
        IProgress<SapUploadProgress>? progress, CancellationToken ct)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        if (request.DocumentBytes == null)
        {
            throw new ArgumentException("DocumentBytes is required.", nameof(request));
        }

        void Report(SapUploadStage stage, int percent) =>
            progress?.Report(new SapUploadProgress(stage, percent));

        Report(SapUploadStage.Preparing, 0);
        Report(SapUploadStage.Authenticating, 5);
        var csrf = await FetchCsrfTokenAsync(request.Connection, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(csrf.Token))
        {
            return new SapUploadResult(false, csrf.HttpStatusCode, null, null, null,
                "CSRF-Token konnte nicht ermittelt werden", null, csrf.RawResponseBody, Array.Empty<SapErrorDetail>());
        }
        Report(SapUploadStage.Uploading, AuthenticatedPercent);

        var uploadUri = new Uri(request.Connection.GetUploadUrl(), UriKind.Absolute);
        var token = csrf.Token!;
        var csrfRetryUsed = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var message = BuildUploadMessage(request, uploadUri, token, progress);
                using var response = await _httpClient.SendAsync(message, ct).ConfigureAwait(false);
                Report(SapUploadStage.WaitingForSap, SentPercent);
                CaptureCookies(uploadUri, response);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = CreateUploadResult(response, body);
                if (!result.Success && !csrfRetryUsed && IsCsrfFailure(response, result))
                {
                    csrfRetryUsed = true;
                    Report(SapUploadStage.Authenticating, AuthenticatedPercent);
                    var refreshed = await FetchCsrfTokenAsync(request.Connection, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(refreshed.Token))
                    {
                        token = refreshed.Token!;
                        continue;
                    }
                }
                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt >= 2)
                {
                    return new SapUploadResult(false, null, null, null, ex.GetType().Name, ex.Message, null, null,
                        Array.Empty<SapErrorDetail>());
                }
                Report(SapUploadStage.Retrying, AuthenticatedPercent);
                await Task.Delay(attempt == 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3), ct)
                    .ConfigureAwait(false);
            }
        }

        return new SapUploadResult(false, null, null, null, "UPLOAD_FAILED", "SAP upload failed.", null, null,
            Array.Empty<SapErrorDetail>());
    }

    private HttpRequestMessage BuildUploadMessage(SapUploadRequest request, Uri uploadUri, string token,
        IProgress<SapUploadProgress>? progress)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, uploadUri);
        AddCommonHeaders(message, request.Connection);
        AddCookieHeader(message, uploadUri);
        message.Headers.TryAddWithoutValidation(CsrfHeaderName, token);
        message.Headers.Accept.ParseAdd("application/json");
        message.Headers.TryAddWithoutValidation("slug", request.FileName);
        message.Headers.TryAddWithoutValidation("x-sap-archivid", request.Profile.ArchiveId ?? string.Empty);
        message.Headers.TryAddWithoutValidation("x-sap-barcode", request.Barcode);
        AddIfNotEmpty(message, "x-sap-arobject", request.Profile.ArObject);
        AddIfNotEmpty(message, "x-sap-sapobj", request.Profile.SapObject);
        AddIfNotEmpty(message, "x-sap-objectid", request.ObjectId);
        message.Content = progress == null
            ? new ByteArrayContent(request.DocumentBytes)
            : new ProgressByteArrayContent(request.DocumentBytes, sent => progress.Report(
                new SapUploadProgress(SapUploadStage.Uploading,
                    AuthenticatedPercent + sent * (SentPercent - AuthenticatedPercent) / 100)));
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(
            request.OverrideMimeType ?? SapMimeTypeResolver.Resolve(request.FileName));
        return message;
    }

    private static SapUploadResult CreateUploadResult(HttpResponseMessage response, string? body)
    {
        var status = (int) response.StatusCode;
        var location = response.Headers.Location?.ToString();
        if (response.IsSuccessStatusCode)
        {
            return new SapUploadResult(true, status, ExtractDocId(body), location, null, null, null, body,
                Array.Empty<SapErrorDetail>());
        }
        var parsedError = ParseError(body);
        return new SapUploadResult(false, status, null, location, parsedError.ErrorCode, parsedError.ErrorMessage,
            parsedError.TransactionId, TruncateBody(body), parsedError.Details);
    }

    private static bool IsCsrfFailure(HttpResponseMessage response, SapUploadResult result)
    {
        var tokenRequired = response.Headers.TryGetValues(CsrfHeaderName, out var values) &&
                            values.Any(x => string.Equals(x, "Required", StringComparison.OrdinalIgnoreCase));
        return result.HttpStatusCode == 403 &&
               (tokenRequired ||
                (result.ErrorMessage?.IndexOf("CSRF", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (result.ErrorMessage?.IndexOf("token", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (result.RawResponseBody?.IndexOf("CSRF", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (result.RawResponseBody?.IndexOf("token", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
    }

    private static string? TruncateBody(string? body)
    {
        return body == null || body.Length <= 4096 ? body : body.Substring(0, 4096);
    }

    private static void TrySetTimeout(HttpClient httpClient)
    {
        try
        {
            httpClient.Timeout = TimeSpan.FromSeconds(60);
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <inheritdoc />
    public async Task<SapConnectionTestResult> TestConnectionAsync(SapConnectionConfig cfg, CancellationToken ct)
    {
        var token = await FetchCsrfTokenAsync(cfg, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(token.Token)
            ? new SapConnectionTestResult(false, null, token.ErrorMessage ?? "CSRF token could not be fetched.")
            : new SapConnectionTestResult(true, token.Token, null);
    }

    /// <summary>
    /// Tests connectivity using the connection supplied in the constructor.
    /// </summary>
    public Task<SapConnectionTestResult> TestConnectionAsync(CancellationToken ct) => TestConnectionAsync(_connection, ct);

    /// <summary>
    /// Fetches a CSRF token and preserves SAP Gateway session cookies.
    /// </summary>
    /// <param name="connection">The SAP OData connection configuration.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The fetched token result.</returns>
    public async Task<CsrfTokenFetchResult> FetchCsrfTokenAsync(SapConnectionConfig connection, CancellationToken ct)
    {
        var rootUri = new Uri(connection.GetRootUrl(), UriKind.Absolute);
        var result = await SendTokenRequestAsync(HttpMethod.Get, rootUri, "application/json", connection, ct)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.Token) || result.HttpStatusCode == 401)
        {
            return result;
        }

        var metadataUri = new Uri(connection.GetMetadataUrl(), UriKind.Absolute);
        var fallback = await SendTokenRequestAsync(HttpMethod.Get, metadataUri, "application/json", connection, ct)
            .ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(fallback.Token) ? fallback : result;
    }

    private async Task<CsrfTokenFetchResult> SendTokenRequestAsync(HttpMethod method, Uri uri, string accept,
        SapConnectionConfig connection, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        string? body = null;
        try
        {
            using var message = new HttpRequestMessage(method, uri);
            AddCommonHeaders(message, connection);
            AddCookieHeader(message, uri);
            message.Headers.TryAddWithoutValidation(CsrfHeaderName, "Fetch");
            message.Headers.Accept.ParseAdd(accept);

            response = await _httpClient.SendAsync(message, ct).ConfigureAwait(false);
            CaptureCookies(uri, response);
            var token = GetHeader(response, CsrfHeaderName);
            if (response.Content != null)
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            return new CsrfTokenFetchResult(token, (int) response.StatusCode,
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Authentifizierung fehlgeschlagen"
                    : response.IsSuccessStatusCode ? null : $"HTTP {(int) response.StatusCode} {response.ReasonPhrase}",
                body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CsrfTokenFetchResult(null, response == null ? null : (int) response.StatusCode, ex.Message, body);
        }
    }

    private static HttpClientHandler CreateHttpClientHandler(SapConnectionConfig connection)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true
        };
        try
        {
            handler.SslProtocols = SslProtocols.Tls12;
        }
        catch (PlatformNotSupportedException)
        {
        }
        if (connection.IgnoreCertificateErrors)
        {
            Trace.TraceWarning("TLS-Validierung deaktiviert");
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    }

    /// <summary>
    /// Creates an HTTP client handler using the same security defaults as the uploader.
    /// </summary>
    public static HttpClientHandler CreateHandler(SapConnectionConfig connection) => CreateHttpClientHandler(connection);

    private static void AddIfNotEmpty(HttpRequestMessage message, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private void AddCommonHeaders(HttpRequestMessage message, SapConnectionConfig connection)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthParameter(connection));
    }

    private static string BuildBasicAuthParameter(SapConnectionConfig connection)
    {
        var password = SapCredentialStore.UnprotectPassword(connection.EncryptedPassword);
        byte[]? credentials = null;
        try
        {
            credentials = Encoding.UTF8.GetBytes($"{connection.User}:{password}");
            return Convert.ToBase64String(credentials);
        }
        finally
        {
            if (credentials != null)
            {
                ZeroMemory(credentials);
            }
            password = string.Empty;
        }
    }

    private static void ZeroMemory(byte[] bytes)
    {
#if NET6_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        CryptographicOperations.ZeroMemory(bytes);
#else
        Array.Clear(bytes, 0, bytes.Length);
#endif
    }

    private void AddCookieHeader(HttpRequestMessage message, Uri uri)
    {
        var cookieHeader = _cookieContainer.GetCookieHeader(uri);
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }
    }

    private void CaptureCookies(Uri uri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }
        foreach (var value in values)
        {
            try
            {
                _cookieContainer.SetCookies(uri, value);
            }
            catch (CookieException)
            {
            }
        }
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private static string? ExtractDocId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(body!);
            if (!doc.RootElement.TryGetProperty("d", out var d))
            {
                return null;
            }
            foreach (var name in new[] { "DocId", "ArchiveDocId", "ArchivDocId" })
            {
                if (d.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    var value = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static ParsedSapError ParseError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ParsedSapError(null, null, null, Array.Empty<SapErrorDetail>());
        }
        try
        {
            using var doc = JsonDocument.Parse(body!);
            if (!doc.RootElement.TryGetProperty("error", out var error))
            {
                return new ParsedSapError(null, null, null, Array.Empty<SapErrorDetail>());
            }
            var code = TryGetString(error, "code");
            string? message = null;
            if (error.TryGetProperty("message", out var messageElement))
            {
                message = TryGetString(messageElement, "value") ??
                          (messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() : null);
            }
            string? transactionId = null;
            var details = new List<SapErrorDetail>();
            if (error.TryGetProperty("innererror", out var innerError))
            {
                transactionId = TryGetString(innerError, "transactionid");
                if (innerError.TryGetProperty("errordetails", out var errorDetails) &&
                    errorDetails.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in errorDetails.EnumerateArray())
                    {
                        details.Add(new SapErrorDetail(
                            TryGetString(detail, "code") ?? string.Empty,
                            TryGetString(detail, "message") ?? string.Empty,
                            TryGetString(detail, "severity") ?? string.Empty));
                    }
                }
            }
            return new ParsedSapError(code, message, transactionId, details);
        }
        catch (JsonException)
        {
            return new ParsedSapError(null, null, null, Array.Empty<SapErrorDetail>());
        }
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    /// <summary>
    /// Releases the HTTP client this uploader created. Does nothing when the client was supplied by the
    /// caller, who keeps ownership of it.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the HTTP client this uploader created.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }
        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private record ParsedSapError(string? ErrorCode, string? ErrorMessage, string? TransactionId,
        IReadOnlyList<SapErrorDetail> Details);

    /// <summary>
    /// Sends the document in chunks so the caller can show how much of it has gone out. A plain
    /// <see cref="ByteArrayContent" /> hands everything to the socket in one call, which leaves the
    /// progress bar sitting still for the whole transfer of a large scan.
    /// </summary>
    private sealed class ProgressByteArrayContent : HttpContent
    {
        private const int BufferSize = 64 * 1024;
        private readonly byte[] _bytes;
        private readonly Action<int> _onPercentSent;

        public ProgressByteArrayContent(byte[] bytes, Action<int> onPercentSent)
        {
            _bytes = bytes;
            _onPercentSent = onPercentSent;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var total = Math.Max(1, _bytes.Length);
            for (var offset = 0; offset < _bytes.Length; offset += BufferSize)
            {
                var count = Math.Min(BufferSize, _bytes.Length - offset);
                await stream.WriteAsync(_bytes, offset, count).ConfigureAwait(false);
                _onPercentSent((int) ((offset + (long) count) * 100 / total));
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }
    }
}

/// <summary>
/// Result of fetching a CSRF token from SAP Gateway.
/// </summary>
public record CsrfTokenFetchResult(string? Token, int? HttpStatusCode, string? ErrorMessage, string? RawResponseBody);
