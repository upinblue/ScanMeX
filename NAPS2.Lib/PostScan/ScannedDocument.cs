using NAPS2.Images;
using NAPS2.Scan;

namespace NAPS2.PostScan;

/// <summary>
/// What still has to happen to a document, and what already did.
/// </summary>
public enum DocumentStatus
{
    /// <summary>
    /// The profile requires an identifier and the document hasn't got one. Nothing is filed under a
    /// stand-in name; the operator has to supply the value first.
    /// </summary>
    NeedsIdentifier,

    /// <summary>
    /// Everything is in place; waiting for the operator to press upload.
    /// </summary>
    Pending,

    /// <summary>
    /// Being written or uploaded right now.
    /// </summary>
    Working,

    /// <summary>
    /// Everything the profile asked for succeeded.
    /// </summary>
    Done,

    /// <summary>
    /// Something failed. The document stays in the queue so the cause can be fixed and it can be retried.
    /// </summary>
    Failed
}

/// <summary>
/// One document produced by a scan: its pages, the barcodes found on them, the value it is filed under,
/// and how far it has got. This lives from the moment the scan is split until the document has reached
/// everywhere the profile sends it, which is what makes both correcting it and retrying it possible.
/// </summary>
/// <remarks>
/// The file is deliberately not part of a document's identity. It used to be: a document *was* the path
/// auto save had already written, so a barcode corrected afterwards could no longer change the name the
/// document was filed under, and a profile that only uploads still had to write into a folder first.
/// Here the file is produced by <see cref="DocumentWriter"/> when it is needed, from whatever the
/// document says at that moment.
/// </remarks>
public sealed class ScannedDocument : IDisposable
{
    private readonly List<DocumentBarcode> _barcodes = [];
    private List<ProcessedImage> _scannedPages = [];
    private List<UiImage>? _windowPages;
    private List<TransformState> _windowPageStates = [];
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();

    public required ScanProfile Profile { get; init; }

    /// <summary>
    /// The copies the scan produced, in order. Owned by the document, and what it is written from until
    /// the window's own page objects have taken over -- which never happens for the command line
    /// scanner, where there is no window at all.
    /// </summary>
    public required IReadOnlyList<ProcessedImage> ScannedPages
    {
        get => _scannedPages;
        init => _scannedPages = value.ToList();
    }

    /// <summary>
    /// The instances handed to the window for these pages. Not owned -- they belong to the window -- and
    /// only used to recognise which <see cref="UiImage"/> carries which of this document's pages.
    /// </summary>
    /// <remarks>
    /// The pipeline gives the window a clone of every page and keeps the original, so the two ends hold
    /// different objects for the same sheet of paper. This is the list that says which is which; matching
    /// them by value instead would tie a document to any page that happens to share storage with one of
    /// its own, which is what a duplicated page in the window is.
    /// </remarks>
    public IReadOnlyList<ProcessedImage> WindowPageCandidates { get; init; } = [];

    /// <summary>
    /// The pages as they are in the window right now, once the window has them. Null while the document
    /// still holds only the scan's own copies: the pages of a scan reach the window a moment after the
    /// document exists, and a document is only pointed at them once every one of its pages has arrived.
    /// </summary>
    public IReadOnlyList<UiImage>? WindowPages => _windowPages;

    public bool HasAdoptedWindowPages => _windowPages != null;

    /// <summary>
    /// Counts up whenever the document's pages change after the window has taken them over -- a page
    /// deleted, moved, or edited. <see cref="WrittenUnderPageRevision"/> compares against it, which is
    /// what makes a file that no longer shows what the document contains be written again.
    /// </summary>
    public int PageRevision { get; private set; }

    /// <summary>
    /// The zero-based position of this document within its scan, used by <c>$(n)</c>.
    /// </summary>
    public required int SequenceIndex { get; init; }

    /// <summary>
    /// When the scan that produced this document happened. Held rather than read from the clock at
    /// upload time so a date placeholder names the day the paper was scanned, not the day the operator
    /// got round to pressing upload.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public int PageCount => _windowPages?.Count ?? _scannedPages.Count;

    /// <summary>
    /// Points the document at the window's page objects, or brings it up to date with them. Returns
    /// whether anything actually changed.
    /// </summary>
    /// <remarks>
    /// Taking them over is not itself a change to the document: at that moment the window holds exactly
    /// the pages the scan produced, so the revision stays where it is and a file written in between is
    /// still current. Everything after that -- a page deleted, reordered or edited -- is a change, and
    /// the scan's own copies are released so that a page removed from the window cannot still reach the
    /// archive through them.
    /// </remarks>
    public bool SetWindowPages(IReadOnlyList<UiImage> pages)
    {
        var states = pages.Select(x => x.GetImageWeakReference().ProcessedImage.TransformState).ToList();
        if (_windowPages != null && _windowPages.SequenceEqual(pages) &&
            _windowPageStates.SequenceEqual(states))
        {
            return false;
        }
        var adopting = _windowPages == null;
        _windowPages = pages.ToList();
        _windowPageStates = states;
        if (adopting)
        {
            foreach (var page in _scannedPages)
            {
                page.Dispose();
            }
            _scannedPages = [];
        }
        else
        {
            PageRevision++;
        }
        return true;
    }

