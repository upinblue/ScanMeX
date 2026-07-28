using NAPS2.ImportExport;

namespace NAPS2.Scan;

/// <summary>
/// How a scan is split into separate documents.
/// </summary>
public enum DocumentSeparationMode
{
    /// <summary>
    /// No barcode-driven separation; the auto save separator decides.
    /// </summary>
    None,

    /// <summary>
    /// Patch-T separator sheets start a new document.
    /// </summary>
    PatchT,

    /// <summary>
    /// A page carrying a barcode of the selected symbology starts a new document. If a separation
    /// pattern is configured, only barcodes matching it count as a document boundary.
    /// </summary>
    Barcode
}

/// <summary>
/// Where the value identifying a document comes from.
/// </summary>
public enum DocumentIdMode
{
    /// <summary>
    /// No identification value; placeholders fall back to the detected barcode.
    /// </summary>
    None,

    /// <summary>
    /// The document's barcode provides the value.
    /// </summary>
    Barcode,

    /// <summary>
    /// The operator types the value once per document after scanning finishes.
    /// </summary>
    ManualInput
}

/// <summary>
/// When documents are uploaded to the configured target systems.
/// </summary>
public enum UploadTrigger
{
    /// <summary>
    /// Upload immediately once a document has been produced.
    /// </summary>
    Automatic,

    /// <summary>
    /// Hold documents in the scan window until the operator presses the upload button.
    /// </summary>
    Manual
}

/// <summary>
/// Per-profile configuration for splitting a scan into documents, identifying them, and uploading them.
/// Null on a profile means "derive from the legacy auto save settings", which keeps existing profiles working.
/// </summary>
public record DocumentWorkflowSettings
{
    public DocumentSeparationMode SeparationMode { get; init; } = DocumentSeparationMode.None;

    /// <summary>
    /// The barcode symbologies this profile cares about, in priority order. The first decoded barcode
    /// matching one of them becomes the page's primary barcode.
    /// </summary>
    public List<BarcodeSymbology> BarcodeSymbologies { get; init; } = [];

    /// <summary>
    /// Optional regex. A page's barcode only starts a new document if it matches. If the pattern has a
    /// capturing group, group 1 becomes the document's barcode value, otherwise the whole match does.
    /// This lets one barcode both mark the boundary and supply the file name.
    /// </summary>
    public string? SeparationPattern { get; init; }

    /// <summary>
    /// Whether the page carrying the separator barcode stays in the document. Production papers usually
    /// need it, since the separator sheet is part of the document; patch-T sheets usually don't.
    /// </summary>
    public bool KeepSeparatorPage { get; init; } = true;

    public DocumentIdMode IdMode { get; init; } = DocumentIdMode.None;

    /// <summary>
    /// Optional label shown above the input box in <see cref="DocumentIdMode.ManualInput"/> mode.
    /// </summary>
    public string? IdPromptLabel { get; init; }

    public UploadTrigger UploadTrigger { get; init; } = UploadTrigger.Automatic;

    /// <summary>
    /// Whether the file written to the auto save path is kept after a successful upload. When false the
    /// document only lives in the temp folder and is removed once it has been uploaded.
    /// </summary>
    public bool KeepLocalCopy { get; init; } = true;

    /// <summary>
    /// Whether documents are removed from the scan window and the temp folder once everything succeeded.
    /// </summary>
    public bool CleanupAfterCompletion { get; init; } = true;

    /// <summary>
    /// Returns the profile's workflow settings, falling back to settings derived from the legacy
    /// auto save configuration for profiles saved before this existed.
    /// </summary>
    public static DocumentWorkflowSettings ForProfile(ScanProfile? profile)
    {
        if (profile?.DocumentWorkflow != null)
        {
            return profile.DocumentWorkflow;
        }
        var autoSave = profile?.AutoSaveSettings;
        return new DocumentWorkflowSettings
        {
            SeparationMode = autoSave?.Separator switch
            {
                SaveSeparator.Code39Barcode => DocumentSeparationMode.Barcode,
                SaveSeparator.PatchT => DocumentSeparationMode.PatchT,
                _ => DocumentSeparationMode.None
            },
            BarcodeSymbologies = autoSave?.Separator == SaveSeparator.Code39Barcode
                ? [BarcodeSymbology.Code39]
                : [],
            SeparationPattern = autoSave?.Code39SeparationPattern,
            // Legacy Code 39 separation kept the barcode page, legacy patch-T dropped it.
            KeepSeparatorPage = autoSave?.Separator != SaveSeparator.PatchT,
            IdMode = DocumentIdMode.None,
            UploadTrigger = UploadTrigger.Automatic,
            KeepLocalCopy = true,
            CleanupAfterCompletion = autoSave?.ClearImagesAfterSaving ?? false
        };
    }

    /// <summary>
    /// The symbologies to hand to the detector. Patch-T separation implies patch-T detection even if the
    /// operator didn't tick a symbology.
    /// </summary>
    public IReadOnlyList<BarcodeSymbology> GetEffectiveSymbologies()
    {
        if (BarcodeSymbologies.Count > 0)
        {
            return BarcodeSymbologies;
        }
        return SeparationMode == DocumentSeparationMode.PatchT ? [BarcodeSymbology.PatchT] : [];
    }

    /// <summary>
    /// Whether this profile needs barcodes decoded at all.
    /// </summary>
    public bool RequiresBarcodeDetection() =>
        SeparationMode != DocumentSeparationMode.None ||
        IdMode == DocumentIdMode.Barcode ||
        BarcodeSymbologies.Count > 0;
}
