#nullable enable
using NAPS2.ImportExport;
using NAPS2.Sap;
using NAPS2.Scan;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// Whether a profile decodes barcodes, and with what restriction. <see cref="BarcodeDetectionGateTests"/>
/// covers the "does this profile need barcodes at all" half; this covers the half that decides whether
/// the search is narrow enough to be trusted. Both failure directions are silent in the finished
/// document -- detection that never runs leaves the placeholder empty, detection that runs unrestricted
/// invents a value -- so neither can be left to a scanner to reveal.
/// </summary>
public class BarcodeDetectionPlanTests
{
    [Fact]
    public void AProfileThatNeedsNoBarcodesDoesNotDecode()
    {
        var plan = BarcodeDetectionPlan.For(new ScanProfile());

        Assert.False(plan.Detect);
        // Not wanting barcodes is the normal case, not a problem to report.
        Assert.Null(plan.SuppressedReason);
    }

    [Fact]
    public void SelectedSymbologiesAreHandedToTheDetector()
    {
        var profile = new ScanProfile
        {
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                SeparationMode = DocumentSeparationMode.Barcode,
                BarcodeSymbologies = [BarcodeSymbology.Code39, BarcodeSymbology.Code128]
            }
        };

        var plan = BarcodeDetectionPlan.For(profile);

        Assert.True(plan.Detect);
        Assert.Equal([BarcodeSymbology.Code39, BarcodeSymbology.Code128], plan.Symbologies);
        Assert.False(plan.PatchTOnly);
        Assert.Null(plan.SuppressedReason);
    }

    /// <summary>
    /// The strictness the operator set has to survive the trip to the detector, because there is nothing
    /// downstream that would reveal it hadn't: the profile would simply go on reading nothing off the
    /// damaged sheets it was lowered for, which is exactly the symptom it was lowered to fix.
    /// </summary>
    [Theory]
    [InlineData(BarcodeStrictness.Strict)]
    [InlineData(BarcodeStrictness.Tolerant)]
    [InlineData(BarcodeStrictness.VeryTolerant)]
    public void TheProfilesBarcodeStrictnessIsHandedToTheDetector(BarcodeStrictness strictness)
    {
        var profile = new ScanProfile
        {
            DocumentWorkflow = new DocumentWorkflowSettings
            {
                Version = DocumentWorkflowSettings.CURRENT_VERSION,
                SeparationMode = DocumentSeparationMode.Barcode,
                BarcodeSymbologies = [BarcodeSymbology.Code39],
                BarcodeStrictness = strictness
            }
        };

        var plan = BarcodeDetectionPlan.For(profile);

        Assert.True(plan.Detect);
        Assert.Equal(strictness, plan.Strictness);
    }

    /// <summary>
    /// Nothing anyone can set makes a patch-T sheet decodable when it is damaged -- the tolerant pass
    /// deliberately leaves patch-T alone -- so the legacy path stays strict rather than implying otherwise.
    /// </summary>
    [Fact]
    public void TheLegacyPatchTPathStaysStrict()
    {
        var plan = BarcodeDetectionPlan.For(new ScanProfile(), detectPatchT: true);

        Assert.True(plan.Detect);
        Assert.True(plan.PatchTOnly);
        Assert.Equal(BarcodeStrictness.Strict, plan.Strictness);
    }

    /// <summary>
    /// The case that produced barcodes which were not on the paper. Without a symbology ZXing tries every
    /// format it knows, and ITF and the EAN/UPC family decode the ruled tables of a dense form. Refusing
    /// to decode is the visible failure; decoding everything is the invisible one.
    /// </summary>
    [Fact]
    public void WantingBarcodesWithoutSelectingATypeDecodesNothingAndSaysWhy()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\$(barcode).pdf" }
        };

        var plan = BarcodeDetectionPlan.For(profile);

        Assert.False(plan.Detect);
        Assert.NotNull(plan.SuppressedReason);
        Assert.Contains("no barcode type is selected", plan.SuppressedReason);
    }

    [Fact]
    public void ASapObjectKeyFromTheBarcodeWithoutATypeAlsoDecodesNothing()
    {
        var profile = new ScanProfile
        {
            SapArchiveSettings = new SapArchiveProfileSettings
            {
                EnableUpload = true,
                BarcodeSource = BarcodeSource.FromScannedBarcode
            }
        };

        var plan = BarcodeDetectionPlan.For(profile);

        Assert.False(plan.Detect);
        Assert.NotNull(plan.SuppressedReason);
    }

    /// <summary>
    /// Patch-T is a restriction in its own right -- it is carried by Code 39 and has to read the one
    /// well-known text -- so a patch-T profile is not the unrestricted case and keeps decoding.
    /// </summary>
    [Fact]
    public void PatchTSeparationDecodesWithoutATickedSymbology()
    {
        var profile = new ScanProfile
        {
            DocumentWorkflow = new DocumentWorkflowSettings { SeparationMode = DocumentSeparationMode.PatchT }
        };

        var plan = BarcodeDetectionPlan.For(profile);

        Assert.True(plan.Detect);
        Assert.Equal([BarcodeSymbology.PatchT], plan.Symbologies);
        Assert.Null(plan.SuppressedReason);
    }

    /// <summary>
    /// Batch scanning asks for patch-T separator sheets without going through a profile setting.
    /// </summary>
    [Fact]
    public void BatchPatchTDecodesEvenOnAPlainProfile()
    {
        var plan = BarcodeDetectionPlan.For(new ScanProfile(), detectPatchT: true);

        Assert.True(plan.Detect);
        Assert.True(plan.PatchTOnly);
        Assert.Null(plan.SuppressedReason);
    }

    /// <summary>
    /// Profiles saved before symbologies were selectable carry the separator on the auto save settings.
    /// Those must keep working rather than being read as "wants barcodes but selected no type".
    /// </summary>
    [Theory]
    [InlineData(SaveSeparator.PatchT)]
    [InlineData(SaveSeparator.Code39Barcode)]
    public void ALegacySeparatorProfileStillDecodes(SaveSeparator separator)
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings { FilePath = @"C:\Scans\out.pdf", Separator = separator }
        };

        var plan = BarcodeDetectionPlan.For(profile);

        Assert.True(plan.Detect);
        Assert.Null(plan.SuppressedReason);
    }
}