    /// <summary>
    /// The pages to write, as clones the caller has to dispose. Read at the moment of writing, so a page
    /// straightened or cropped in the window is straightened and cropped in the archived file too.
    /// </summary>
    public DisposableList<ProcessedImage> GetPagesForWriting() =>
        (_windowPages != null
            ? _windowPages.Select(x => x.GetClonedImage())
            : _scannedPages.Select(x => x.Clone()))
        .ToDisposableList();

    /// <summary>
    /// The pages as they stand, without taking a reference. For reading only -- anything that keeps one
    /// of these past the call has to clone it.
    /// </summary>
    public IReadOnlyList<ProcessedImage> CurrentPageStates() =>
        _windowPages != null
            ? _windowPages.Select(x => x.GetImageWeakReference().ProcessedImage).ToList()
            : _scannedPages;

    /// <summary>
    /// Every barcode on the document, the one the profile's regex accepts first. Editable: a value can be
    /// corrected, a phantom removed, or one added by hand.
    /// </summary>
    public IReadOnlyList<DocumentBarcode> Barcodes => _barcodes;

    /// <summary>
    /// The value the document is filed under -- it names the file and supplies the SAP object key.
    /// </summary>
    public string? Identifier { get; private set; }

    public DocumentBarcodeSource IdentifierSource { get; private set; } = DocumentBarcodeSource.Detected;

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    /// <summary>
    /// Why the document failed, or what it is currently doing. Shown in the document list.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Where the document was written, once it has been. Null while it exists only as pages.
    /// </summary>
    public string? SavedPath { get; set; }

    /// <summary>
    /// Whether <see cref="SavedPath"/> is a staging copy to be removed once every target succeeded. A
    /// profile that doesn't keep a local copy still needs a file on disk to upload.
    /// </summary>
    public bool SavedPathIsTemporary { get; set; }

    /// <summary>
    /// The identification the existing file was named after, so a later correction can be told from a
    /// file that is still current.
    /// </summary>
    /// <remarks>
    /// Without this the file was written once and then reused verbatim, while the SharePoint folder and
    /// the SAP object key were re-expanded from the identification at upload time -- so correcting a
    /// barcode after the document had been written (a profile that files locally and uploads on the
    /// button, or a retry after a failed upload) archived the document under the new key with the old
    /// name on it. That is exactly the drift between the file name and the archive key that nothing
    /// afterwards can detect.
    /// </remarks>
    public string? WrittenUnderIdentifier { get; set; }

    /// <summary>
    /// Whether the file on disk still carries the name the document would be given now.
    /// </summary>
    public bool FileMatchesIdentifier =>
        string.Equals(WrittenUnderIdentifier, Identifier, StringComparison.Ordinal);

    /// <summary>
    /// The page revision the existing file was written from. The counterpart to
    /// <see cref="WrittenUnderIdentifier"/> for the document's contents rather than its name.
    /// </summary>
    public int? WrittenUnderPageRevision { get; set; }

    /// <summary>
    /// Whether the file on disk still shows the pages the document consists of now. A page deleted or
    /// straightened after the document was written leaves a file that looks filed and is not what the
    /// operator is looking at, which is not something anyone notices afterwards.
    /// </summary>
    public bool FileMatchesPages => WrittenUnderPageRevision == PageRevision;

    /// <summary>
    /// The targets the document has already reached, so the list can say what happened rather than only
    /// that something did.
    /// </summary>
    public List<string> CompletedTargets { get; } = [];

    /// <summary>
    /// Whether the document was written to the folder the profile files documents in, as opposed to a
    /// staging copy that is deleted again.
    /// </summary>
    public bool IsSavedLocally => SavedPath != null && !SavedPathIsTemporary;

    /// <summary>
    /// Whether the document has reached somewhere it cannot be taken back from -- SharePoint, the SAP
    /// archive. This, and not <see cref="DocumentStatus.Done"/>, is what makes its pages untouchable:
    /// a file in the operator's own folder can be written again, while a document in an archive is a
    /// record that says these exact pages are in there.
    /// </summary>
    public bool IsFiledRemotely => CompletedTargets.Count > 0;

    /// <summary>
    /// The file to upload. Only valid once the document has been written.
    /// </summary>
    public string FilePath => SavedPath ??
                              throw new InvalidOperationException(
                                  "The document has not been written to a file yet.");

    /// <summary>
    /// The name the document carries, whether or not it exists as a file yet. Before it is written this
    /// is the unexpanded template, which is only used for display.
    /// </summary>
    public string FileName =>
        SavedPath != null ? Path.GetFileName(SavedPath) : Workflow.GetDocumentNameTemplate();

