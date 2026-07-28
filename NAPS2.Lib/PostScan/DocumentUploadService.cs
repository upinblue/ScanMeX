using NAPS2.EtoForms;
using NAPS2.ImportExport;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.SharePoint;

namespace NAPS2.PostScan;

/// <summary>
/// The single path documents take to the target systems, used by both the automatic and the manual
/// upload trigger so the two can't drift apart.
/// </summary>
public class DocumentUploadService
{
    private readonly OperationProgress _operationProgress;
    private readonly SapArchivePostScanService _sapArchivePostScanService;

    public DocumentUploadService(Naps2Config config, OperationProgress operationProgress)
    {
        _operationProgress = operationProgress;
        _sapArchivePostScanService = new SapArchivePostScanService(config, operationProgress);
    }

    /// <summary>
    /// Whether the profile sends documents anywhere at all.
    /// </summary>
    public static bool HasAnyTarget(ScanProfile? profile, AutoSaveSettings? settings) =>
        (settings?.UploadToSharePoint == true && profile?.SharePointUploadSettings != null) ||
        (settings?.UploadToSap == true && profile?.SapArchiveSettings != null);

    /// <summary>
    /// Uploads a document to every target its profile enables. Updates the document's status and message,
    /// and removes the staging file when the profile doesn't keep a local copy.
    /// </summary>
    public async Task<bool> UploadAsync(PendingDocument document)
    {
        var settings = document.Profile.AutoSaveSettings;
        document.Status = DocumentUploadStatus.Uploading;
        document.Message = null;

        var failures = new List<string>();
        if (settings?.UploadToSharePoint == true && document.Profile.SharePointUploadSettings != null)
        {
            var error = await UploadToSharePointAsync(document);
            if (error != null)
            {
                failures.Add(error);
            }
        }
        if (settings?.UploadToSap == true && document.Profile.SapArchiveSettings != null)
        {
            var error = await UploadToSapAsync(document);
            if (error != null)
            {
                failures.Add(error);
            }
        }

        if (failures.Count > 0)
        {
            document.Status = DocumentUploadStatus.Failed;
            document.Message = string.Join(" | ", failures);
            return false;
        }

        document.Status = DocumentUploadStatus.Uploaded;
        document.Message = null;
        if (document.DeleteFileAfterUpload)
        {
            TryDeleteFile(document.FilePath);
        }
        return true;
    }

    private async Task<string?> UploadToSharePointAsync(PendingDocument document)
    {
        try
        {
            var settings = ResolveSharePointSettings(document.Profile.SharePointUploadSettings!, document.Context);
            var uploadOp = new UploadSharePointOperation(new SharePointUploadService());
            if (uploadOp.Start(settings, document.FilePath, document.FileName))
            {
                _operationProgress.ShowProgress(uploadOp);
                if (!await uploadOp.Success)
                {
                    return UiStrings.SharePointUploadFailedShort;
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.ErrorException("SharePoint upload failed", ex);
            return ex.Message;
        }
    }

    private async Task<string?> UploadToSapAsync(PendingDocument document)
    {
        try
        {
            var ok = await _sapArchivePostScanService.UploadSavedFileAsync(
                document.Profile, document.FilePath, document.Context.Images, document.Context);
            return ok ? null : SapUi.UploadFailed(string.Empty);
        }
        catch (Exception ex)
        {
            Log.ErrorException("SAP ArchiveLink upload failed", ex);
            return ex.Message;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.ErrorException($"Could not delete staged document {path}", ex);
        }
    }

    private static SharePointUploadSettings ResolveSharePointSettings(
        SharePointUploadSettings settings, ScanContext ctx)
    {
        var placeholders = new FileNamePlaceholders();
        return new SharePointUploadSettings
        {
            SiteUrl = Substitute(settings.SiteUrl, placeholders, ctx),
            LibraryNameOrPath = Substitute(settings.LibraryNameOrPath, placeholders, ctx),
            FolderPath = Substitute(settings.FolderPath, placeholders, ctx),
            TenantId = settings.TenantId,
            ClientId = settings.ClientId,
            ClientSecret = settings.ClientSecret
        };
    }

    private static string? Substitute(string? value, FileNamePlaceholders placeholders, ScanContext ctx) =>
        string.IsNullOrWhiteSpace(value) ? value : placeholders.SubstitutePlaceholders(value, ctx);
}
