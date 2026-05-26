using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NAPS2.Sap;

/// <summary>
/// Lightweight diagnostics for SAP ArchiveLink connectivity.
/// </summary>
public static class SapArchiveDiagnostics
{
    /// <summary>
    /// Tests the configured SAP ArchiveLink connection without uploading a document.
    /// </summary>
    public static async Task<SapUploadResult> TestConnectionAsync(SapConnectionConfig connection, CancellationToken ct = default)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        return connection.ConnectionMode == ConnectionMode.HttpContentServer
            ? await TestHttpContentServerAsync(connection, ct).ConfigureAwait(false)
            : TestRfc(connection, ct);
    }

    private static SapUploadResult TestRfc(SapConnectionConfig connection, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var client = new NcoSapRfcClientFactory().Create(connection);
            var ping = client.CreateFunction("RFC_PING");
            ping.Invoke();
            return new SapUploadResult(true, null, null, null);
        }
        catch (SapRfcException ex)
        {
            return new SapUploadResult(false, null, ex.Message, ex.Key);
        }
    }

    private static async Task<SapUploadResult> TestHttpContentServerAsync(SapConnectionConfig connection, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connection.ContentServerBaseUrl))
        {
            return new SapUploadResult(false, null, "ContentServerBaseUrl is required.", "CONTENT_SERVER_URL_MISSING");
        }
        if (connection.UseHttps && !connection.ContentServerBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new SapUploadResult(false, null, "ContentServerBaseUrl must use HTTPS unless UseHttps is disabled.", "HTTPS_REQUIRED");
        }

        try
        {
            using var httpClient = HttpSapArchiveUploader.CreateHttpClient(connection.IgnoreCertificateErrors);
            using var request = new HttpRequestMessage(HttpMethod.Head, connection.ContentServerBaseUrl);
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new SapUploadResult(true, null, null, null)
                : new SapUploadResult(false, null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", response.StatusCode.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SapUploadResult(false, null, ex.Message, ex.GetType().Name);
        }
    }
}