    /// <summary>
    /// The placeholder context as the document stands right now. Read fresh on every access on purpose:
    /// the operator may have corrected the identifier since the pages were scanned, and the SharePoint
    /// folder and the SAP object key are expanded from this at upload time.
    /// </summary>
    public ScanContext Context => BuildContext(SavedPath ?? Workflow.GetDocumentNameTemplate());

    public DocumentWorkflowSettings Workflow => DocumentWorkflowSettings.ForProfile(Profile);

    public void SetBarcodes(IEnumerable<DocumentBarcode> barcodes)
    {
        _barcodes.Clear();
        _barcodes.AddRange(barcodes);
    }

    public void AddBarcode(DocumentBarcode barcode) => _barcodes.Add(barcode);

    public bool RemoveBarcode(DocumentBarcode barcode) => _barcodes.Remove(barcode);

    /// <summary>
    /// Replaces a barcode's value, marking it as corrected by hand. When it was the value the document is
    /// filed under, the identifier follows it -- otherwise correcting the barcode the file is named after
    /// would leave the name on the old value.
    /// </summary>
    public void ReplaceBarcode(DocumentBarcode barcode, string newValue)
    {
        var index = _barcodes.IndexOf(barcode);
        if (index < 0)
        {
            return;
        }
        var wasIdentifier = IdentifierSource == DocumentBarcodeSource.Detected &&
                            string.Equals(Identifier, barcode.Value, StringComparison.Ordinal);
        var replacement = barcode with { Value = newValue, Source = DocumentBarcodeSource.Manual };
        _barcodes[index] = replacement;
        if (wasIdentifier)
        {
            Identifier = newValue;
        }
    }

    /// <summary>
    /// Sets the value the document is filed under. <paramref name="source"/> records whether it came off
    /// the paper or from the operator, which is what the document list shows and what decides whether a
    /// later re-detection may overwrite it.
    /// </summary>
    public void SetIdentifier(string? value, DocumentBarcodeSource source)
    {
        Identifier = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        IdentifierSource = source;
    }

    /// <summary>
    /// Whether the document can be filed. A profile that requires an identifier holds the document back
    /// rather than letting it reach the archive under a stand-in name, because a wrongly named document
    /// in an archive is not something anyone finds again.
    /// </summary>
    public bool HasEverythingItNeeds() =>
        !Workflow.RequireIdentifier || !string.IsNullOrWhiteSpace(Identifier);

    /// <summary>
    /// The placeholder context for this document as it stands right now. Everything a template can expand
    /// to is read from here, so a corrected identifier or barcode reaches the file name, the SharePoint
    /// folder and the SAP object key together.
    /// </summary>
    public ScanContext BuildContext(string template)
    {
        var ext = Path.GetExtension(template).TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = "pdf";
        }
        return new ScanContext
        {
            Timestamp = Timestamp,
            SequenceIndex = SequenceIndex,
            Profile = Profile,
            Images = CurrentPageStates(),
            Barcodes = _barcodes
                .Select(x => new DetectedBarcode(
                    x.Value,
                    x.Format ?? string.Empty,
                    Math.Max(x.PageIndex, 0),
                    Barcode.IsPatchTText(x.Value)))
                .ToList(),
            // The barcode that separated the document is the value it is archived under, and an operator
            // correction replaces it in the same slot -- so $(barcode) and $(id) always agree with what
            // the document list shows.
            SeparatorBarcodeValue = Identifier,
            DocumentId = Identifier,
            OutputExtension = ext,
            FileFormat = ext
        };
    }

    /// <summary>
    /// How the document reads in a console line.
    /// </summary>
    public string Describe() =>
        $"document {SequenceIndex + 1} ({PageCount} page(s), id '{Identifier ?? "(none)"}')";

    /// <summary>
    /// Removes the staging copy. Only ever the temporary one: a document filed into the profile's folder
    /// is the operator's file, and for a profile that keeps no local copy the staging file is the only
    /// copy there is, so it must outlive a failed upload.
    /// </summary>
    public void DiscardStagingFile()
    {
        if (SavedPath == null || !SavedPathIsTemporary)
        {
            return;
        }
        try
        {
            if (File.Exists(SavedPath))
            {
                File.Delete(SavedPath);
            }
            var folder = Path.GetDirectoryName(SavedPath);
            if (folder != null && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex)
        {
            Log.ErrorException($"Could not delete staged document {SavedPath}", ex);
        }
        SavedPath = null;
        SavedPathIsTemporary = false;
        WrittenUnderIdentifier = null;
        WrittenUnderPageRevision = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DiscardStagingFile();
        // Only the scan's own copies. Once the window has taken the pages over they are the window's,
        // and disposing them here would take pages out from under the list they are still shown in.
        foreach (var page in _scannedPages)
        {
            page.Dispose();
        }
        _scannedPages = [];
    }
}
