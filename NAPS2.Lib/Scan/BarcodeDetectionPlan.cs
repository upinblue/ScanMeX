using NAPS2.ImportExport;

namespace NAPS2.Scan;

/// <summary>
/// What barcode detection a profile asks for, and -- when it asks for something that cannot be answered
/// safely -- why nothing is decoded. Separated from <see cref="ScanPerformer"/> so the decision can be
/// tested without a scanner, because getting it wrong is silent in both directions: detection that never
/// runs leaves placeholders empty, and detection that runs unrestricted invents barcodes.
/// </summary>
public sealed record BarcodeDetectionPlan
{
    private BarcodeDetectionPlan()
    {
    }

    /// <summary>
    /// Whether the pages should be decoded at all.
    /// </summary>
    public bool Detect { get; private init; }

    /// <summary>
    /// The symbologies to look for. Never empty when <see cref="Detect"/> is true and
    /// <see cref="PatchTOnly"/> is false.
    /// </summary>
    public IReadOnlyList<BarcodeSymbology> Symbologies { get; private init; } = [];

    /// <summary>
    /// The legacy patch-T-only path, used by batch scanning and by profiles saved before symbologies
    /// were selectable. Patch-T is carried by Code 39, so this is a restriction too.
    /// </summary>
    public bool PatchTOnly { get; private init; }

    /// <summary>
    /// Set when the profile wants barcodes but no symbology restricts the search, in which case nothing
    /// is decoded. Null whenever detection runs, and null when the profile never wanted barcodes -- that
    /// is the normal case and not worth a warning.
    /// </summary>
    public string? SuppressedReason { get; private init; }

    /// <summary>
    /// Decides what to decode for a profile.
    /// </summary>
    /// <remarks>
    /// Detection without a symbology restriction is refused rather than run. ZXing then tries every
    /// format it knows -- ITF, Codabar and the EAN/UPC family among them -- and those have no usable
    /// self-check, so the ruled tables and dense print of a real form decode as barcodes that are not on
    /// the paper. Measured on a customer certificate carrying two Code 128 codes: restricted to
    /// Code 39 + Code 128 every noisy variant of the page yields exactly those two, while unrestricted
    /// the same variants yield three to five, one of which became the page's primary value. A phantom
    /// that names a file or an archive key is indistinguishable from a correct scan afterwards, whereas
    /// a profile that decodes nothing says so in the console on every scan.
    /// </remarks>
    public static BarcodeDetectionPlan For(ScanProfile profile, bool detectPatchT = false)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var workflow = DocumentWorkflowSettings.ForProfile(profile);
        var symbologies = workflow.GetEffectiveSymbologies().ToList();
        var legacySeparator = profile.AutoSaveSettings?.Separator;
        var legacyPatchT = detectPatchT ||
                           legacySeparator is SaveSeparator.PatchT or SaveSeparator.Code39Barcode;
        var wantsBarcodes = detectPatchT || profile.NeedsBarcodeValues() || legacyPatchT;

        if (!wantsBarcodes)
        {
            return new BarcodeDetectionPlan();
        }
        if (symbologies.Count > 0)
        {
            return new BarcodeDetectionPlan { Detect = true, Symbologies = symbologies };
        }
        if (legacyPatchT)
        {
            return new BarcodeDetectionPlan { Detect = true, PatchTOnly = true };
        }
        return new BarcodeDetectionPlan
        {
            SuppressedReason =
                "the profile uses barcodes but no barcode type is selected. Decoding without a type " +
                "restriction reads phantom EAN/UPC and ITF values out of the print noise of a dense " +
                "form, so nothing is decoded at all. Select the types the paperwork actually carries."
        };
    }
}
