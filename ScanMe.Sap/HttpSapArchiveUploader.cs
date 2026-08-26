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
    /// The error code reported when the document was sent but SAP did not answer in time.
    /// </summary>
    public const string TimeoutErrorCode = "TIMEOUT";

    /// <summary>
    /// The error code reported when the gateway did not answer the sign-in request in time.
    /// </summary>
    public const string SignInTimeoutErrorCode = "SIGNIN_TIMEOUT";

    /// <summary>
    /// Percentage reached once the CSRF token is in hand. The token round trip is a real wait against a
    /// slow gateway, so it gets a visible share of the bar rather than being folded into "starting".
    /// </summary>
    private const int AuthenticatedPercent = 20;

    /// <summary>
    /// Percentage reached once every byte has been handed to the socket.
    /// </summary>
    /// <remarks>
    /// A small share on purpose. This number counts bytes handed to <c>Stream.WriteAsync</c>, which is
    /// how much sits in the socket buffer, not how much SAP has -- for a document of any ordinary size it
    /// is reached in milliseconds. It used to be 90, so the bar crossed seventy points in a single tick
    /// and then stood still for the whole archiving, which is the part that actually takes the time. The
    /// caller carries the bar the rest of the way while it waits; see the crawl in
    /// <c>UploadSapArchiveOperation</c>.
    /// </remarks>
    private const int SentPercent = 45;

    /// <summary>
    /// How many times an upload is attempted when the connection fails outright. A timeout is deliberately
    /// not one of those cases; see <see cref="UploadAsync(SapUploadRequest,IProgress{SapUploadProgress},CancellationToken)" />.
    /// </summary>
    private const int MaxAttempts = 3;

    private readonly SapConnectionConfig _connection;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly CookieContainer _cookieContainer;
    private bool _disposed;

    /// <summary>
    /// Receives a line of diagnostic text for every step of an upload. Null discards them.
    /// </summary>
    /// <remarks>
    /// A callback rather than a logger because this assembly targets netstandard2.0 and cannot reference
    /// <c>NAPS2.Lib</c>; the caller hangs <c>ScanConsole.Upload</c> on it. Without this the HTTP half of
    /// the upload was the one step of the scan chain that reported nothing at all, so a timeout at a
    /// customer site could not be traced to the sending or to SAP's own processing -- which is exactly
    /// the difference that decides what to do about it.
    /// </remarks>
    public Action<string>? DiagnosticLog { get; set; }

    private void Log(string message) => DiagnosticLog?.Invoke(message);

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
        DisableClientTimeout(_httpClient);
        _cookieContainer = cookieContainer ?? new CookieContainer();
    }

    private HttpSapArchiveUploader(SapConnectionConfig connection, HttpMessageHandler handler, bool disposeHttpClient)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _cookieContainer = new CookieContainer();
        _httpClient = new HttpClient(handler, disposeHttpClient);
        _ownsHttpClient = disposeHttpClient;
        DisableClientTimeout(_httpClient);
    }

    /// <inheritdoc />
    public Task<SapUploadResult> UploadAsync(SapUploadRequest request, CancellationToken ct) =>
        UploadAsync(request, null, ct);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Each request gets its own deadline from <see cref="SapConnectionConfig.GetUploadTimeout" /> rather
    /// than relying on <see cref="HttpClient.Timeout" />, which is disabled here. Two reasons: the two
    /// waits need different lengths -- a gateway that won't hand out a token in half a minute is
    /// unreachable, while an upload can legitimately run for minutes -- and a deadline this class owns is
    /// one it can tell apart from the operator pressing Cancel. It could not before, because
    /// <c>HttpClient</c> reports its own timeout as <see cref="TaskCanceledException" />, which derives
    /// from <see cref="OperationCanceledException" />: a timeout while signing in was reported to the
    /// operator as "upload cancelled".
    /// </para>
    /// <para>
    /// <b>A timeout is not retried.</b> A connection that never came up carried nothing, so trying again
    /// is free; a request that timed out was received in full as far as anyone here knows, and SAP may
    /// well be archiving it at that moment. Retrying used to send the whole document twice more --
    /// measured: three complete uploads, then a failure message -- which is up to three copies filed
    /// under one barcode and indistinguishable afterwards from a scan done three times. The failure says
    /// the document may already be in SAP instead.
    /// </para>
    /// </remarks>
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

        var uploadTimeout = request.Connection.GetUploadTimeout();
        var totalWatch = Stopwatch.StartNew();
        Log($"SAP upload of '{request.FileName}' started: {DescribeSize(request.DocumentBytes.Length)}, " +
            $"barcode '{request.Barcode}', sign-in timeout {Seconds(request.Connection.GetConnectTimeout())}, " +
            $"upload timeout {Seconds(uploadTimeout)}.");

        Report(SapUploadStage.Preparing, 0);
        Report(SapUploadStage.Authenticating, 5);
        var signInWatch = Stopwatch.StartNew();
        var csrf = await FetchCsrfTokenAsync(request.Connection, ct).ConfigureAwait(false);
        signInWatch.Stop();
        if (csrf.TimedOut)
        {
            Log($"Signing in to SAP timed out after {Elapsed(signInWatch)}. The gateway at " +
                $"'{request.Connection.Host}' did not answer; nothing was uploaded.");
            return new SapUploadResult(false, null, null, null, SignInTimeoutErrorCode,
                $"SAP did not answer the sign-in request within {Seconds(request.Connection.GetConnectTimeout())}.",
                null, null, Array.Empty<SapErrorDetail>());
        }
        if (string.IsNullOrWhiteSpace(csrf.Token))
        {
            Log($"No CSRF token after {Elapsed(signInWatch)} (HTTP {Describe(csrf.HttpStatusCode)}): " +
                $"{csrf.ErrorMessage ?? "no reason given"}. Nothing was uploaded.");
            return new SapUploadResult(false, csrf.HttpStatusCode, null, null, null,
                "CSRF-Token konnte nicht ermittelt werden", null, csrf.RawResponseBody, Array.Empty<SapErrorDetail>());
        }
        Log($"Signed in to SAP after {Elapsed(signInWatch)} (HTTP {Describe(csrf.HttpStatusCode)}).");
        Report(SapUploadStage.Uploading, AuthenticatedPercent);

        var uploadUri = new Uri(request.Connection.GetUploadUrl(), UriKind.Absolute);
        var token = csrf.Token!;
        var csrfRetryUsed = false;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var trace = new AttemptTrace();
            var attemptWatch = Stopwatch.StartNew();
            // Linked so the operator's Cancel still gets through, and separate so an expiry of ours can be
            // told apart from it below.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(uploadTimeout);
            try
            {
                using var message = BuildUploadMessage(request, uploadUri, token, progress, trace, attemptWatch);
                using var response = await _httpClient.SendAsync(message, deadline.Token).ConfigureAwait(false);
                CaptureCookies(uploadUri, response);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = CreateUploadResult(response, body);
                attemptWatch.Stop();
                Log($"SAP answered HTTP {(int) response.StatusCode} for '{request.FileName}' after " +
                    $"{Elapsed(attemptWatch)} ({DescribeSplit(trace, attemptWatch)}).");
                if (!result.Success && !csrfRetryUsed && IsCsrfFailure(response, result))
                {
                    csrfRetryUsed = true;
                    Log("SAP rejected the CSRF token; fetching a new one and sending the document again.");
                    Report(SapUploadStage.Authenticating, AuthenticatedPercent);
                    var refreshed = await FetchCsrfTokenAsync(request.Connection, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(refreshed.Token))
                    {
                        token = refreshed.Token!;
                        continue;
                    }
                    Log("A new CSRF token could not be fetched either; giving up on this document.");
                }
                if (result.Success)
                {
                    Log($"'{request.FileName}' archived as ArchivDocId '{result.ArchivDocId ?? "(none returned)"}' " +
                        $"in {Elapsed(totalWatch)} overall.");
                }
                else
                {
                    Log($"SAP refused '{request.FileName}': code '{result.ErrorCode ?? "(none)"}', " +
                        $"message '{result.ErrorMessage ?? "(none)"}', transaction '{result.TransactionId ?? "(none)"}'.");
                }
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The operator pressed Cancel. Not ours to turn into a failed upload.
                Log($"The upload of '{request.FileName}' was cancelled after {Elapsed(attemptWatch)}.");
                throw;
            }
            catch (OperationCanceledException)
            {
                attemptWatch.Stop();
                Log(DescribeTimeout(request, trace, attemptWatch, uploadTimeout));
                return new SapUploadResult(false, null, null, null, TimeoutErrorCode,
                    $"SAP did not answer within {Seconds(uploadTimeout)}.", null, null,
                    Array.Empty<SapErrorDetail>());
            }
            catch (HttpRequestException ex)
            {
                attemptWatch.Stop();
                // The connection failed rather than expired, so nothing usable reached SAP and sending it
                // again cannot duplicate anything.
                if (attempt >= MaxAttempts)
                {
                    Log($"Attempt {attempt} of {MaxAttempts} for '{request.FileName}' failed after " +
                        $"{Elapsed(attemptWatch)}: {ex.Message}. Giving up.");
                    return new SapUploadResult(false, null, null, null, ex.GetType().Name, ex.Message, null, null,
                        Array.Empty<SapErrorDetail>());
                }
                var delay = attempt == 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3);
                Log($"Attempt {attempt} of {MaxAttempts} for '{request.FileName}' failed after " +
                    $"{Elapsed(attemptWatch)}: {ex.Message}. Retrying in {Seconds(delay)}.");
                Report(SapUploadStage.Retrying, AuthenticatedPercent);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        Log($"'{request.FileName}' was not uploaded: {MaxAttempts} attempts, none of them accepted.");
        return new SapUploadResult(false, null, null, null, "UPLOAD_FAILED", "SAP upload failed.", null, null,
            Array.Empty<SapErrorDetail>());
    }

    /// <summary>
    /// Says where the deadline expired, which is the one thing that decides what to do about it: still
    /// sending means the link is too slow for the document, sent and waiting means SAP is the slow part
    /// -- and that the document may already be in the archive.
    /// </summary>
    private static string DescribeTimeout(SapUploadRequest request, AttemptTrace trace, Stopwatch watch,
        TimeSpan timeout)
    {
        var total = request.DocumentBytes.Length;
        if (trace.SentAt == null)
        {
            return $"Timed out after {Elapsed(watch)} (limit {Seconds(timeout)}) while still sending " +
                   $"'{request.FileName}': {DescribeSize(trace.BytesSent)} of {DescribeSize(total)} handed over. " +
                   "The link to SAP is too slow for a document this size, or the connection stalled. " +
                   "Nothing was archived.";
        }
        var waited = watch.Elapsed - trace.SentAt.Value;
        return $"Timed out after {Elapsed(watch)} (limit {Seconds(timeout)}): '{request.FileName}' " +
               $"({DescribeSize(total)}) was sent in full after {Format(trace.SentAt.Value)}, then SAP did not " +
               $"answer for {Format(waited)}. SAP had the whole document, so it may already be archived -- " +
               "check there before uploading it again.";
    }

    private static string DescribeSplit(AttemptTrace trace, Stopwatch watch)
    {
        if (trace.SentAt == null)
        {
            return "sending time not measured";
        }
        var waited = watch.Elapsed - trace.SentAt.Value;
        return $"{Format(trace.SentAt.Value)} sending, {Format(waited)} waiting for SAP";
    }

    private HttpRequestMessage BuildUploadMessage(SapUploadRequest request, Uri uploadUri, string token,
        IProgress<SapUploadProgress>? progress, AttemptTrace trace, Stopwatch watch)
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
        // Always the counting content, progress callback or not: how far the document got is what says
        // where a timeout struck, and that has to be known whether or not anyone is watching a bar.
        message.Content = new ProgressByteArrayContent(request.DocumentBytes,
            (sentBytes, sentPercent) =>
            {
                trace.BytesSent = sentBytes;
                progress?.Report(new SapUploadProgress(SapUploadStage.Uploading,
                    AuthenticatedPercent + sentPercent * (SentPercent - AuthenticatedPercent) / 100));
            },
            () =>
            {
                trace.SentAt = watch.Elapsed;
                // Reported here rather than after SendAsync returns, which is where it used to be -- by
                // then SAP has already answered and the wait the operator was meant to be told about is
                // over. This is also the moment the caller starts carrying the bar on its own.
                progress?.Report(new SapUploadProgress(SapUploadStage.WaitingForSap, SentPercent));
            });
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(
            request.OverrideMimeType ?? SapMimeTypeResolver.Resolve(request.FileName));
        return message;
    }

    /// <summary>
    /// What one attempt did, so a timeout can say where it struck.
    /// </summary>
    private sealed class AttemptTrace
    {
        public long BytesSent;
        public TimeSpan? SentAt;
    }

    private static string Describe(int? httpStatusCode) => httpStatusCode?.ToString() ?? "(no response)";

    private static string Elapsed(Stopwatch watch) => Format(watch.Elapsed);

    private static string Format(TimeSpan value) => $"{value.TotalSeconds:0.0} s";

    private static string Seconds(TimeSpan value) => $"{value.TotalSeconds:0} s";

    private static string DescribeSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{bytes / 1024.0:0.0} kB";

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

    /// <summary>
    /// Hands the deadline to this class instead of the client.
    /// </summary>
    /// <remarks>
    /// <see cref="HttpClient.Timeout" /> is one value for every request and reports its expiry as a
    /// <see cref="TaskCanceledException" />, which is indistinguishable from the operator cancelling.
    /// Both matter here, so each request carries its own linked token with its own deadline instead.
    /// Throws once the client has sent a request, which only a caller-supplied client can have done; its
    /// own timeout then stays in force and simply caps ours.
    /// </remarks>
    private static void DisableClientTimeout(HttpClient httpClient)
    {
        try
        {
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <inheritdoc />
    public async Task<SapConnectionTestResult> TestConnectionAsync(SapConnectionConfig cfg, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var token = await FetchCsrfTokenAsync(cfg, ct).ConfigureAwait(false);
        watch.Stop();
        if (string.IsNullOrWhiteSpace(token.Token))
        {
            Log($"SAP connection test to '{cfg.Host}' failed after {Elapsed(watch)}: " +
                $"{token.ErrorMessage ?? "no CSRF token returned"}.");
            return new SapConnectionTestResult(false, null, token.ErrorMessage ?? "CSRF token could not be fetched.");
        }
        Log($"SAP connection test to '{cfg.Host}' succeeded after {Elapsed(watch)}.");
        return new SapConnectionTestResult(true, token.Token, null);
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
        // A timeout is the gateway not answering at all -- asking a second URL on the same host would only
        // spend the deadline again for the same answer.
        if (!string.IsNullOrWhiteSpace(result.Token) || result.HttpStatusCode == 401 || result.TimedOut)
        {
            return result;
        }

        Log($"The service root gave no CSRF token ({result.ErrorMessage ?? "no reason given"}); trying $metadata.");
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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(connection.GetConnectTimeout());
        try
        {
            using var message = new HttpRequestMessage(method, uri);
            AddCommonHeaders(message, connection);
            AddCookieHeader(message, uri);
            message.Headers.TryAddWithoutValidation(CsrfHeaderName, "Fetch");
            message.Headers.Accept.ParseAdd(accept);

            response = await _httpClient.SendAsync(message, deadline.Token).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The operator pressed Cancel, which is the one case that really is a cancellation.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Ours expired. Reported as a timeout rather than left to propagate: it used to escape as a
            // TaskCanceledException -- an OperationCanceledException by inheritance -- straight past this
            // handler and out of UploadAsync, where the caller reported it to the operator as "upload
            // cancelled".
            return new CsrfTokenFetchResult(null, null,
                $"SAP did not answer within {Seconds(connection.GetConnectTimeout())}.", null, TimedOut: true);
        }
        catch (Exception ex)
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
        private readonly Action<long, int> _onSent;
        private readonly Action _onCompleted;

        public ProgressByteArrayContent(byte[] bytes, Action<long, int> onSent, Action onCompleted)
        {
            _bytes = bytes;
            _onSent = onSent;
            _onCompleted = onCompleted;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var total = Math.Max(1, _bytes.Length);
            for (var offset = 0; offset < _bytes.Length; offset += BufferSize)
            {
                var count = Math.Min(BufferSize, _bytes.Length - offset);
                await stream.WriteAsync(_bytes, offset, count).ConfigureAwait(false);
                var sent = offset + (long) count;
                _onSent(sent, (int) (sent * 100 / total));
            }
            // Outside the loop so an empty document reaches it too, and so the caller is told the sending
            // is over at the moment it is over rather than when SAP eventually answers.
            _onCompleted();
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
/// <param name="Token">The token, or null if none was returned.</param>
/// <param name="HttpStatusCode">The status the gateway answered with, or null if it did not answer.</param>
/// <param name="ErrorMessage">Why no token was returned.</param>
/// <param name="RawResponseBody">The response body, for diagnosis.</param>
/// <param name="TimedOut">
/// Whether the gateway failed to answer within the connection's sign-in timeout. Kept apart from the
/// other failures because it is the one the operator must not be shown as a cancellation.
/// </param>
public record CsrfTokenFetchResult(string? Token, int? HttpStatusCode, string? ErrorMessage, string? RawResponseBody,
    bool TimedOut = false);
