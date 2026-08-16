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
            ScanConsole.Upload("[SP] Upload skipped: SharePoint upload not enabled in profile flags.");
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

    internal static string? CombineFolders(params string?[] parts)
    {
        var segments = new List<string>();
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            foreach (var seg in p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = seg.Trim();
                if (s.Length > 0) segments.Add(s);
            }
        }
        return segments.Count == 0 ? null : string.Join('/', segments);
    }

    private static string EncodePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var encoded = string.Join('/', path.Split('/')
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => Uri.EscapeDataString(s)));
        return encoded;
    }

    /// <summary>
    /// Builds the Graph URL that writes the file's content.
    /// </summary>
    /// <remarks>
    /// Graph addresses a drive item by path as <c>root:/{folder}/{name}:/content</c>: one colon opens the
    /// path expression and one closes it, with the folders and the file name forming a single path in
    /// between. Ending the folder with its own colon produces <c>root:/{folder}:/{name}:/content</c>,
    /// which Graph reads as a second path expression and rejects -- so uploading to the library root
    /// worked while uploading into a subfolder failed with "invalid request". Keep the two joined by a
    /// plain slash.
    /// </remarks>
    internal static string BuildUploadUrl(string siteId, string driveId, string? folderPath, string fileName)
    {
        var encodedFileName = Uri.EscapeDataString(fileName);
        var itemPath = string.IsNullOrWhiteSpace(folderPath)
            ? encodedFileName
            : $"{EncodePath(folderPath!)}/{encodedFileName}";
        return $"https://graph.microsoft.com/v1.0/sites/{siteId}/drives/{driveId}/root:/{itemPath}:/content";
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
        ScanConsole.Upload("[SP] Starting upload");
        ScanConsole.Upload($"[SP] localFilePath='{localFilePath}', exists={File.Exists(localFilePath)}");
        ScanConsole.Upload($"[SP] fileName='{fileName}'");
        ScanConsole.Upload($"[SP] Settings: SiteUrl='{sp.SiteUrl}', Library='{sp.LibraryNameOrPath}', Folder='{sp.FolderPath}', TenantId='{Mask(sp.TenantId)}', ClientId='{Mask(sp.ClientId)}', ClientSecretLen={(sp.ClientSecret?.Length ?? 0)}");

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
        ScanConsole.Upload($"[SP] Acquiring token at: {tokenUrl}");
        string token = await AcquireTokenAsync(sp.TenantId!, sp.ClientId!, sp.ClientSecret!, cancellationToken);
        ScanConsole.Upload($"[SP] Token acquired. Length={token.Length}");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Report(10);

        // Parse site URL
        var uri = new Uri(sp.SiteUrl!);
        string hostname = uri.Host; // e.g. tenant.sharepoint.com
        string sitePath = uri.AbsolutePath.Trim('/'); // e.g. sites/Invoices
        if (string.IsNullOrEmpty(sitePath)) sitePath = "sites/root"; // fallback

        var siteEndpoint = $"https://graph.microsoft.com/v1.0/sites/{hostname}:/{sitePath}";
        ScanConsole.Upload($"[SP] Resolving site: {siteEndpoint}");
        var siteResp = await _httpClient.GetAsync(siteEndpoint, cancellationToken);
        if (!siteResp.IsSuccessStatusCode)
        {
            var msg = await FormatGraphFailureAsync(siteResp, siteEndpoint, cancellationToken);
            ScanConsole.Upload($"[SP] Site resolve failed: {msg}");
            throw new InvalidOperationException($"SharePoint site resolution failed: {msg}");
        }
        using var siteJson = JsonDocument.Parse(await siteResp.Content.ReadAsStringAsync(cancellationToken));
        string siteId = siteJson.RootElement.GetProperty("id").GetString()!;
        ScanConsole.Upload($"[SP] Site resolved. siteId='{siteId}'");
        Report(30);

        // Parse library and optional subpath from LibraryNameOrPath
        var libInput = sp.LibraryNameOrPath!.Trim().Trim('/');
        string libraryName = libInput;
        string? librarySubPath = null;
        var slashIdx = libInput.IndexOf('/');
        if (slashIdx >= 0)
        {
            libraryName = libInput.Substring(0, slashIdx).Trim();
            librarySubPath = libInput.Substring(slashIdx + 1).Trim('/');
        }
        ScanConsole.Upload($"[SP] Parsed library='{libraryName}', librarySubPath='{librarySubPath ?? ""}'");

        // Resolve drive (document library)
        var drivesEndpoint = $"https://graph.microsoft.com/v1.0/sites/{siteId}/drives";
        ScanConsole.Upload("[SP] Listing document libraries...");
        var drivesResp = await _httpClient.GetAsync(drivesEndpoint, cancellationToken);
        if (!drivesResp.IsSuccessStatusCode)
        {
            var msg = await FormatGraphFailureAsync(drivesResp, drivesEndpoint, cancellationToken);
            ScanConsole.Upload($"[SP] Drives list failed: {msg}");
            throw new InvalidOperationException($"SharePoint drives list failed: {msg}");
        }
        using var drivesJson = JsonDocument.Parse(await drivesResp.Content.ReadAsStringAsync(cancellationToken));
        string? driveId = null;
        string? driveName = null;
        string? driveWebUrl = null;

        var drives = drivesJson.RootElement.GetProperty("value").EnumerateArray().ToList();
        foreach (var drive in drives)
        {
            var name = drive.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
            var webUrl = drive.TryGetProperty("webUrl", out var wProp) ? wProp.GetString() : null;

            bool match = false;
            if (!string.IsNullOrEmpty(name) && string.Equals(name, libraryName, StringComparison.OrdinalIgnoreCase))
            {
                match = true;
            }
            else if (!string.IsNullOrEmpty(webUrl))
            {
                try
                {
                    var wuri = new Uri(webUrl);
                    var lastSeg = wuri.Segments.Length > 0 ? wuri.Segments[^1].Trim('/') : string.Empty;
                    var decodedLast = Uri.UnescapeDataString(lastSeg);
                    if (string.Equals(decodedLast, libraryName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                    }
                }
                catch { /* ignore parsing issues */ }
            }

            if (match)
            {
                driveId = drive.GetProperty("id").GetString();
                driveName = name;
                driveWebUrl = webUrl;
                break;
            }
        }

        if (string.IsNullOrEmpty(driveId))
        {
            if (drives.Count == 1)
            {
                // Fall back to the only drive
                var drive = drives[0];
                driveId = drive.GetProperty("id").GetString();
                driveName = drive.TryGetProperty("name", out var n) ? n.GetString() : null;
                driveWebUrl = drive.TryGetProperty("webUrl", out var w) ? w.GetString() : null;
                ScanConsole.Upload($"[SP] Library '{libraryName}' not found. Defaulting to the only available library: '{driveName}' ({driveWebUrl})");
            }
            else if (drives.Count > 1)
            {
                // Keep previous behavior (first drive), but log all available to help troubleshoot
                var first = drives.FirstOrDefault();
                if (first.ValueKind != JsonValueKind.Undefined)
                {
                    driveId = first.GetProperty("id").GetString();
                    driveName = first.TryGetProperty("name", out var n) ? n.GetString() : null;
                    driveWebUrl = first.TryGetProperty("webUrl", out var w) ? w.GetString() : null;
                }
                var available = string.Join(", ", drives.Select(d =>
                {
                    var n = d.TryGetProperty("name", out var np) ? np.GetString() : "?";
                    var w = d.TryGetProperty("webUrl", out var wp) ? wp.GetString() : "?";
                    return $"{n} ({w})";
                }));
                // A typo in the library name lands the document in whichever library happens to come
                // first, which afterwards looks exactly like a correct upload to the wrong place.
                ScanConsole.Upload(
                    $"[SP] WARNING: library '{libraryName}' was not found, so documents go to '{driveName}' " +
                    $"instead. Available libraries: {available}");
            }
        }

        ScanConsole.Upload($"[SP] Drive selected id='{driveId}', name='{driveName}', webUrl='{driveWebUrl}'");
        if (string.IsNullOrEmpty(driveId))
        {
            throw new InvalidOperationException("Could not resolve target document library.");
        }
        Report(40);

        // Determine the item path combining library subpath, configured folder path and file name.
        var combinedFolder = CombineFolders(librarySubPath, sp.FolderPath);
        string uploadUrl = BuildUploadUrl(siteId, driveId!, combinedFolder, fileName);
        ScanConsole.Upload(
            $"[SP] Uploading into folder '{combinedFolder ?? "(library root)"}' as '{fileName}'.");
        ScanConsole.Upload($"[SP] Upload URL: {uploadUrl}");

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
            ScanConsole.Upload($"[SP] Upload failed: {msg}");
            throw new InvalidOperationException($"SharePoint upload failed: {msg}");
        }

        string? itemWebUrl = null;
        try
        {
            using var putJson = JsonDocument.Parse(await putResp.Content.ReadAsStringAsync(cancellationToken));
            itemWebUrl = putJson.RootElement.TryGetProperty("webUrl", out var w) ? w.GetString() : null;
        }
        catch { /* ignore parse */ }

        if (!string.IsNullOrEmpty(itemWebUrl))
        {
            ScanConsole.Upload($"[SP] Upload succeeded. Item webUrl: {itemWebUrl}");
        }
        else
        {
            ScanConsole.Upload("[SP] Upload succeeded");
        }

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
