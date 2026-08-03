using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
using NAPS2.EtoForms.Ui;
using NAPS2.ImportExport.Images;
using NAPS2.Pdf;
using NAPS2.PostScan;
using NAPS2.Scan;

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
    private readonly DocumentUploadService _documentUploadService;
    private readonly DocumentUploadQueue? _uploadQueue;

    // The active ScanProfile associated with the current auto-save session.
    public ScanProfile? ActiveProfile { get; set; }

    public AutoSaver(ErrorOutput errorOutput, DialogHelper dialogHelper,
        OperationProgress operationProgress, ISaveNotify notify, PdfExporter pdfExporter,
        IOverwritePrompt overwritePrompt, Naps2Config config, ImageContext imageContext, UiImageList imageList,
        DocumentUploadQueue? uploadQueue = null, DocumentUploadService? documentUploadService = null)
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
        _uploadQueue = uploadQueue;
        _documentUploadService = documentUploadService ??
                                 new DocumentUploadService(config, operationProgress, notify);
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
            // Barcode separation carries the pattern-applied value of the barcode that started each
            // document, so $(barcode) resolves to that rather than to whatever code happens to be first.
            var scans = SeparateIntoDocuments(settings, images);
            foreach (var (imagesToSave, separatorValue) in scans)
            {
                (bool success, string? filePath) =
                    await SaveOneFile(settings, placeholders, i++, imagesToSave, scans.Count == 1, separatorValue);
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
                int imageCount = scans.Sum(x => x.Images.Count);
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

    /// <summary>
    /// Splits the scan into the documents that will each become one file. Barcode-driven separation goes
    /// through <see cref="DocumentSeparator"/>; everything else keeps the classic auto save separators.
    /// </summary>
    private List<(List<ProcessedImage> Images, string? SeparatorValue)> SeparateIntoDocuments(
        AutoSaveSettings settings, List<ProcessedImage> images)
    {
        var workflow = DocumentWorkflowSettings.ForProfile(ActiveProfile);
        if (workflow.SeparationMode != DocumentSeparationMode.None)
        {
            var separated = DocumentSeparator.Separate(images, workflow)
                .Select(x => (x.Images.ToList(), x.SeparatorBarcodeValue))
                .ToList();
            ScanConsole.Document(
                $"{workflow.SeparationMode} separation split {images.Count} page(s) into {separated.Count} document(s): " +
                string.Join(", ", separated.Select((x, i) =>
                    $"#{i + 1}={x.Item1.Count}p/'{x.SeparatorBarcodeValue ?? "(no barcode)"}'")));
            return separated;
        }
        var scans = SaveSeparatorHelper.SeparateScans(new[] { images }, ActiveProfile, settings)
            .Select(x => (x, (string?) null))
            .ToList();
        ScanConsole.Document(
            $"No barcode separation; auto save separator '{settings.Separator}' produced {scans.Count} document(s) " +
            $"from {images.Count} page(s).");
        return scans;
    }

    private async Task<(bool, string?)> SaveOneFile(AutoSaveSettings settings, Placeholders placeholders, int i,
        List<ProcessedImage> images, bool doNotify, string? separatorValue = null)
    {
        if (images.Count == 0)
        {
            ScanConsole.Document($"Document {i + 1} has no pages; nothing saved or uploaded.");
            return (true, null);
        }
        var workflow = DocumentWorkflowSettings.ForProfile(ActiveProfile);
        var ctx = CreateScanContext(settings.FilePath, i, images, separatorValue);
        // The identification number has to be known before the path is resolved so $(id) can be used
        // in the file name. Scanning has already finished at this point, so prompting is safe here.
        if (workflow.IdMode == DocumentIdMode.ManualInput)
        {
            var documentId = PromptForDocumentId(workflow, ctx, i);
            if (documentId == null)
            {
                ScanConsole.Document($"Document {i + 1}: identification cancelled, document not saved.");
                return (false, null);
            }
            ctx = ctx with { DocumentId = documentId };
            ScanConsole.Document($"Document {i + 1}: identification '{documentId}' entered.");
        }
        string subPath = ResolveAutoSavePath(settings.FilePath, placeholders, ctx);
        if (subPath.Contains("$(", StringComparison.Ordinal))
        {
            ScanConsole.Document(
                $"Document {i + 1}: unresolved placeholder in '{settings.FilePath}' (resolved to '{subPath}'); not saved.");
            _errorOutput.DisplayError(string.Format(UiStrings.UnresolvedPlaceholder, settings.FilePath));
            return (false, null);
        }
        ScanConsole.Document(
            $"Document {i + 1}: {images.Count} page(s), barcode '{ctx.SeparatorBarcodeValue ?? "(none)"}' -> '{subPath}'");
        if (settings.PromptForFilePath)
        {
            string? newPath = null!;
            if (Invoker.Current.InvokeGet(() => _dialogHelper.PromptToSavePdfOrImage(subPath, out newPath)))
            {
                ctx = CreateScanContext(newPath!, i, images, separatorValue) with { DocumentId = ctx.DocumentId };
                subPath = ResolveAutoSavePath(newPath!, placeholders, ctx);
                if (subPath.Contains("$(", StringComparison.Ordinal))
                {
                    _errorOutput.DisplayError(string.Format(UiStrings.UnresolvedPlaceholder, newPath));
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
            ScanConsole.Document(success
                ? $"Saved PDF '{subPath}'."
                : $"Saving PDF '{subPath}' failed: {DescribeFailure(op)}");
            // A file that only exists to be uploaded is deleted again afterwards, so a "saved" notification
            // would point at a path that no longer exists. The upload notification reports it instead.
            bool isStagingCopy = !workflow.KeepLocalCopy && DocumentUploadService.HasAnyTarget(ActiveProfile);
            if (success && doNotify && !isStagingCopy)
            {
                _notify.PdfSaved(subPath);
            }

            if (success)
            {
                await HandleUpload(workflow, ctx, subPath);
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
            ScanConsole.Document(success
                ? $"Saved image file(s), first is '{op.FirstFileSaved}'."
                : $"Saving image file(s) to '{subPath}' failed: {DescribeFailure(op)}");
            if (success && doNotify && op.FirstFileSaved != null)
            {
                _notify.ImagesSaved(images.Count, op.FirstFileSaved);
            }
            if (success && DocumentUploadService.HasAnyTarget(ActiveProfile))
            {
                // Uploading only covers the PDF output, since image output writes one file per page and
                // that doesn't map to one document. Say so rather than silently skipping the upload.
                ScanConsole.Upload(
                    "Upload skipped: the profile writes image files, and uploading only supports PDF output.");
                _notify.DocumentUploadFailed(Path.GetFileName(subPath), UiStrings.UploadRequiresPdf);
            }
            return (success, subPath);
        }
    }

    /// <summary>
    /// Why a save operation ended without success. A failed save is the single most common reason a scan
    /// never reaches SharePoint or SAP, so the console has to name the cause rather than just the outcome.
    /// Operations that fail before starting (an overwrite prompt the operator declined, a file held open
    /// by another program) never raise an error, which is why the status text is used as a fallback.
    /// </summary>
    private static string DescribeFailure(OperationBase op)
    {
        if (op.LastError != null)
        {
            return $"{op.LastError.ErrorMessage} ({op.LastError.Exception.Message})";
        }
        return $"cancelled or declined at '{op.Status?.StatusText}'";
    }

    /// <summary>
    /// Asks the operator for this document's identification number. Returns null if they cancelled,
    /// which aborts saving the document rather than filing it under a wrong or empty name.
    /// </summary>
    private string? PromptForDocumentId(DocumentWorkflowSettings workflow, ScanContext ctx, int index)
    {
        var description = string.Format(UiStrings.DocumentIdPromptDescription, index + 1, ctx.Images.Count);
        // Pre-fill with the barcode when there is one, so the operator only has to confirm.
        var suggested = ctx.SeparatorBarcodeValue ?? ctx.Barcodes.FirstOrDefault()?.Value;
        return Invoker.Current.InvokeGet(() =>
        {
            var form = new DocumentIdPromptForm(_config, description, workflow.IdPromptLabel, suggested);
            form.ShowModal();
            return form.DocumentId;
        });
    }

    /// <summary>
    /// Sends the document to its targets now, or parks it in the queue for the manual upload button.
    /// </summary>
    private async Task HandleUpload(DocumentWorkflowSettings workflow, ScanContext ctx, string filePath)
    {
        if (ActiveProfile == null || !DocumentUploadService.HasAnyTarget(ActiveProfile))
        {
            ScanConsole.Upload($"No upload target enabled for '{Path.GetFileName(filePath)}'; the file stays local.");
            return;
        }

        var document = new PendingDocument
        {
            Profile = ActiveProfile,
            Context = ctx,
            FilePath = filePath,
            DeleteFileAfterUpload = !workflow.KeepLocalCopy
        };

        if (workflow.UploadTrigger == UploadTrigger.Manual)
        {
            ScanConsole.Upload($"'{document.FileName}' queued for the manual upload button.");
            _uploadQueue?.Add(document);
            return;
        }

        // Success and failure are both reported by DocumentUploadService as a notification, the same way
        // a saved file is, so there's nothing left to do with the result here.
        await _documentUploadService.UploadAsync(document);
    }

    private ScanContext CreateScanContext(string template, int index, List<ProcessedImage> images,
        string? separatorValue = null)
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
            SeparatorBarcodeValue = separatorValue ??
                                    (images.FirstOrDefault()?.PostProcessingData.Barcode.IsPatchT == true
                                        ? images.First().PostProcessingData.Barcode.DetectedText
                                        : null),
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

}