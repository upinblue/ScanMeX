using NAPS2.ImportExport;

namespace NAPS2.Scan;

/// <summary>
/// How a scan is split into separate documents.
/// </summary>
public enum DocumentSeparationMode
{
    /// <summary>
    /// The whole scan is one document.
    /// </summary>
    /// <remarks>
    /// Named None rather than OnePerScan because that is the name already written into every stored
    /// profile, and an enum member that no longer parses is a profile that silently reverts to the
    /// default. What it means has been pinned down -- it used to mean "the legacy auto save separator
    /// decides", which left two settings able to contradict each other.
    /// </remarks>
    None,

    /// <summary>
    /// Patch-T separator sheets start a new document.
    /// </summary>
    PatchT,

    /// <summary>
    /// A page carrying a barcode of the selected symbology starts a new document. If a separation
    /// pattern is configured, only barcodes matching it count as a document boundary.
    /// </summary>
    Barcode,

    /// <summary>
    /// Every page is its own document.
    /// </summary>
    OnePerPage
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
    /// The operator supplies the value in the document list after scanning finishes. Documents start
    /// out marked as needing input rather than interrupting the scan with a dialog, so a stack of paper
    /// can be fed through in one go and identified afterwards.
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
    /// <summary>
    /// The version this settings object was written by. Profiles saved before saving and uploading were
    /// separable have no value here, and their <see cref="SaveLocally"/> and <see cref="LocalPath"/>
    /// would deserialize as "off" and "nowhere" -- which would quietly stop saving for a profile that had
    /// been saving all along. <see cref="ForProfile"/> fills those in from the legacy auto save settings
    /// instead, and stamps the current version so it only happens once.
    /// </summary>
    public const int CURRENT_VERSION = 1;

    public int Version { get; init; }

    public DocumentSeparationMode SeparationMode { get; init; } = DocumentSeparationMode.None;

    /// <summary>
    /// The barcode symbologies this profile cares about, in priority order. The first decoded barcode
    /// matching one of them becomes the page's primary barcode.
    /// </summary>
    public List<BarcodeSymbology> BarcodeSymbologies { get; init; } = [];

    /// <summary>
    /// How much damage a printed barcode may carry and still be accepted. Strict is both the default for
    /// a new profile and what a profile saved before this setting existed reads as, because the element
    /// is simply absent from those files and the enum's zero value is Strict -- so lowering it is always
    /// something an operator did on purpose, and nothing starts accepting damaged barcodes on its own.
    /// </summary>
    public BarcodeStrictness BarcodeStrictness { get; init; } = BarcodeStrictness.Strict;

    /// <summary>
    /// Whether barcode detection only looks at <see cref="BarcodeArea"/> instead of at the whole page.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="BarcodeArea"/> rather than folded into it as "null means everywhere",
    /// so that turning the restriction off and on again doesn't lose the area that was drawn. Absent from
    /// every profile written before this existed, and false is exactly what those should get: nothing
    /// starts ignoring part of a page because of an update.
    /// </remarks>
    public bool RestrictBarcodeArea { get; init; }

    /// <summary>
    /// The part of the page barcodes are looked for in, in fractions of the page. Null means the whole
    /// page, and so does an area covering it.
    /// </summary>
    public BarcodeSearchArea? BarcodeArea { get; init; }

    /// <summary>
    /// The profile's one barcode regex. A page's barcode only starts a new document if it matches, and
    /// the same pattern decides which of a page's barcodes is the one the operator means. If the pattern
    /// has a capturing group, group 1 becomes the value, otherwise the whole match does -- so one barcode
    /// can both mark the boundary and contribute only part of itself.
    /// </summary>
    /// <remarks>
    /// Named for separation because that is the name already in every stored profile, but it is no longer
    /// only about separating. There used to be a second regex on the SAP settings deciding the object
    /// key, which meant a profile could select one barcode for its file name and a different one for the
    /// archive -- the two drifting apart is exactly the failure that is invisible afterwards. The SAP one
    /// is now folded in here on load and the archive takes the document's identification.
    /// </remarks>
    public string? SeparationPattern { get; init; }

    /// <summary>
    /// Whether the page carrying the separator barcode stays in the document. Production papers usually
    /// need it, since the separator sheet is part of the document; patch-T sheets usually don't.
    /// </summary>
    public bool KeepSeparatorPage { get; init; } = true;

    /// <summary>
    /// Whether a page only starts a new document when its barcode differs from the one the current
    /// document was started with. The paperwork for one process order repeats the order barcode on every
    /// cover sheet it contains -- accompanying document, route card, storage slip -- so treating each of
    /// them as a boundary would split one order into several files that all carry the same name. With
    /// this on, a stack of several orders still splits, but only where the order number actually changes.
    /// </summary>
    public bool NewDocumentOnlyOnValueChange { get; init; } = true;

