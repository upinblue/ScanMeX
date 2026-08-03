using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
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
    private const string SharePointTargetName = "SharePoint";

    private readonly OperationProgress _operationProgress;
    private readonly ISaveNotify _notify;
    private readonly SapArchivePostScanService _sapArchivePostScanService;

    public DocumentUploadService(Naps2Config config, OperationProgress operationProgress, ISaveNotify notify)
    {
        _operationProgress = operationProgress;
        _notify = notify;
        _sapArchivePostScanService = new SapArchivePostScanService(config, operationProgress);
    }

    /// <summary>
    /// Whether the profile sends documents anywhere at all.
    /// </summary>
    public static bool HasAnyTarget(ScanProfile? profile) =>
        profile?.UploadsToSharePoint() == true || profile?.UploadsToSap() == true;

    /// <summary>
    /// Uploads a document to every target its profile enables. Updates the document's status and message,
    /// notifies the operator of the outcome, and removes the staging file when the profile doesn't keep
    /// a local copy.
    /// </summary>
    public async Task<bool> UploadAsync(PendingDocument document)
    {
        document.Status = DocumentUploadStatus.Uploading;
        document.Message = null;
        ScanConsole.Upload($"Uploading '{document.FileName}' ({document.PageCount} page(s)).");

        var failures = new List<string>();
        var reachedTargets = new List<string>();
        if (document.Profile.UploadsToSharePoint())
        {
            var error = await UploadToSharePointAsync(document);
            if (error != null)
            {
                failures.Add($"{SharePointTargetName}: {error}");
            }
            else
            {
                reachedTargets.Add(SharePointTargetName);
            }
        }
        if (document.Profile.UploadsToSap())
        {
            var error = await UploadToSapAsync(document);
            if (error != null)
            {
                failures.Add($"{UiStrings.SapArchiveLink}: {error}");
            }
            else
            {
                reachedTargets.Add(UiStrings.SapArchiveLink);
            }
        }

        if (failures.Count > 0)
        {
            document.Status = DocumentUploadStatus.Failed;
            document.Message = string.Join(" | ", failures);
            ScanConsole.Upload($"FAILED '{document.FileName}': {document.Message}");
            // Uploading is the last step of a scan, so its outcome has to be as visible as a saved file.
            _notify.DocumentUploadFailed(document.FileName, document.Message);
            return false;
        }

        document.Status = DocumentUploadStatus.Uploaded;
        document.Message = null;
        if (reachedTargets.Count > 0)
        {
            ScanConsole.Upload($"OK '{document.FileName}' -> {string.Join(", ", reachedTargets)}");
            _notify.DocumentUploaded(document.FileName, string.Join(", ", reachedTargets));
        }
        if (document.DeleteFileAfterUpload)
        {
            ScanConsole.Upload($"Removing staged file '{document.FilePath}' (profile keeps no local copy).");
            TryDeleteFile(document.FilePath);
        }
        return true;
    }

    private async Task<string?> UploadToSharePointAsync(PendingDocument document)
    {
        try
        {
            var settings = ResolveSharePointSettings(document.Profile.SharePointUploadSettings!, document.Context);
            ScanConsole.Upload(
                $"SharePoint target: Site='{settings.SiteUrl}', Library='{settings.LibraryNameOrPath}', " +
                $"Folder='{settings.FolderPath}'");
            var uploadOp = new UploadSharePointOperation(new SharePointUploadService());
            if (uploadOp.Start(settings, document.FilePath, document.FileName))
            {
                // See the note in SapArchivePostScanService: one upload per document must not mean one
                // modal dialog per document.
                _operationProgress.ShowBackgroundProgress(uploadOp);
                if (!await uploadOp.Success)
                {
                    return uploadOp.FailureMessage ?? UiStrings.SharePointUploadFailedShort;
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
            return await _sapArchivePostScanService.UploadSavedFileAsync(
                document.Profile, document.FilePath, document.Context.Images, document.Context);
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
