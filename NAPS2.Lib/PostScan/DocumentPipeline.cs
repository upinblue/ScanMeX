using NAPS2.EtoForms.Notifications;
using NAPS2.Images;
using NAPS2.Scan;

namespace NAPS2.PostScan;

/// <summary>
/// What happens to a scan once the pages have arrived: split it into documents, note the barcodes each
/// one carries, and then -- now or when the operator presses upload -- write it and send it where the
/// profile says.
/// </summary>
/// <remarks>
/// This is the only post-scan path. A second, sink-based one once existed alongside it, was never wired
/// into the container, and carried drifting copies of the barcode and path logic; don't reintroduce one.
/// If the flow needs to change, change it here.
/// </remarks>
public class DocumentPipeline
{
    private readonly ErrorOutput _errorOutput;
    private readonly ISaveNotify _notify;
    private readonly UiImageList _imageList;
    private readonly DocumentQueue _queue;
    private readonly DocumentWriter _writer;
    private readonly DocumentUploadService _uploadService;

    public DocumentPipeline(ErrorOutput errorOutput, ISaveNotify notify, UiImageList imageList,
        DocumentQueue queue, DocumentWriter writer, DocumentUploadService uploadService)
    {
        _errorOutput = errorOutput;
        _notify = notify;
        _imageList = imageList;
        _queue = queue;
        _writer = writer;
        _uploadService = uploadService;
    }

    /// <summary>
    /// Consumes the scanned pages, produces documents from them, and passes the pages on to the window.
    /// </summary>
    /// <remarks>
    /// The pages always reach the window, even for a profile that files and archives everything without
    /// asking. They are what the operator looks at while correcting a barcode, and they are removed again
    /// once the document is finished if the profile asks for that -- which is a different thing from
    /// never showing them, and the one that leaves the correction possible.
    /// </remarks>
    public IAsyncEnumerable<ProcessedImage> Process(ScanProfile profile, IAsyncEnumerable<ProcessedImage> images)
    {
        return AsyncProducers.RunProducer<ProcessedImage>(async produceImage =>
        {
            var pages = new List<ProcessedImage>();
            try
            {
                await foreach (var image in images)
                {
                    pages.Add(image);
                    // The document keeps the original; the window gets a reference of its own. Both have
                    // to survive independently -- the document may still have to render a PDF long after
                    // the operator has deleted the page from the window.
                    produceImage(image.Clone());
                }
            }
            finally
            {
                await BuildAndRunDocuments(profile, pages);
            }
        });
    }

    private async Task BuildAndRunDocuments(ScanProfile profile, List<ProcessedImage> pages)
    {
        if (pages.Count == 0)
        {
            ScanConsole.Document("The scan produced no pages, so there is nothing to file.");
            return;
        }
        List<ScannedDocument> documents;
        try
        {
            documents = Split(profile, DocumentWorkflowSettings.ForProfile(profile), pages);
        }
        catch (Exception ex)
        {
            Log.ErrorException(MiscResources.AutoSaveError, ex);
            _errorOutput.DisplayError(MiscResources.AutoSaveError, ex);
            foreach (var page in pages)
            {
                page.Dispose();
            }
            return;
        }
        foreach (var document in documents)
        {
            _queue.Add(document);
        }
        foreach (var document in documents)
        {
            await Advance(document);
        }
    }

