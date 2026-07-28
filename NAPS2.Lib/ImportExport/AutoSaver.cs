using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
using NAPS2.ImportExport.Images;
using NAPS2.Pdf;
using NAPS2.Scan;
using NAPS2.SharePoint;
using NAPS2.Sap;

namespace NAPS2.ImportExport;

public class AutoSaver
{
    private readonly ErrorOutput _errorOutput;
    private readonly DialogHelper _dialogHelper;
    private readonly OperationProgress _operationProgress;
    private readonly ISaveNotify _notify;
    private readonly PdfExporter _pdfExporter;
    private readonly IOverwritePrompt _overwritePrompt;
    private readonly Naps2Config _config;
    private readonly ImageContext _imageContext;
    private readonly UiImageList _imageList;
    private readonly SharePointUploadService _sharePointUploadService = new();
    private readonly SapArchivePostScanService _sapArchivePostScanService;

    // The active ScanProfile associated with the current auto-save session.
    public ScanProfile? ActiveProfile { get; set; }

    public AutoSaver(ErrorOutput errorOutput, DialogHelper dialogHelper,
        OperationProgress operationProgress, ISaveNotify notify, PdfExporter pdfExporter,
        IOverwritePrompt overwritePrompt, Naps2Config config, ImageContext imageContext, UiImageList imageList)
    {
        _errorOutput = errorOutput;
        _dialogHelper = dialogHelper;
        _operationProgress = operationProgress;
        _notify = notify;
        _pdfExporter = pdfExporter;
        _overwritePrompt = overwritePrompt;
        _config = config;
        _imageContext = imageContext;
        _imageList = imageList;
        _sapArchivePostScanService = new SapArchivePostScanService(config, operationProgress);
    }

    // Overload: pass the ScanProfile explicitly, ensuring SharePoint settings are available.
    public IAsyncEnumerable<ProcessedImage> Save(ScanProfile profile, AutoSaveSettings settings, IAsyncEnumerable<ProcessedImage> images)
    {
        ActiveProfile = profile;
        return Save(settings, images);
    }

    public IAsyncEnumerable<ProcessedImage> Save(AutoSaveSettings settings, IAsyncEnumerable<ProcessedImage> images)
    {
        return AsyncProducers.RunProducer<ProcessedImage>(async produceImage =>
        {
            var imageList = new List<ProcessedImage>();
            try
            {
                await foreach (var img in images)
                {
                    imageList.Add(img);
                    if (!settings.ClearImagesAfterSaving)
                    {
                        produceImage(img.Clone());
                    }
                }
            }
            finally
            {
                if (!await InternalSave(settings, imageList) && settings.ClearImagesAfterSaving)
                {
                    // Fallback in case auto save failed; pipe all the images back at once
                    foreach (var img in imageList)
                    {
                        produceImage(img);
                    }
                }
                else
                {
                    foreach (var img in imageList)
                    {
                        img.Dispose();
                    }
                }
            }
        });
    }

    private async Task<bool> InternalSave(AutoSaveSettings settings, List<ProcessedImage> images)
    {
        try
        {
            bool ok = true;
            var placeholders = Placeholders.All.WithDate(DateTime.Now);
            int i = 0;
            string? firstFileSaved = null;
            // Use extended separator that supports barcode separation with an optional regex
            var scans = SaveSeparatorHelper.SeparateScans(new[] { images }, ActiveProfile, settings).ToList();
            foreach (var imagesToSave in scans)
            {
                (bool success, string? filePath) =
                    await SaveOneFile(settings, placeholders, i++, imagesToSave, scans.Count == 1);
                if (success)
                {
                    // Normally we're supposed to take the CurrentState before the save operation starts, but that
                    // doesn't really work here since populating the UiImageList happens asynchronously so the images
                    // we're saving might not be present yet. In practice waiting until after saving will ensure the
                    // list is populated so that this logic works correctly.
                    _imageList.MarkSaved(_imageList.CurrentState, imagesToSave);
                    firstFileSaved ??= filePath;
                }
                else
                {
                    ok = false;
                }
            }
            // TODO: Shouldn't this give duplicate notifications?
            if (scans.Count > 1 && ok)
            {
                // Can't just do images.Count because that includes patch codes
                int imageCount = scans.SelectMany(x => x).Count();
                _notify.ImagesSaved(imageCount, firstFileSaved!);
            }
            return ok;
        }
        catch (Exception ex)
        {
            Log.ErrorException(MiscResources.AutoSaveError, ex);
            _errorOutput.DisplayError(MiscResources.AutoSaveError, ex);
            return false;
        }
    }

