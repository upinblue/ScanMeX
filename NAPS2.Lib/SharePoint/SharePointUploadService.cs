using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using NAPS2.Scan;

namespace NAPS2.SharePoint;

/// <summary>
/// Uploads files to SharePoint Online using Microsoft Graph with app-only (client credentials) auth.
/// </summary>
public class SharePointUploadService
{
    private readonly HttpClient _httpClient;

    public SharePointUploadService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    // Convenience overload: upload using SharePointUploadSettings directly.
    public Task UploadFileAsync(
        SharePointUploadSettings sp,
        string localFilePath,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        => UploadCoreAsync(sp, localFilePath, fileName, progress, cancellationToken);

    public Task UploadFileAsync(
        ScanProfile profile,
        string localFilePath,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (profile?.AutoSaveSettings?.UploadToSharePoint != true && profile?.EnableSharePointUpload != true)
        {
            Debug.WriteLine("[SP] Upload skipped: SharePoint upload not enabled in profile flags.");
            return Task.CompletedTask; // Not enabled
        }
        var sp = profile.SharePointUploadSettings ?? throw new InvalidOperationException("SharePoint settings are incomplete. (SharePointUploadSettings is null)");
        return UploadCoreAsync(sp, localFilePath, fileName, progress, cancellationToken);
    }

    private static string Mask(string? value, int show = 4)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= show ? new string('*', value.Length) : $"{value.Substring(0, show)}...({value.Length} chars)";
    }

    private static string Truncate(string s, int max = 1024)
        => s.Length <= max ? s : s.Substring(0, max) + $"... (truncated, {s.Length} chars)";

    private static string ExtractGraphError(JsonElement root)
    {
        // Graph errors shape: {"error":{"code":"","message":"","innerError":{"date":...,"request-id":...,"client-request-id":...}}}
        if (root.TryGetProperty("error", out var err))
        {
            var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (err.TryGetProperty("innerError", out var inner))
            {
                var reqId = inner.TryGetProperty("request-id", out var r) ? r.GetString() : null;
                var clientReqId = inner.TryGetProperty("client-request-id", out var cr) ? cr.GetString() : null;
                var date = inner.TryGetProperty("date", out var d) ? d.GetString() : null;
                return $"code={code ?? "?"}, message={msg ?? "?"}, request-id={reqId ?? "?"}, client-request-id={clientReqId ?? "?"}, date={date ?? "?"}";
            }
            return $"code={code ?? "?"}, message={msg ?? "?"}";
        }
        return "(no error object)";
    }

    private static async Task<string> FormatGraphFailureAsync(HttpResponseMessage resp, string endpoint, CancellationToken cancellationToken)
    {
        var status = (int)resp.StatusCode + " " + resp.ReasonPhrase;
        string bodyText = Truncate(await resp.Content.ReadAsStringAsync(cancellationToken));
        string details;
        try
        {
            using var doc = JsonDocument.Parse(bodyText);
            details = ExtractGraphError(doc.RootElement);
        }
        catch
        {
            details = bodyText;
        }
        return $"{status} at {endpoint}. Details: {details}";
    }