    public DocumentIdMode IdMode { get; init; } = DocumentIdMode.None;

    /// <summary>
    /// Optional label shown next to the identifier box in the document list in
    /// <see cref="DocumentIdMode.ManualInput"/> mode, so it can say "Auftragsnummer" rather than
    /// "Kennzeichnung".
    /// </summary>
    public string? IdPromptLabel { get; init; }

    /// <summary>
    /// Whether a document without an identifier is held back instead of being filed. A document that
    /// reaches the archive under a stand-in name is not something anyone finds again, so a profile whose
    /// paperwork always carries a number should say so here.
    /// </summary>
    public bool RequireIdentifier { get; init; }

    /// <summary>
    /// Whether the document is written to <see cref="LocalPath"/>.
    /// </summary>
    /// <remarks>
    /// Independent of the upload targets and of <see cref="UploadTrigger"/> on purpose. These three used
    /// to be one setting: uploading ran on the file auto save had written, so a profile that only wanted
    /// to archive to SAP still had to nominate a folder and then have the file deleted again. With them
    /// separate, "don't keep anything locally, upload on the button" is a combination that can simply be
    /// selected.
    /// </remarks>
    public bool SaveLocally { get; init; }

    /// <summary>
    /// The folder documents are written to when <see cref="SaveLocally"/> is on. Supports placeholders.
    /// </summary>
    public string? LocalFolder { get; init; }

    /// <summary>
    /// The document's file name, with extension. Supports placeholders, <c>$(barcode)</c> and
    /// <c>$(id)</c> included.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="LocalFolder"/> because the name is not only a local matter: it is also
    /// the name the document arrives under in SharePoint and in the SAP archive. A profile that keeps
    /// nothing locally still has to be able to say what its documents are called, and when the two were
    /// one path template it could only do that by nominating a folder it didn't want.
    /// </remarks>
    public string? DocumentNameTemplate { get; init; }

    /// <summary>
    /// Whether the operator is asked where to put the file each time.
    /// </summary>
    public bool PromptForFilePath { get; init; }

    /// <summary>
    /// The file name template, falling back to the identifier so a profile that never nominated one
    /// still produces a document named after the value it is filed under rather than after nothing.
    /// </summary>
    public string GetDocumentNameTemplate() =>
        string.IsNullOrWhiteSpace(DocumentNameTemplate) ? "$(id).pdf" : DocumentNameTemplate!;

    public UploadTrigger UploadTrigger { get; init; } = UploadTrigger.Automatic;

    /// <summary>
    /// Whether documents are removed from the scan window once everything succeeded.
    /// </summary>
    public bool CleanupAfterCompletion { get; init; } = true;

    /// <summary>
    /// Whether this profile does anything at all once the pages have been scanned.
    /// </summary>
    public bool HasPostScanWork(ScanProfile? profile) =>
        SaveLocally || profile?.UploadsToSharePoint() == true || profile?.UploadsToSap() == true;

    /// <summary>
    /// Returns the profile's workflow settings, falling back to settings derived from the legacy
    /// auto save configuration for profiles saved before this existed.
    /// </summary>
    public static DocumentWorkflowSettings ForProfile(ScanProfile? profile)
    {
        var autoSave = profile?.AutoSaveSettings;
        if (profile?.DocumentWorkflow is { } stored)
        {
            if (stored.Version >= CURRENT_VERSION)
            {
                return stored;
            }
            // Written before saving became separable from uploading. SaveLocally and LocalPath aren't in
            // the file, so taking them at face value would turn off saving for a profile that has been
            // saving all along. They come from the auto save settings, which is where they lived.
            return stored with
            {
                Version = CURRENT_VERSION,
                // "None" used to defer to the auto save separator, so a stored None on a profile whose
                // separator was one-file-per-page meant per-page, not one document per scan.
                SeparationMode = stored.SeparationMode == DocumentSeparationMode.None
                    ? SeparationModeFor(autoSave?.Separator)
                    : stored.SeparationMode,
                SaveLocally = profile.EnableAutoSave,
                LocalFolder = FolderOf(autoSave?.FilePath),
                DocumentNameTemplate = NameOf(autoSave?.FilePath),
                PromptForFilePath = autoSave?.PromptForFilePath ?? false,
                SeparationPattern = FoldInSapRegex(stored.SeparationPattern, profile),
                // The old manual-input mode aborted the save when the operator cancelled the prompt, so
                // "no value entered" already meant "don't file this document".
                RequireIdentifier = stored.IdMode == DocumentIdMode.ManualInput
            };
        }
        return new DocumentWorkflowSettings
        {
            Version = CURRENT_VERSION,
            SeparationMode = SeparationModeFor(autoSave?.Separator),
            BarcodeSymbologies = autoSave?.Separator == SaveSeparator.Code39Barcode
                ? [BarcodeSymbology.Code39]
                : [],
            SeparationPattern = FoldInSapRegex(autoSave?.Code39SeparationPattern, profile),
            // Legacy Code 39 separation kept the barcode page, legacy patch-T dropped it.
            KeepSeparatorPage = autoSave?.Separator != SaveSeparator.PatchT,
            // A patch-T sheet carries no value to compare, so every sheet has to keep separating. Barcode
            // separation gets the same default as a new profile.
            NewDocumentOnlyOnValueChange = autoSave?.Separator != SaveSeparator.PatchT,
            IdMode = DocumentIdMode.None,
            UploadTrigger = UploadTrigger.Automatic,
            // Saving was what auto save meant, so a profile that had it on keeps writing to the same path.
            SaveLocally = profile?.EnableAutoSave ?? false,
            LocalFolder = FolderOf(autoSave?.FilePath),
            DocumentNameTemplate = NameOf(autoSave?.FilePath),
            PromptForFilePath = autoSave?.PromptForFilePath ?? false,
            CleanupAfterCompletion = autoSave?.ClearImagesAfterSaving ?? false
        };
    }