    private async Task<(bool, string?)> SaveOneFile(AutoSaveSettings settings, Placeholders placeholders, int i,
        List<ProcessedImage> images, bool doNotify)
    {
        if (images.Count == 0)
        {
            return (true, null);
        }
        var ctx = CreateScanContext(settings.FilePath, i, images);
        string subPath = ResolveAutoSavePath(settings.FilePath, placeholders, ctx);
        if (subPath.Contains("$(", StringComparison.Ordinal))
        {
            _errorOutput.DisplayError($"Unaufgel�ster Platzhalter: {settings.FilePath}");
            return (false, null);
        }
        if (settings.PromptForFilePath)
        {
            string? newPath = null!;
            if (Invoker.Current.InvokeGet(() => _dialogHelper.PromptToSavePdfOrImage(subPath, out newPath)))
            {
                ctx = CreateScanContext(newPath!, i, images);
                subPath = ResolveAutoSavePath(newPath!, placeholders, ctx);
                if (subPath.Contains("$(", StringComparison.Ordinal))
                {
                    _errorOutput.DisplayError($"Unaufgel�ster Platzhalter: {newPath}");
                    return (false, null);
                }
            }
            else
            {
                return (false, null);
            }
        }
        // TODO: This placeholder handling is complex and wrong in some cases (e.g. FilePerScan with ext = "jpg")
        // TODO: Maybe have initial placeholders that replace date, then rely on the ops to increment the file num
        var extension = Path.GetExtension(subPath);
        if (extension != null && extension.Equals(".pdf", StringComparison.InvariantCultureIgnoreCase))
        {
            if (File.Exists(subPath))
            {
                subPath = placeholders.Substitute(subPath, true, 0, 1);
            }
            var op = new SavePdfOperation(_pdfExporter, _overwritePrompt);
            if (op.Start(subPath, placeholders, images, _config.Get(c => c.PdfSettings), _config.DefaultOcrParams()))
            {
                _operationProgress.ShowProgress(op);
            }
            bool success = await op.Success;
            if (success && doNotify)
            {
                _notify.PdfSaved(subPath);
            }

            if (success && settings.UploadToSharePoint && ActiveProfile?.SharePointUploadSettings != null)
            {
                try
                {
                    var fileName = Path.GetFileName(subPath);
                    var sharePointSettings = ResolveSharePointSettings(ActiveProfile.SharePointUploadSettings, ctx);
                    var uploader = new SharePointUploadService();
                    var uploadOp = new UploadSharePointOperation(uploader);
                    if (uploadOp.Start(sharePointSettings, subPath, fileName))
                    {
                        _operationProgress.ShowProgress(uploadOp);
                        await uploadOp.Success;
                    }
                }
                catch (Exception ex)
                {
                    _errorOutput.DisplayError($"Auto Save succeeded, but SharePoint upload failed: {ex.Message}");
                }
            }

            if (success && settings.UploadToSap && ActiveProfile?.SapArchiveSettings != null)
            {
                try
                {
                    await _sapArchivePostScanService.UploadSavedFileAsync(ActiveProfile, subPath, images, ctx);
                }
                catch (Exception ex)
                {
                    Log.ErrorException("SAP ArchiveLink upload failed", ex);
                    _errorOutput.DisplayError(SapUi.UploadFailed(ex.Message));
                }
            }

            return (success, subPath);
        }
        else
        {
            var op = new SaveImagesOperation(_overwritePrompt, _imageContext);
            if (op.Start(subPath, placeholders, images, _config.Get(c => c.ImageSettings)))
            {
                _operationProgress.ShowProgress(op);
            }
            bool success = await op.Success;
            if (success && doNotify && op.FirstFileSaved != null)
            {
                _notify.ImagesSaved(images.Count, op.FirstFileSaved);
            }
            return (success, subPath);
        }
    }

    private ScanContext CreateScanContext(string template, int index, List<ProcessedImage> images)
    {
        var ext = Path.GetExtension(template).TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = "pdf";
        }
        return new ScanContext
        {
            Timestamp = DateTime.Now,
            SequenceIndex = index,
            Profile = ActiveProfile ?? new ScanProfile(),
            Images = images,
            Barcodes = new BarcodeExtractor().Extract(images),
            SeparatorBarcodeValue = images.FirstOrDefault()?.PostProcessingData.Barcode.IsPatchT == true
                ? images.First().PostProcessingData.Barcode.DetectedText
                : null,
            OutputExtension = ext,
            FileFormat = ext
        };
    }

    private string ResolveAutoSavePath(string template, Placeholders placeholders, ScanContext ctx)
    {
        if (ActiveProfile == null)
        {
            return placeholders.Substitute(template, true, ctx.SequenceIndex);
        }
        return new FileNamePlaceholders().SubstitutePlaceholders(template, ctx, autoIncrement: true);
    }

    private static SharePointUploadSettings ResolveSharePointSettings(SharePointUploadSettings settings, ScanContext ctx)
    {
        var placeholders = new FileNamePlaceholders();
        return new SharePointUploadSettings
        {
            SiteUrl = SubstituteUploadSetting(settings.SiteUrl, placeholders, ctx),
            LibraryNameOrPath = SubstituteUploadSetting(settings.LibraryNameOrPath, placeholders, ctx),
            FolderPath = SubstituteUploadSetting(settings.FolderPath, placeholders, ctx),
            TenantId = settings.TenantId,
            ClientId = settings.ClientId,
            ClientSecret = settings.ClientSecret
        };
    }

    private static string? SubstituteUploadSetting(string? value, FileNamePlaceholders placeholders, ScanContext ctx)
    {
        return string.IsNullOrWhiteSpace(value) ? value : placeholders.SubstitutePlaceholders(value, ctx);
    }
}