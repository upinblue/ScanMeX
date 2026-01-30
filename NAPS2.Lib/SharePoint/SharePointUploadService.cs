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

    private async Task UploadCoreAsync(
        SharePointUploadSettings sp,
        string localFilePath,
        string fileName,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0);

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

        // Parse site URL
        var uri = new Uri(sp.SiteUrl!);
        string hostname = uri.Host; // e.g. tenant.sharepoint.com
        string sitePath = uri.AbsolutePath.Trim('/'); // e.g. sites/Invoices
        if (string.IsNullOrEmpty(sitePath)) sitePath = "sites/root"; // fallback

        Debug.WriteLine($"[SP] Resolving site: host='{hostname}', path='/{sitePath}'");
        // Resolve site
        var siteResp = await _httpClient.GetAsync($"https://graph.microsoft.com/v1.0/sites/{hostname}:/{sitePath}", cancellationToken);
        if (!siteResp.IsSuccessStatusCode)
        {
            var body = await siteResp.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[SP] Site resolve failed: {siteResp.StatusCode} {body}");
            throw new InvalidOperationException($"Unable to resolve SharePoint site: {siteResp.StatusCode} {body}");
        }
        using var siteJson = JsonDocument.Parse(await siteResp.Content.ReadAsStringAsync(cancellationToken));
        string siteId = siteJson.RootElement.GetProperty("id").GetString()!;
        Debug.WriteLine($"[SP] Site resolved. siteId='{siteId}'");

        // Resolve drive (document library)
        Debug.WriteLine("[SP] Listing document libraries...");
        var drivesResp = await _httpClient.GetAsync($"https://graph.microsoft.com/v1.0/sites/{siteId}/drives", cancellationToken);
        if (!drivesResp.IsSuccessStatusCode)
        {
            var body = await drivesResp.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[SP] Drives list failed: {drivesResp.StatusCode} {body}");
            throw new InvalidOperationException($"Unable to list document libraries: {drivesResp.StatusCode} {body}");
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

        // Determine folder segment
        string folderSegment = string.IsNullOrWhiteSpace(sp.FolderPath) ? "root" : $"root:/{sp.FolderPath!.Trim('/')}";
        string uploadUrl = $"https://graph.microsoft.com/v1.0/sites/{siteId}/drives/{driveId}/{folderSegment}:/{fileName}:/content";
        Debug.WriteLine($"[SP] Upload URL: {uploadUrl}");

        using var fs = File.OpenRead(localFilePath);
        var content = new StreamContent(fs);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var putResp = await _httpClient.PutAsync(uploadUrl, content, cancellationToken);
        if (!putResp.IsSuccessStatusCode)
        {
            var body = await putResp.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[SP] Upload failed: {putResp.StatusCode} {body}");
            throw new InvalidOperationException($"Upload failed: {putResp.StatusCode} {body}");
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
        var resp = await _httpClient.PostAsync($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", new FormUrlEncodedContent(form), cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine($"[SP] Token acquisition failed: {resp.StatusCode} {body}");
            throw new InvalidOperationException($"Token acquisition failed: {resp.StatusCode} {body}");
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }
}
