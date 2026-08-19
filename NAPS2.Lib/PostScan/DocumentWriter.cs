using NAPS2.EtoForms;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Images;
using NAPS2.Pdf;
using NAPS2.Scan;

namespace NAPS2.PostScan;

/// <summary>
/// The outcome of writing a document to a file.
/// </summary>
/// <param name="Success">Whether a file was produced.</param>
/// <param name="Path">Where it was written, or null if it wasn't.</param>
/// <param name="Error">Why it wasn't, in a form fit to show the operator. Null on success.</param>
/// <param name="Cancelled">Whether the operator declined rather than something failing.</param>
public sealed record DocumentWriteResult(bool Success, string? Path, string? Error, bool Cancelled = false)
{
    public static DocumentWriteResult Failed(string error) => new(false, null, error);

    public static DocumentWriteResult WasCancelled() => new(false, null, null, true);

    public static DocumentWriteResult Wrote(string path) => new(true, path, null);
}

/// <summary>
/// Turns a document's pages into a file. Separated from the pipeline because *when* this happens is now
/// a profile decision: a document that is filed locally is written as soon as it has been scanned, while
/// one that only goes to an archive is written when the operator presses upload -- by which time they
/// may have corrected the barcode the file is named after.
/// </summary>
public class DocumentWriter
{
    private readonly PdfExporter _pdfExporter;
    private readonly IOverwritePrompt _overwritePrompt;
    private readonly OperationProgress _operationProgress;
    private readonly Naps2Config _config;
    private readonly ImageContext _imageContext;
    private readonly DialogHelper _dialogHelper;

    public DocumentWriter(PdfExporter pdfExporter, IOverwritePrompt overwritePrompt,
        OperationProgress operationProgress, Naps2Config config, ImageContext imageContext,
        DialogHelper dialogHelper)
    {
        _pdfExporter = pdfExporter;
        _overwritePrompt = overwritePrompt;
        _operationProgress = operationProgress;
        _config = config;
        _imageContext = imageContext;
        _dialogHelper = dialogHelper;
    }

    /// <summary>
    /// Writes the document to the folder its profile files documents in.
    /// </summary>
    public async Task<DocumentWriteResult> WriteToProfileFolderAsync(ScannedDocument document)
    {
        var workflow = document.Workflow;
        var template = Path.Combine(workflow.LocalFolder ?? "", workflow.GetDocumentNameTemplate());
        var resolved = Resolve(document, template);
        if (resolved.Error != null)
        {
            return DocumentWriteResult.Failed(resolved.Error);
        }
        var path = resolved.Path!;

        if (workflow.PromptForFilePath)
        {
            string? chosen = null!;
            if (!Invoker.Current.InvokeGet(() => _dialogHelper.PromptToSavePdfOrImage(path, out chosen)))
            {
                ScanConsole.Document($"{Describe(document)}: the save dialog was cancelled; nothing written.");
                return DocumentWriteResult.WasCancelled();
            }
            var reResolved = Resolve(document, chosen!);
            if (reResolved.Error != null)
            {
                return DocumentWriteResult.Failed(reResolved.Error);
            }
            path = reResolved.Path!;
        }

        return await WriteAsync(document, path, notifyOnCollision: true);
    }

    /// <summary>
    /// Writes the document to a temporary file so it can be uploaded. Used by profiles that keep no local
    /// copy: the archive still needs a file, but it must not land in a folder the operator has to clean
    /// out afterwards.
    /// </summary>
    public async Task<DocumentWriteResult> WriteToStagingAsync(ScannedDocument document)
    {
        var resolved = Resolve(document, document.Workflow.GetDocumentNameTemplate());
        if (resolved.Error != null)
        {
            return DocumentWriteResult.Failed(resolved.Error);
        }
        // The name still comes from the template because it is the name the archive stores, but it is
        // sanitized here rather than trusted: it goes straight into a path.
        var fileName = FileNamePlaceholders.SanitizeForFileName(Path.GetFileName(resolved.Path!));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return DocumentWriteResult.Failed(UiStrings.DocumentNameEmpty);
        }
        var folder = Path.Combine(Paths.Temp, "upload", document.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        return await WriteAsync(document, Path.Combine(folder, fileName), notifyOnCollision: false);
    }

    /// <summary>
    /// Expands the template against the document as it stands now. An unresolved placeholder is an error
    /// rather than something to write literally: a document called <c>scan_$(unknown).pdf</c> is worse
    /// than one that wasn't written, because it looks filed.
    /// </summary>
    private static (string? Path, string? Error) Resolve(ScannedDocument document, string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return (null, UiStrings.DocumentNameEmpty);
        }
        var ctx = document.BuildContext(template);
        var path = new FileNamePlaceholders().SubstitutePlaceholders(template, ctx, autoIncrement: true);
        if (path.Contains("$(", StringComparison.Ordinal))
        {
            ScanConsole.Document(
                $"{Describe(document)}: unresolved placeholder in '{template}' (resolved to '{path}'); " +
                "nothing written.");
            return (null, string.Format(UiStrings.UnresolvedPlaceholder, template));
        }
        return (path, null);
    }

    private async Task<DocumentWriteResult> WriteAsync(ScannedDocument document, string path,
        bool notifyOnCollision)
    {
        var placeholders = Placeholders.All.WithDate(document.Timestamp);
        var extension = Path.GetExtension(path);
        // Read at this moment rather than when the document was split: for a profile that uploads on the
        // button, minutes of correcting can sit in between, and a page straightened in the window has to
        // be straightened in the archived file too. Disposed once the operation is finished with them.
        using var images = document.GetPagesForWriting();

        if (extension.Equals(".pdf", StringComparison.InvariantCultureIgnoreCase))
        {
            if (notifyOnCollision && File.Exists(path))
            {
                path = placeholders.Substitute(path, true, 0, 1);
            }
            var op = new SavePdfOperation(_pdfExporter, _overwritePrompt);
            if (op.Start(path, placeholders, images, _config.Get(c => c.PdfSettings),
                    _config.DefaultOcrParams()))
            {
                _operationProgress.ShowProgress(op);
            }
            if (!await op.Success)
            {
                var reason = DescribeFailure(op);
                ScanConsole.Document($"{Describe(document)}: writing '{path}' failed: {reason}");
                return DocumentWriteResult.Failed(reason);
            }
            ScanConsole.Document($"{Describe(document)}: wrote '{path}'.");
            return DocumentWriteResult.Wrote(path);
        }

        var imageOp = new SaveImagesOperation(_overwritePrompt, _imageContext);
        if (imageOp.Start(path, placeholders, images, _config.Get(c => c.ImageSettings)))
        {
            _operationProgress.ShowProgress(imageOp);
        }
        if (!await imageOp.Success)
        {
            var reason = DescribeFailure(imageOp);
            ScanConsole.Document($"{Describe(document)}: writing image file(s) to '{path}' failed: {reason}");
            return DocumentWriteResult.Failed(reason);
        }
        ScanConsole.Document(
            $"{Describe(document)}: wrote image file(s), first is '{imageOp.FirstFileSaved}'.");
        return DocumentWriteResult.Wrote(imageOp.FirstFileSaved ?? path);
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

    private static string Describe(ScannedDocument document) => document.Describe();
}
