using System;
using System.Net.Http;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace NAPS2.Sap;

/// <summary>
/// SAP ArchiveLink uploader using the HTTP Content Server protocol for content upload and RFC for connection insert.
/// </summary>
public class HttpSapArchiveUploader : ISapArchiveUploader
{
    private readonly HttpClient _httpClient;
    private readonly RfcSapArchiveUploader _rfcUploader;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpSapArchiveUploader" /> class.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client. If omitted, one is created per uploader.</param>
    /// <param name="rfcClientFactory">Optional RFC client factory used for the connection-insert step.</param>
    public HttpSapArchiveUploader(HttpClient? httpClient = null, ISapRfcClientFactory? rfcClientFactory = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
        _rfcUploader = new RfcSapArchiveUploader(rfcClientFactory);
    }

    /// <inheritdoc />
    public async Task<SapUploadResult> UploadAsync(SapUploadRequest request, CancellationToken ct)
    {
        ValidateRequest(request);
        ct.ThrowIfCancellationRequested();

        var archivDocId = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var uploadUri = BuildCreateUri(request.Connection.ContentServerBaseUrl!, request.Profile.ArchiveId!, archivDocId);

        try
        {
            using var content = new ByteArrayContent(request.DocumentBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(request.MimeType) ? "application/octet-stream" : request.MimeType);

            using var response = await _httpClient.PutAsync(uploadUri, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new SapUploadResult(false, archivDocId,
                    $"SAP Content Server upload failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body}",
                    response.StatusCode.ToString());
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            return new SapUploadResult(false, archivDocId, ex.Message, ex.GetType().Name);
        }

        return request.Connection.ConnectionInsertMode switch
        {
            ConnectionInsertMode.StandardRfc => await _rfcUploader.InsertExistingArchiveDocumentAsync(request, archivDocId, ct).ConfigureAwait(false),
            ConnectionInsertMode.CustomRfc => await _rfcUploader.InsertExistingArchiveDocumentWithCustomRfcAsync(request, archivDocId, ct).ConfigureAwait(false),
            _ => new SapUploadResult(false, archivDocId, "Unsupported ConnectionInsertMode.", "UNSUPPORTED_CONNECTION_INSERT_MODE")
        };
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient();
    }

    /// <summary>
    /// Creates an HTTP client that can explicitly ignore certificate errors when configured.
    /// </summary>
    /// <param name="ignoreCertificateErrors">Whether certificate validation errors should be ignored.</param>
    /// <returns>A configured HTTP client.</returns>
    public static HttpClient CreateHttpClient(bool ignoreCertificateErrors)
    {
        if (!ignoreCertificateErrors)
        {
            return new HttpClient();
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None || ignoreCertificateErrors
        };
        return new HttpClient(handler);
    }

    private static Uri BuildCreateUri(string baseUrl, string archiveId, string archivDocId)
    {
        var separator = baseUrl.TrimEnd('/');
        var url = $"{separator}/?create&contRep={Uri.EscapeDataString(archiveId)}&docId={Uri.EscapeDataString(archivDocId)}&compId=data&pVersion=0046";
        return new Uri(url, UriKind.Absolute);
    }

    private static void ValidateRequest(SapUploadRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        if (request.Connection == null)
        {
            throw new ArgumentException("Connection is required.", nameof(request));
        }
        if (request.Profile == null)
        {
            throw new ArgumentException("Profile is required.", nameof(request));
        }
        var contentServerBaseUrl = request.Connection.ContentServerBaseUrl;
        if (string.IsNullOrWhiteSpace(contentServerBaseUrl))
        {
            throw new ArgumentException("ContentServerBaseUrl is required for HTTP Content Server upload.", nameof(request));
        }
        if (request.Connection.UseHttps && !contentServerBaseUrl!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("ContentServerBaseUrl must use HTTPS unless UseHttps is disabled.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.ObjectKey))
        {
            throw new ArgumentException("ObjectKey is required for SAP ArchiveLink upload.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Profile.ArchiveId))
        {
            throw new ArgumentException("Profile.ArchiveId is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Profile.ArDocType))
        {
            throw new ArgumentException("Profile.ArDocType is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Profile.SapObjectType))
        {
            throw new ArgumentException("Profile.SapObjectType is required.", nameof(request));
        }
        if (request.DocumentBytes == null)
        {
            throw new ArgumentException("DocumentBytes is required.", nameof(request));
        }
    }
}
