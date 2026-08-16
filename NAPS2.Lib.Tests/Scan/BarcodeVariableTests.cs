#nullable enable
using NAPS2.ImportExport;
using NAPS2.Images;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// What lands in the barcode variables. A production paper carries several Code 39 codes -- an order
/// number, an article number, a route card number -- and which one reads first is a property of where it
/// sits on the sheet. The profile's regex is the only statement of which one the operator means, so it is
/// what decides the value of <c>$(barcode)</c> and <c>$(barcode:1)</c>; without that the document is filed
/// under a plausible-looking but wrong number, which nobody spots afterwards.
/// </summary>
public class BarcodeVariableTests : ContextualTests
{
    private const string ArticleCode = "907144560816024365";
    private const string OrderCode = "DOC-4711";
    private const string RouteCard = "RK-99";
    private const string OrderPattern = @"^DOC-\d+$";

    [Fact]
    public void FirstVariable_IsTheBarcodeMatchingTheRegex_NotTheFirstInReadingOrder()
    {
        var pages = CreatePages(1);
        SetBarcodes(pages, 0, Code39(ArticleCode), Code39(OrderCode));

        var barcodes = Extract(pages, OrderPattern);

        Assert.Equal(OrderCode, barcodes[0].Value);
    }

    [Fact]
    public void FirstVariable_IsPromotedAcrossPages()
    {
        // The cover sheet with the order barcode is not always the first page that carries any barcode.
        var pages = CreatePages(2);
        SetBarcodes(pages, 0, Code39(ArticleCode));
        SetBarcodes(pages, 1, Code39(OrderCode));

        var barcodes = Extract(pages, OrderPattern);

        Assert.Equal(OrderCode, barcodes[0].Value);
    }

    [Fact]
    public void RemainingVariables_KeepReadingOrder()
    {
        var pages = CreatePages(1);
        SetBarcodes(pages, 0, Code39(ArticleCode), Code39(OrderCode), Code39(RouteCard));

        var barcodes = Extract(pages, OrderPattern);

        Assert.Equal([OrderCode, ArticleCode, RouteCard], barcodes.Select(x => x.Value));
    }

    [Fact]
    public void WithoutARegex_ThePrimaryStaysFirst()
    {
        var pages = CreatePages(1);
        SetBarcodes(pages, 0, Code39(ArticleCode), Code39(OrderCode));

        var barcodes = Extract(pages, null);

        Assert.Equal(ArticleCode, barcodes[0].Value);
    }

    [Fact]
    public void AnInvalidRegexIsIgnoredRatherThanLosingEveryBarcode()
    {
        var pages = CreatePages(1);
        SetBarcodes(pages, 0, Code39(ArticleCode), Code39(OrderCode));

        var barcodes = Extract(pages, "DOC-[");

        Assert.Equal([ArticleCode, OrderCode], barcodes.Select(x => x.Value));
    }

    /// <summary>
    /// The cap exists to keep a noisy page from flooding the variables, but it must never be what drops
    /// the one barcode the profile asked for.
    /// </summary>
    [Fact]
    public void TheMatchingBarcodeSurvivesThePerPageCap()
    {
        var pages = CreatePages(1);
        SetBarcodes(pages, 0,
            Code39("N1"), Code39("N2"), Code39("N3"), Code39("N4"), Code39("N5"), Code39(OrderCode));

        var barcodes = new BarcodeExtractor { MaxBarcodesPerPage = 3, SelectionPattern = OrderPattern }
            .Extract(pages);

        Assert.Equal(3, barcodes.Count);
        Assert.Equal(OrderCode, barcodes[0].Value);
    }

    /// <summary>
    /// $(barcode) applies the regex's capturing group, the numbered variables hold the raw code. Both have
    /// to name the same barcode -- a file called after the order and an object key taken from the article
    /// number is the failure this ordering exists to prevent.
    /// </summary>
    [Fact]
    public void BarcodePlaceholderAndFirstVariableNameTheSameCode()
    {
        var pages = CreatePages(1);
        SetBarcodes(pages, 0, Code39(ArticleCode), Code39(OrderCode));
        var profile = ProfileWithSeparationPattern(@"^DOC-(\d+)$");
        var ctx = new ScanContext
        {
            Timestamp = new DateTime(2024, 1, 1),
            Profile = profile,
            Images = pages,
            Barcodes = new BarcodeExtractor { SelectionPattern = profile.GetBarcodeSelectionPattern() }
                .Extract(pages)
        };

        var placeholders = new FileNamePlaceholders();
        Assert.Equal("4711", placeholders.SubstitutePlaceholders("$(barcode)", ctx));
        Assert.Equal(OrderCode, placeholders.SubstitutePlaceholders("$(barcode:1)", ctx));
    }

    /// <summary>
    /// A profile that archives to SAP without separating has no separation pattern; there the SAP object
    /// key regex is the operator's only statement of which barcode matters.
    /// </summary>
    [Fact]
    public void SapRegexSelectsTheBarcodeWhenThereIsNoSeparationPattern()
    {
        var profile = new ScanProfile
        {
            SapArchiveSettings = new SapArchiveProfileSettings
            {
                EnableUpload = true,
                BarcodeSource = BarcodeSource.FromScannedBarcode,
                BarcodeRegex = OrderPattern
            }
        };

        Assert.Equal(OrderPattern, profile.GetBarcodeSelectionPattern());
    }

    [Fact]
    public void TheSeparationPatternWinsOverTheSapRegex()
    {
        var profile = ProfileWithSeparationPattern(OrderPattern);
        profile.SapArchiveSettings = new SapArchiveProfileSettings { BarcodeRegex = @"^RK-\d+$" };

        Assert.Equal(OrderPattern, profile.GetBarcodeSelectionPattern());
    }

    private static ScanProfile ProfileWithSeparationPattern(string? pattern) => new()
    {
        DisplayName = "Test",
        DocumentWorkflow = new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.Barcode,
            BarcodeSymbologies = [BarcodeSymbology.Code39],
            SeparationPattern = pattern
        }
    };

    private static IReadOnlyList<DetectedBarcode> Extract(List<ProcessedImage> pages, string? pattern) =>
        new BarcodeExtractor { SelectionPattern = pattern }.Extract(pages);

    private static BarcodeValue Code39(string text) => new(text, "CODE_39");

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
