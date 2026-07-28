#nullable enable
using NAPS2.Images;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

public class DocumentSeparatorTests : ContextualTests
{
    [Fact]
    public void NoSeparation_KeepsEverythingInOneDocument()
    {
        var pages = CreatePages(4);
        SetBarcode(pages, 2, "ORDER-1", "CODE_39");

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.None
        }).ToList();

        Assert.Single(result);
        Assert.Equal(4, result[0].Images.Count);
    }

    [Fact]
    public void BarcodePage_StartsNewDocumentAndSuppliesValue()
    {
        var pages = CreatePages(4);
        SetBarcode(pages, 0, "ORDER-1", "CODE_39");
        SetBarcode(pages, 2, "ORDER-2", "CODE_39");

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39]
        }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Images.Count);
        Assert.Equal(2, result[1].Images.Count);
        Assert.Equal("ORDER-1", result[0].SeparatorBarcodeValue);
        Assert.Equal("ORDER-2", result[1].SeparatorBarcodeValue);
        Assert.Equal(0, result[0].StartPageIndex);
        Assert.Equal(2, result[1].StartPageIndex);
    }

    [Fact]
    public void Regex_OnlyMatchingBarcodesSplit_AndGroupBecomesValue()
    {
        var pages = CreatePages(4);
        // Page 0 matches the pattern, page 2 carries a barcode that does not.
        SetBarcode(pages, 0, "AB-4711-X", "CODE_39");
        SetBarcode(pages, 2, "SOMETHING-ELSE", "CODE_39");

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            SeparationPattern = @"^AB-(\d+)-X$"
        }).ToList();

        Assert.Single(result);
        Assert.Equal(4, result[0].Images.Count);
        // Capturing group 1 wins so the file name gets just the number.
        Assert.Equal("4711", result[0].SeparatorBarcodeValue);
    }

    [Fact]
    public void WithoutCapturingGroup_WholeMatchBecomesValue()
    {
        var pages = CreatePages(2);
        SetBarcode(pages, 0, "AB-4711-X", "CODE_39");

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            SeparationPattern = @"\d+"
        }).ToList();

        Assert.Single(result);
        Assert.Equal("4711", result[0].SeparatorBarcodeValue);
    }

    [Fact]
    public void SelectedSymbology_IgnoresOtherBarcodeTypesOnTheSamePage()
    {
        var pages = CreatePages(4);
        // A production sheet carrying both an article code and the order code that should split.
        SetBarcodes(pages, 2,
            new BarcodeValue("5901234123457", "EAN_13"),
            new BarcodeValue("ORDER-2", "CODE_39"));
        // Page 1 only has an article code, so it must not start a document.
        SetBarcodes(pages, 1, new BarcodeValue("5901234123457", "EAN_13"));

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39]
        }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Images.Count);
        Assert.Equal("ORDER-2", result[1].SeparatorBarcodeValue);
    }

    [Fact]
    public void KeepSeparatorPage_False_DropsTheBarcodeSheet()
    {
        var pages = CreatePages(4);
        SetBarcode(pages, 0, "ORDER-1", "CODE_39");
        SetBarcode(pages, 2, "ORDER-2", "CODE_39");

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            KeepSeparatorPage = false
        }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Single(result[0].Images);
        Assert.Single(result[1].Images);
        // The value survives even though the page carrying it was dropped.
        Assert.Equal("ORDER-1", result[0].SeparatorBarcodeValue);
        Assert.Equal("ORDER-2", result[1].SeparatorBarcodeValue);
    }

    [Fact]
    public void PatchT_SplitsWithoutTreatingTheTextAsAValue()
    {
        var pages = CreatePages(4);
        SetBarcode(pages, 2, "PATCHT", "CODE_39");

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.PatchT
        }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Null(result[1].SeparatorBarcodeValue);
    }

    [Fact]
    public void UnknownFormat_StillCountsAsSeparator()
    {
        var pages = CreatePages(4);
        // Some import paths don't populate the format; those must not be silently ignored.
        SetBarcode(pages, 2, "ORDER-2", null);

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39]
        }).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("ORDER-2", result[1].SeparatorBarcodeValue);
    }

    [Fact]
    public void InvalidPattern_IsIgnoredRatherThanThrowing()
    {
        var pages = CreatePages(2);
        SetBarcode(pages, 0, "ORDER-1", "CODE_39");

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            SeparationPattern = "([unclosed"
        }).ToList();

        Assert.Single(result);
        Assert.Equal("ORDER-1", result[0].SeparatorBarcodeValue);
    }

    [Fact]
    public void ForProfile_DerivesLegacyCode39SettingsWhenWorkflowIsMissing()
    {
        var profile = new ScanProfile
        {
            AutoSaveSettings = new AutoSaveSettings
            {
                Separator = NAPS2.ImportExport.SaveSeparator.Code39Barcode,
                Code39SeparationPattern = @"^X\d+$"
            }
        };

        var workflow = DocumentWorkflowSettings.ForProfile(profile);

        Assert.Equal(DocumentSeparationMode.Barcode, workflow.SeparationMode);
        Assert.Equal([BarcodeSymbology.Code39], workflow.BarcodeSymbologies);
        Assert.Equal(@"^X\d+$", workflow.SeparationPattern);
        Assert.True(workflow.KeepSeparatorPage);
    }

    private List<ProcessedImage> CreatePages(int count) =>
        CreateScannedImages(Enumerable.Repeat(ImageResources.dog, count).ToArray()).ToList();

    private static void SetBarcode(List<ProcessedImage> pages, int index, string text, string? format) =>
        SetBarcodes(pages, index, new BarcodeValue(text, format));

    private static void SetBarcodes(List<ProcessedImage> pages, int index, params BarcodeValue[] values)
    {
        var primary = values[0];
        pages[index] = pages[index].WithPostProcessingData(pages[index].PostProcessingData with
        {
            Barcode = new Barcode(true, true, primary.Text, primary.Format)
            {
                AllDetections = values.ToList()
            }
        }, true);
    }
}