    /// <summary>
    /// Splits a scan into documents and attaches the barcodes and the identifying value to each.
    /// </summary>
    private static List<ScannedDocument> Split(
        ScanProfile profile, DocumentWorkflowSettings workflow, List<ProcessedImage> pages)
    {
        var segments = DocumentSeparator.Separate(pages, workflow)
            .Select(x => (Pages: x.Images.ToList(), Separator: x.SeparatorBarcodeValue))
            .ToList();

        ScanConsole.Document(
            $"{workflow.SeparationMode} separation: {pages.Count} page(s) -> {segments.Count} document(s) " +
            $"[{string.Join(", ", segments.Select((x, i) => $"#{i + 1}={x.Pages.Count}p/'{x.Separator ?? "no barcode"}'"))}]");

        var timestamp = DateTime.Now;
        var documents = new List<ScannedDocument>();
        for (var i = 0; i < segments.Count; i++)
        {
            var (segmentPages, separatorValue) = segments[i];
            var document = new ScannedDocument
            {
                Profile = profile,
                Pages = segmentPages,
                SequenceIndex = i,
                Timestamp = timestamp
            };
            AttachBarcodes(document, workflow, separatorValue);
            documents.Add(document);
        }
        return documents;
    }

    /// <summary>
    /// Records what was decoded on the document's pages and what it is filed under.
    /// </summary>
    /// <remarks>
    /// The identifying value is the barcode that separated the document, because that is the one the
    /// operator's regex accepted; deriving it a second way lets the file name and the archive key drift
    /// apart. Where nothing separated the document -- a profile that doesn't separate at all -- the same
    /// regex picks it out of the page's barcodes, so the two paths still agree.
    /// </remarks>
    private static void AttachBarcodes(
        ScannedDocument document, DocumentWorkflowSettings workflow, string? separatorValue)
    {
        var extracted = new BarcodeExtractor
        {
            SelectionPattern = document.Profile.GetBarcodeSelectionPattern()
        }.Extract(document.Pages);

        document.SetBarcodes(extracted.Select(x =>
            new DocumentBarcode(x.Value, string.IsNullOrWhiteSpace(x.BarcodeType) ? null : x.BarcodeType,
                x.PageIndex)));

        // In manual mode a detected barcode is still offered as the starting value, so the usual case is
        // confirming rather than typing. It is only a suggestion: RequireIdentifier decides whether the
        // document may go anywhere before the operator has looked at it.
        document.SetIdentifier(
            separatorValue ?? FirstValueThePatternAllows(document, extracted),
            DocumentBarcodeSource.Detected);

        document.Status = document.HasEverythingItNeeds()
            ? DocumentStatus.Pending
            : DocumentStatus.NeedsIdentifier;

        ScanConsole.Document(
            $"{document.Describe()}: " +
            (document.Barcodes.Count == 0
                ? "no barcodes on its pages."
                : $"barcodes {string.Join(", ", document.Barcodes.Select(x => x.Describe()))}."));
        if (document.Status == DocumentStatus.NeedsIdentifier)
        {
            ScanConsole.Document(
                $"{document.Describe()}: the profile requires an identification and none was found. " +
                "The document waits in the document list until one is entered.");
        }
        else if (workflow.IdMode == DocumentIdMode.ManualInput)
        {
            ScanConsole.Document(
                $"{document.Describe()}: identification is entered by hand for this profile; " +
                $"'{document.Identifier ?? ""}' is offered as the starting value.");
        }
    }

    /// <summary>
    /// The document's value when nothing separated it. A page can carry several barcodes and the first in
    /// reading order is not necessarily the one that identifies the document -- when the profile has a
    /// regex, that regex is the operator's statement of which one does.
    /// </summary>
    private static string? FirstValueThePatternAllows(
        ScannedDocument document, IReadOnlyList<DetectedBarcode> extracted)
    {
        var pattern = DocumentSeparator.CompilePattern(document.Profile.GetBarcodeSelectionPattern());
        if (pattern == null)
        {
            return extracted.FirstOrDefault()?.Value;
        }
        return extracted
            .Select(x => DocumentSeparator.ApplyPattern(x.Value, pattern))
            .FirstOrDefault(x => x != null);
    }

    /// <summary>
    /// Takes a document as far as it can go right now: writes it if the profile files documents locally,
    /// uploads it if the profile uploads automatically, and otherwise leaves it for the upload button.
    /// This is also what the button calls, so the manual and the automatic route can't drift apart.
    /// </summary>
    public async Task Advance(ScannedDocument document, bool triggeredByOperator = false)
    {
        var workflow = document.Workflow;
        var hasTargets = DocumentUploadService.HasAnyTarget(document.Profile);

        if (!document.HasEverythingItNeeds())
        {
            document.Status = DocumentStatus.NeedsIdentifier;
            ScanConsole.Document(
                $"{document.Describe()}: waiting for an identification; nothing written or uploaded.");
            _queue.NotifyChanged();
            return;
        }

        if (!workflow.SaveLocally && !hasTargets)
        {
            // Nothing to do with it at all. Saying so is the point: a profile in this state scans and
            // then quietly keeps nothing, which is the failure operators cannot see.
            ScanConsole.Document(
                $"{document.Describe()}: the profile neither files documents locally nor uploads them, " +
                "so the pages stay in the window only.");
            Finish(document);
            return;
        }

        var uploadNow = hasTargets &&
                        (workflow.UploadTrigger == UploadTrigger.Automatic || triggeredByOperator);
        if (!workflow.SaveLocally && !uploadNow)
        {
            document.Status = DocumentStatus.Pending;
            ScanConsole.Upload(
                $"{document.Describe()}: waiting for the upload button; nothing has been written yet.");
            _queue.NotifyChanged();
            return;
        }

        document.Status = DocumentStatus.Working;
        _queue.NotifyChanged();
        try
        {
            if (!await EnsureFile(document, workflow))
            {
                return;
            }
            if (!uploadNow)
            {
                document.Status = DocumentStatus.Pending;
                ScanConsole.Upload($"{document.Describe()}: filed locally, waiting for the upload button.");
                return;
            }
            await Upload(document);
        }
        finally
        {
            _queue.NotifyChanged();
        }
    }

    /// <summary>
    /// Makes sure there is a file to upload, writing one if there isn't. Returns false when the document
    /// could not be written, in which case it is left failed rather than uploaded.
    /// </summary>
    private async Task<bool> EnsureFile(ScannedDocument document, DocumentWorkflowSettings workflow)
    {
        if (document.SavedPath != null && File.Exists(document.SavedPath))
        {
            return true;
        }

        var result = workflow.SaveLocally
            ? await _writer.WriteToProfileFolderAsync(document)
            : await _writer.WriteToStagingAsync(document);

        if (!result.Success)
        {
            document.Status = DocumentStatus.Failed;
            document.Message = result.Cancelled ? UiStrings.DocumentSaveCancelled : result.Error;
            if (result.Error != null)
            {
                _errorOutput.DisplayError(result.Error);
            }
            return false;
        }

        document.SavedPath = result.Path;
        document.SavedPathIsTemporary = !workflow.SaveLocally;
        if (workflow.SaveLocally)
        {
            // Keeps closing the window from warning about pages that are on disk. Only for a file the
            // operator keeps: a staging copy that is about to be deleted is not a saved document.
            _imageList.MarkSaved(_imageList.CurrentState, document.Pages);
            if (!DocumentUploadService.HasAnyTarget(document.Profile))
            {
                // With an upload still to come, the upload notification reports the outcome instead --
                // otherwise every document produces two notifications saying different things.
                _notify.PdfSaved(result.Path!);
            }
        }
        return true;
    }

    private async Task Upload(ScannedDocument document)
    {
        if (!IsPdf(document.FilePath))
        {
            // Image output writes one file per page, which doesn't map to one document. Say so rather
            // than silently skipping the upload.
            ScanConsole.Upload(
                $"{document.Describe()}: upload skipped, the profile writes image files and uploading " +
                "only supports PDF output.");
            document.Status = DocumentStatus.Failed;
            document.Message = UiStrings.UploadRequiresPdf;
            _notify.DocumentUploadFailed(document.FileName, UiStrings.UploadRequiresPdf);
            return;
        }

        if (await _uploadService.UploadAsync(document))
        {
            // The staging copy has done its job. A file the operator keeps is left alone.
            document.DiscardStagingFile();
            Finish(document);
            return;
        }
        // The staging file deliberately survives a failure: for a profile that keeps no local copy it is
        // the only copy of the scan, so removing it would destroy a document that never reached the
        // archive. It is also what makes the retry cheap.
        document.Status = DocumentStatus.Failed;
        ScanConsole.Upload($"{document.Describe()}: stays in the document list so it can be retried.");
    }

    /// <summary>
    /// Marks a finished document as done and, for profiles that ask for it, takes its pages out of the
    /// window. Only its own pages: a batch can leave several documents in different states, and clearing
    /// the window would throw away one that still has to be retried.
    /// </summary>
    public void Finish(ScannedDocument document)
    {
        document.Status = DocumentStatus.Done;
        document.Message = null;
        _imageList.MarkSaved(_imageList.CurrentState, document.Pages);

        if (document.Workflow.CleanupAfterCompletion)
        {
            var pages = document.Pages.ToHashSet();
            var toRemove = _imageList.Images
                .Where(x => pages.Contains(x.GetImageWeakReference().ProcessedImage))
                .ToList();
            if (toRemove.Count > 0)
            {
                ScanConsole.Document(
                    $"{document.Describe()}: finished, removing its {toRemove.Count} page(s) from the window.");
                Invoker.Current.Invoke(() =>
                    _imageList.Mutate(new ListMutation<UiImage>.DeleteSelected(),
                        ListSelection.From(toRemove)));
            }
            _queue.Remove(document);
        }
        _queue.NotifyChanged();
    }

    private static bool IsPdf(string path) =>
        Path.GetExtension(path).Equals(".pdf", StringComparison.InvariantCultureIgnoreCase);
}
