#nullable enable
using NAPS2.ImportExport;
using NAPS2.Images;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// One page carrying several Code 39 barcodes, with a regex that only matches one of them. The barcode
/// the regex picks has to be the one the document is named and archived under -- the page's first
/// barcode in reading order is an accident of layout, not the operator's intent.
/// </summary>
public class MultiBarcodePageTests : ContextualTests
{
    // A production paper: the long concatenated code reads first, the document number reads second.
    private const string PagePrimary = "907144560816024365";
    private const string WantedBarcode = "DOC-4711";

    [Fact]
    public void Separation_PicksTheBarcodeMatchingThePattern()
    {
        var pages = CreatePages(2);
        SetBarcodes(pages, 0, new BarcodeValue(PagePrimary, "CODE_39"), new BarcodeValue(WantedBarcode, "CODE_39"));

        var result = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            SeparationPattern = @"^DOC-\d+$"
        }).ToList();

        Assert.Single(result);
        Assert.Equal(WantedBarcode, result[0].SeparatorBarcodeValue);
    }

    [Fact]
    public void SapKey_UsesASecondaryBarcodeWhenTheRegexOnlyMatchesThat()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: null,
            pageBarcodes: [PagePrimary],
            secondaryPageBarcodes: [WantedBarcode],
            pattern: @"^DOC-(\d+)$");

        Assert.Equal("4711", key);
    }

    /// <summary>
    /// The primaries are still what decides when they can. Widening the search only where the old logic
    /// gave up keeps every document that resolves today resolving to the same key.
    /// </summary>
    [Fact]
    public void SapKey_PrimaryStillWinsWhenItMatchesTheRegex()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: null,
            pageBarcodes: ["DOC-1000"],
            secondaryPageBarcodes: ["DOC-2000"],
            pattern: @"^DOC-(\d+)$");

        Assert.Equal("1000", key);
    }

    /// <summary>
    /// Without a regex there is nothing to tell a page's barcodes apart, so the secondary ones stay out
    /// of it rather than turning a resolvable document into an ambiguous one.
    /// </summary>
    [Fact]
    public void SapKey_WithoutARegexOnlyThePrimariesCount()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: null,
            pageBarcodes: [PagePrimary, PagePrimary],
            secondaryPageBarcodes: [WantedBarcode],
            pattern: null);

        Assert.Equal(PagePrimary, key);
    }

    [Fact]
    public void SapKey_SecondaryBarcodesThatDisagreeGiveNoKey()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: null,
            pageBarcodes: [PagePrimary],
            secondaryPageBarcodes: ["DOC-4711", "DOC-4712"],
            pattern: @"^DOC-(\d+)$");

        Assert.Null(key);
    }

    /// <summary>
    /// The file name side of the same problem: with a separation pattern configured but no separator
    /// value for this document, $(barcode) must not fall back to a barcode the pattern rejected.
    /// </summary>
    [Fact]
    public void FileName_BarcodePlaceholderRespectsTheSeparationPattern()
    {
        var ctx = ContextWithPattern(@"^DOC-\d+$",
            new DetectedBarcode(PagePrimary, "CODE_39", 0, false),
            new DetectedBarcode(WantedBarcode, "CODE_39", 0, false));

        Assert.Equal($"scan_{WantedBarcode}.pdf",
            new FileNamePlaceholders().SubstitutePlaceholders("scan_$(barcode).pdf", ctx));
    }

    [Fact]
    public void FileName_BarcodePlaceholderIsEmptyWhenNoBarcodeMatchesThePattern()
    {
        var ctx = ContextWithPattern(@"^DOC-\d+$", new DetectedBarcode(PagePrimary, "CODE_39", 0, false));

        Assert.Equal("scan_.pdf",
            new FileNamePlaceholders().SubstitutePlaceholders("scan_$(barcode).pdf", ctx));
    }

    [Fact]
    public void FileName_WithoutAPatternTheFirstBarcodeIsStillUsed()
    {
        var ctx = ContextWithPattern(null,
            new DetectedBarcode(PagePrimary, "CODE_39", 0, false),
            new DetectedBarcode(WantedBarcode, "CODE_39", 0, false));

        Assert.Equal($"scan_{PagePrimary}.pdf",
            new FileNamePlaceholders().SubstitutePlaceholders("scan_$(barcode).pdf", ctx));
    }

    private static ScanContext ContextWithPattern(string? pattern, params DetectedBarcode[] barcodes) =>
        new()
        {
            Timestamp = new DateTime(2024, 1, 1),
            Profile = new ScanProfile
            {
                DisplayName = "Test",
                DocumentWorkflow = new DocumentWorkflowSettings
                {
                    SeparationMode = DocumentSeparationMode.Barcode,
                    BarcodeSymbologies = [BarcodeSymbology.Code39],
                    SeparationPattern = pattern
                }
            },
            Barcodes = barcodes
        };

    private List<ProcessedImage> CreatePages(int count) =>
        CreateScannedImages(Enumerable.Repeat(ImageResources.dog, count).ToArray()).ToList();

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
