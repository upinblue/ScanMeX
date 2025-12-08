using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NAPS2.Scan;

namespace ScanMe.SharePoint;

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

    public async Task UploadFileAsync(
        ScanProfile profile,
        string localFilePath,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(0);

        if (profile?.AutoSaveSettings?.UploadToSharePoint != true)
        {
            return; // Not enabled
        }
        var sp = profile.SharePointUploadSettings;
        if (sp == null || string.IsNullOrWhiteSpace(sp.SiteUrl) || string.IsNullOrWhiteSpace(sp.LibraryNameOrPath)
            || string.IsNullOrWhiteSpace(sp.TenantId) || string.IsNullOrWhiteSpace(sp.ClientId) || string.IsNullOrWhiteSpace(sp.ClientSecret))
        {
            throw new InvalidOperationException("SharePoint settings are incomplete.");
        }
        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Local file to upload was not found.", localFilePath);
        }

        string token = await AcquireTokenAsync(sp.TenantId!, sp.ClientId!, sp.ClientSecret!, cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Parse site URL
        var uri = new Uri(sp.SiteUrl!);
        string hostname = uri.Host; // e.g. tenant.sharepoint.com
        string sitePath = uri.AbsolutePath.Trim('/'); // e.g. sites/Invoices
        if (string.IsNullOrEmpty(sitePath)) sitePath = "sites/root"; // fallback

        // Resolve site
        var siteResp = await _httpClient.GetAsync($"https://graph.microsoft.com/v1.0/sites/{hostname}:/{sitePath}", cancellationToken);
        if (!siteResp.IsSuccessStatusCode)
        {
            var body = await siteResp.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Unable to resolve SharePoint site: {siteResp.StatusCode} {body}");
        }
        using var siteJson = JsonDocument.Parse(await siteResp.Content.ReadAsStringAsync(cancellationToken));
        string siteId = siteJson.RootElement.GetProperty("id").GetString()!;

        // Resolve drive (document library)
        var drivesResp = await _httpClient.GetAsync($"https://graph.microsoft.com/v1.0/sites/{siteId}/drives", cancellationToken);
        if (!drivesResp.IsSuccessStatusCode)
        {
            var body = await drivesResp.Content.ReadAsStringAsync(cancellationToken);
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
        if (string.IsNullOrEmpty(driveId))
        {
            throw new InvalidOperationException("Could not resolve target document library.");
        }

        // Determine folder segment
        string folderSegment = string.IsNullOrWhiteSpace(sp.FolderPath) ? "root" : $"root:/{sp.FolderPath!.Trim('/')}";
        string uploadUrl = $"https://graph.microsoft.com/v1.0/sites/{siteId}/drives/{driveId}/{folderSegment}:/{fileName}:/content";

        using var fs = File.OpenRead(localFilePath);
        var content = new StreamContent(fs);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var putResp = await _httpClient.PutAsync(uploadUrl, content, cancellationToken);
        if (!putResp.IsSuccessStatusCode)
        {
            var body = await putResp.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Upload failed: {putResp.StatusCode} {body}");
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
        var resp = await _httpClient.PostAsync($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", new FormUrlEncodedContent(form), cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Token acquisition failed: {resp.StatusCode} {body}");
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }
}
