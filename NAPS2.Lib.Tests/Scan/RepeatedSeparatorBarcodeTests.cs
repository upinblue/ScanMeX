#nullable enable
using NAPS2.Images;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// The paperwork for one process order repeats the order barcode on every cover sheet it contains. The
/// customer's 22-page sample carries 90714456 on seven of its pages -- the accompanying document, four
/// route cards, the manufacturing instruction and the storage slip -- and is meant to come out as a
/// single file named after that order. Treating each of those sheets as a boundary produced seven files
/// with the same name instead, which is why <see cref="DocumentWorkflowSettings.NewDocumentOnlyOnValueChange"/>
/// exists.
/// </summary>
public class RepeatedSeparatorBarcodeTests : ContextualTests
{
    private const string Order = "90714456";
    private const string OtherOrder = "90872237";

    // The pages that carry the order barcode in the sample, zero-based.
    private static readonly int[] BarcodePages = [0, 3, 8, 12, 13, 19, 21];

    private static DocumentWorkflowSettings Settings(bool onlyOnChange, bool keepSeparatorPage = true) => new()
    {
        SeparationMode = DocumentSeparationMode.Barcode,
        BarcodeSymbologies = [BarcodeSymbology.Code39],
        // Only the eight-digit order number, which is what names the file. The same sheets also carry a
        // ten-digit batch code and, on the first page, both concatenated into one long code.
        SeparationPattern = @"^\d{8}$",
        KeepSeparatorPage = keepSeparatorPage,
        NewDocumentOnlyOnValueChange = onlyOnChange
    };

    [Fact]
    public void OneOrderStaysOneDocument()
    {
        var pages = SamplePages();

        var docs = DocumentSeparator.Separate(pages, Settings(onlyOnChange: true)).ToList();

        var doc = Assert.Single(docs);
        Assert.Equal(Order, doc.SeparatorBarcodeValue);
        Assert.Equal(22, doc.Images.Count);
        Assert.Equal(0, doc.StartPageIndex);
    }

    [Fact]
    public void TurningTheOptionOffSplitsAtEveryBarcodePage()
    {
        var pages = SamplePages();

        var docs = DocumentSeparator.Separate(pages, Settings(onlyOnChange: false)).ToList();

        Assert.Equal(BarcodePages.Length, docs.Count);
        Assert.All(docs, x => Assert.Equal(Order, x.SeparatorBarcodeValue));
        Assert.Equal(BarcodePages, docs.Select(x => x.StartPageIndex));
    }

    [Fact]
    public void AStackOfSeveralOrdersStillSplitsWhereTheOrderChanges()
    {
        var pages = CreatePages(6);
        SetBarcodes(pages, 0, Order);
        SetBarcodes(pages, 2, Order);
        SetBarcodes(pages, 3, OtherOrder);
        SetBarcodes(pages, 5, OtherOrder);

        var docs = DocumentSeparator.Separate(pages, Settings(onlyOnChange: true)).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal(Order, docs[0].SeparatorBarcodeValue);
        Assert.Equal(3, docs[0].Images.Count);
        Assert.Equal(OtherOrder, docs[1].SeparatorBarcodeValue);
        Assert.Equal(3, docs[1].Images.Count);
    }

    /// <summary>
    /// A profile that drops separator sheets has to drop the repeated one too. Keeping it because it no
    /// longer marks a boundary would slip a sheet into the document that the profile says never belongs
    /// in one.
    /// </summary>
    [Fact]
    public void ARepeatedSeparatorSheetIsStillDroppedWhenSeparatorPagesAreNotKept()
    {
        var pages = CreatePages(4);
        SetBarcodes(pages, 0, Order);
        SetBarcodes(pages, 2, Order);

        var docs = DocumentSeparator.Separate(pages, Settings(onlyOnChange: true, keepSeparatorPage: false))
            .ToList();

        var doc = Assert.Single(docs);
        Assert.Equal(Order, doc.SeparatorBarcodeValue);
        Assert.Equal(2, doc.Images.Count);
    }

    /// <summary>
    /// Patch-T sheets carry no value, so there is nothing for the comparison to match on and every sheet
    /// has to keep separating regardless of the setting.
    /// </summary>
    [Fact]
    public void PatchTSheetsAlwaysSeparate()
    {
        var pages = CreatePages(4);
        SetBarcodes(pages, 0, "PATCHT");
        SetBarcodes(pages, 2, "PATCHT");

        var docs = DocumentSeparator.Separate(pages, new DocumentWorkflowSettings
        {
            SeparationMode = DocumentSeparationMode.PatchT,
            KeepSeparatorPage = false,
            NewDocumentOnlyOnValueChange = true
        }).ToList();

        Assert.Equal(2, docs.Count);
    }

    private List<ProcessedImage> SamplePages()
    {
        var pages = CreatePages(22);
        foreach (var index in BarcodePages)
        {
            // Reading order on these sheets: the order number first, the batch code below it. The first
            // page additionally carries the two concatenated in one wide code, and that one reads first.
            if (index == 0)
            {
                SetBarcodes(pages, index, "907144560816024365", Order, "0816024365");
            }
            else
            {
                SetBarcodes(pages, index, Order, "0816024365");
            }
        }
        return pages;
    }

    private List<ProcessedImage> CreatePages(int count) =>
        Enumerable.Range(0, count).Select(_ => CreateScannedImage()).ToList();

    private static void SetBarcodes(List<ProcessedImage> pages, int index, params string[] values)
    {
        pages[index] = pages[index].WithPostProcessingData(pages[index].PostProcessingData with
        {
            Barcode = new Barcode(true, true, values[0], "CODE_39")
            {
                AllDetections = values.Select(x => new BarcodeValue(x, "CODE_39")).ToList()
            }
        }, true);
    }
}