    private async Task UploadCoreAsync(
        SharePointUploadSettings sp,
        string localFilePath,
        string fileName,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        void Report(int value)
        {
            value = Math.Clamp(value, 0, 99); // final 100 reported on success
            progress?.Report(value);
        }

        Report(0);
        Debug.WriteLine("[SP] Starting upload");
        Debug.WriteLine($"[SP] localFilePath='{localFilePath}', exists={File.Exists(localFilePath)}");
        Debug.WriteLine($"[SP] fileName='{fileName}'");
        Debug.WriteLine($"[SP] Settings: SiteUrl='{sp.SiteUrl}', Library='{sp.LibraryNameOrPath}', Folder='{sp.FolderPath}', TenantId='{Mask(sp.TenantId)}', ClientId='{Mask(sp.ClientId)}', ClientSecretLen={(sp.ClientSecret?.Length ?? 0)}");

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(sp.SiteUrl)) missing.Add(nameof(sp.SiteUrl));
        if (string.IsNullOrWhiteSpace(sp.LibraryNameOrPath)) missing.Add(nameof(sp.LibraryNameOrPath));
        if (string.IsNullOrWhiteSpace(sp.TenantId)) missing.Add(nameof(sp.TenantId));
        if (string.IsNullOrWhiteSpace(sp.ClientId)) missing.Add(nameof(sp.ClientId));
        if (string.IsNullOrWhiteSpace(sp.ClientSecret)) missing.Add(nameof(sp.ClientSecret));
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"SharePoint settings are incomplete. Missing: {string.Join(", ", missing)}");
        }
        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Local file to upload was not found.", localFilePath);
        }

        var tokenUrl = $"https://login.microsoftonline.com/{sp.TenantId}/oauth2/v2.0/token";
        Debug.WriteLine($"[SP] Acquiring token at: {tokenUrl}");
        string token = await AcquireTokenAsync(sp.TenantId!, sp.ClientId!, sp.ClientSecret!, cancellationToken);
        Debug.WriteLine($"[SP] Token acquired. Length={token.Length}");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Report(10);

        // Parse site URL
        var uri = new Uri(sp.SiteUrl!);
        string hostname = uri.Host; // e.g. tenant.sharepoint.com
        string sitePath = uri.AbsolutePath.Trim('/'); // e.g. sites/Invoices
        if (string.IsNullOrEmpty(sitePath)) sitePath = "sites/root"; // fallback

        var siteEndpoint = $"https://graph.microsoft.com/v1.0/sites/{hostname}:/{sitePath}";
        Debug.WriteLine($"[SP] Resolving site: {siteEndpoint}");
        var siteResp = await _httpClient.GetAsync(siteEndpoint, cancellationToken);
        if (!siteResp.IsSuccessStatusCode)
        {
            var msg = await FormatGraphFailureAsync(siteResp, siteEndpoint, cancellationToken);
            Debug.WriteLine($"[SP] Site resolve failed: {msg}");
            throw new InvalidOperationException($"SharePoint site resolution failed: {msg}");
        }
        using var siteJson = JsonDocument.Parse(await siteResp.Content.ReadAsStringAsync(cancellationToken));
        string siteId = siteJson.RootElement.GetProperty("id").GetString()!;
        Debug.WriteLine($"[SP] Site resolved. siteId='{siteId}'");
        Report(30);

        // Resolve drive (document library)
        var drivesEndpoint = $"https://graph.microsoft.com/v1.0/sites/{siteId}/drives";
        Debug.WriteLine("[SP] Listing document libraries...");
        var drivesResp = await _httpClient.GetAsync(drivesEndpoint, cancellationToken);
        if (!drivesResp.IsSuccessStatusCode)
        {
            var msg = await FormatGraphFailureAsync(drivesResp, drivesEndpoint, cancellationToken);
            Debug.WriteLine($"[SP] Drives list failed: {msg}");
            throw new InvalidOperationException($"SharePoint drives list failed: {msg}");
        }
        using var drivesJson = JsonDocument.Parse(await drivesResp.Content.ReadAsStringAsync(cancellationToken));
        string? driveId = null;
        foreach (var drive in drivesJson.RootElement.GetProperty("value").EnumerateArray())
        {
            var name = drive.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
            var webUrl = drive.TryGetProperty("webUrl", out var wProp) ? wProp.GetString() : null;
            if (string.Equals(name, sp.LibraryNameOrPath, StringComparison.OrdinalIgnoreCase) ||
                (webUrl != null && webUrl.Contains(sp.LibraryNameOrPath!, StringComparison.OrdinalIgnoreCase)))
            {
                driveId = drive.GetProperty("id").GetString();
                break;
            }
        }
        driveId ??= drivesJson.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault().GetProperty("id").GetString();
        Debug.WriteLine($"[SP] DriveId selected='{driveId}'");
        if (string.IsNullOrEmpty(driveId))
        {
            throw new InvalidOperationException("Could not resolve target document library.");
        }
        Report(40);

        // Determine folder segment
        string folderSegment = string.IsNullOrWhiteSpace(sp.FolderPath) ? "root" : $"root:/{sp.FolderPath!.Trim('/')}";
        string uploadUrl = $"https://graph.microsoft.com/v1.0/sites/{siteId}/drives/{driveId}/{folderSegment}:/{fileName}:/content";
        Debug.WriteLine($"[SP] Upload URL: {uploadUrl}");

        using var fs = File.OpenRead(localFilePath);
        var content = new ProgressStreamContent(fs, "application/pdf", fs.Length, percent =>
        {
            // Map upload progress (0..100) to overall 40..100
            var mapped = 40 + (int)Math.Round(percent / 100.0 * 60.0);
            Report(mapped);
        });
        var putResp = await _httpClient.PutAsync(uploadUrl, content, cancellationToken);
        if (!putResp.IsSuccessStatusCode)
        {
            var msg = await FormatGraphFailureAsync(putResp, uploadUrl, cancellationToken);
            Debug.WriteLine($"[SP] Upload failed: {msg}");
            throw new InvalidOperationException($"SharePoint upload failed: {msg}");
        }
        Debug.WriteLine("[SP] Upload succeeded");

        progress?.Report(100);
    }

    private async Task<string> AcquireTokenAsync(string tenantId, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "https://graph.microsoft.com/.default"
        };
        var endpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var resp = await _httpClient.PostAsync(endpoint, new FormUrlEncodedContent(form), cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var status = (int)resp.StatusCode + " " + resp.ReasonPhrase;
            var body = Truncate(await resp.Content.ReadAsStringAsync(cancellationToken));
            string details;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                // Token endpoint errors: {"error":"","error_description":"","error_codes":[...],"trace_id":"","correlation_id":"","timestamp":""}
                var err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                var desc = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
                var trace = root.TryGetProperty("trace_id", out var t) ? t.GetString() : null;
                var corr = root.TryGetProperty("correlation_id", out var c) ? c.GetString() : null;
                details = $"error={err ?? "?"}, description={desc ?? "?"}, trace_id={trace ?? "?"}, correlation_id={corr ?? "?"}";
            }
            catch
            {
                details = body;
            }
            throw new InvalidOperationException($"Token acquisition failed: {status} at {endpoint}. Details: {details}");
        }
        using var okDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        return okDoc.RootElement.GetProperty("access_token").GetString()!;
    }

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly long _total;
        private readonly Action<int> _progress;
        private const int BufferSize = 64 * 1024;

        public ProgressStreamContent(Stream stream, string mediaType, long total, Action<int> progress)
        {
            _stream = stream;
            _total = Math.Max(1, total);
            _progress = progress;
            Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }

        protected override async Task SerializeToStreamAsync(Stream target, TransportContext? context)
        {
            var buffer = new byte[BufferSize];
            long uploaded = 0;
            int read;
            while ((read = await _stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                uploaded += read;
                var percent = (int)Math.Round(uploaded * 100.0 / _total);
                if (percent >= 0 && percent <= 100)
                {
                    _progress(percent);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _total;
            return true;
        }
    }
}