    /// <summary>
    /// A profile that only ever set the SAP object key regex keeps it, now as the profile's one barcode
    /// pattern. Without this the pattern such a profile relied on would simply be gone after the update,
    /// and it would start archiving under whichever barcode happened to come first on the page.
    /// </summary>
    private static string? FoldInSapRegex(string? pattern, ScanProfile? profile)
    {
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            return pattern;
        }
        var sapRegex = profile?.SapArchiveSettings?.BarcodeRegex;
        return string.IsNullOrWhiteSpace(sapRegex) ? pattern : sapRegex;
    }

    private static DocumentSeparationMode SeparationModeFor(SaveSeparator? separator) => separator switch
    {
        SaveSeparator.Code39Barcode => DocumentSeparationMode.Barcode,
        SaveSeparator.PatchT => DocumentSeparationMode.PatchT,
        SaveSeparator.FilePerPage => DocumentSeparationMode.OnePerPage,
        _ => DocumentSeparationMode.None
    };

    /// <summary>
    /// Splits a legacy full-path template into its folder and its file name. Placeholders never contain a
    /// path separator, so the split lands where it would for a literal path.
    /// </summary>
    private static string? FolderOf(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }
        var folder = Path.GetDirectoryName(template);
        return string.IsNullOrWhiteSpace(folder) ? null : folder;
    }

    private static string? NameOf(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }
        var name = Path.GetFileName(template);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Whether the page that marked the boundary stays in the document.
    /// </summary>
    /// <remarks>
    /// Only barcode separation gets a say. A patch-T sheet is a reusable separator card with nothing on
    /// it, so keeping it would file a blank page at the front of every document; the question is only
    /// real for barcode separation, where the sheet carrying the order number is usually the document's
    /// own cover sheet and has to be kept.
    /// </remarks>
    public bool KeepsSeparatorPage() =>
        SeparationMode != DocumentSeparationMode.PatchT && KeepSeparatorPage;

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
    /// The area to hand the detector, or null for the whole page.
    /// </summary>
    /// <remarks>
    /// Everything downstream asks this rather than reading the two properties, so "restricted" and
    /// "restricted to the whole page" cannot be told apart by accident, and a stored area that has
    /// collapsed to nothing -- a hand-edited profile -- searches the whole page instead of nothing at
    /// all. <see cref="ScanPerformer"/> says in the console which of the two happened.
    /// </remarks>
    public BarcodeSearchArea? GetBarcodeSearchArea()
    {
        if (!RestrictBarcodeArea || BarcodeArea == null || !BarcodeArea.IsUsable)
        {
            return null;
        }
        var area = BarcodeArea.Normalized();
        return area.IsWholePage ? null : area;
    }

    /// <summary>
    /// Whether this profile needs barcodes decoded at all.
    /// </summary>
    /// <remarks>
    /// Only the two modes that read the paper count. One-document-per-page and one-per-scan split by
    /// counting, not by anything printed, so listing them here would turn detection on for every profile
    /// that files one page at a time -- which is the default a plain profile ends up with.
    /// </remarks>
    public bool RequiresBarcodeDetection() =>
        SeparationMode is DocumentSeparationMode.Barcode or DocumentSeparationMode.PatchT ||
        IdMode == DocumentIdMode.Barcode ||
        BarcodeSymbologies.Count > 0;
}
