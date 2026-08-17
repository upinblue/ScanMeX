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
    /// <remarks>
    /// Virtual so <see cref="ImportExport.AutoSaver"/> and
    /// <see cref="EtoForms.Desktop.DocumentUploadController"/> can be tested against a stand-in. Both of
    /// them do something with the outcome -- queueing a failure for retry, clearing the window on success
    /// -- that is worth pinning down without reaching a real SharePoint or SAP system.
    /// </remarks>
    public virtual async Task<bool> UploadAsync(ScannedDocument document)
    {
        document.Status = DocumentStatus.Working;
        document.Message = null;
        document.CompletedTargets.Clear();
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

        // Recorded whether or not everything worked: one target failing does not undo the other, and the
        // document list has to be able to say which half got through.
        document.CompletedTargets.AddRange(reachedTargets);

        if (failures.Count > 0)
        {
            document.Status = DocumentStatus.Failed;
            document.Message = string.Join(" | ", failures);
            ScanConsole.Upload($"FAILED '{document.FileName}': {document.Message}");
            // Uploading is the last step of a scan, so its outcome has to be as visible as a saved file.
            _notify.DocumentUploadFailed(document.FileName, document.Message);
            return false;
        }

        document.Status = DocumentStatus.Done;
        document.Message = null;
        if (reachedTargets.Count > 0)
        {
            ScanConsole.Upload($"OK '{document.FileName}' -> {string.Join(", ", reachedTargets)}");
            _notify.DocumentUploaded(document.FileName, string.Join(", ", reachedTargets));
        }
        return true;
    }

    /// <summary>
    /// Sends the document to SharePoint. Returns null on success, otherwise why it failed.
    /// </summary>
    /// <remarks>
    /// Virtual together with <see cref="UploadToSapAsync"/> so a test can drive
    /// <see cref="UploadAsync"/> with a chosen outcome per target. What matters there is what happens
    /// around the two calls -- that one target failing still lets the other run, that the failures are
    /// reported together, and that the staging file survives a failure -- and none of that should need a
    /// reachable SharePoint tenant to check.
    /// </remarks>
    protected virtual async Task<string?> UploadToSharePointAsync(ScannedDocument document)
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

    /// <summary>
    /// Sends the document to SAP ArchiveLink. Returns null on success, otherwise why it failed.
    /// See <see cref="UploadToSharePointAsync"/> for why this is virtual.
    /// </summary>
    protected virtual async Task<string?> UploadToSapAsync(ScannedDocument document)
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
