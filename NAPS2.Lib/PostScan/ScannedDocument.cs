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
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();

    public required ScanProfile Profile { get; init; }

    /// <summary>
    /// The pages, in order.
    /// </summary>
    public required IReadOnlyList<ProcessedImage> Pages { get; init; }

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

    public int PageCount => Pages.Count;

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
            Images = Pages,
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
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        DiscardStagingFile();
        foreach (var page in Pages)
        {
            page.Dispose();
        }
    }
}
