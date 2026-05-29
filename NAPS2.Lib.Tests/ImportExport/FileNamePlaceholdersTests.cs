#nullable enable

using NAPS2.ImportExport;
using NAPS2.Scan;
using Xunit;

namespace NAPS2.Lib.Tests.ImportExport;

public class FileNamePlaceholdersTests
{
    private readonly FileNamePlaceholders _placeholders = new();

    [Fact]
    public void ExistingDateAndNumberPlaceholdersStillWork()
    {
        var ctx = Context(timestamp: new DateTime(2024, 5, 6, 7, 8, 9), sequenceIndex: 0);

        var result = _placeholders.SubstitutePlaceholders("Rechnung_$(YYYY)_$(MM)_$(DD)_$(nnn).pdf", ctx);

        Assert.Equal("Rechnung_2024_05_06_001.pdf", result);
    }

    [Fact]
    public void BarcodeWithoutContextBecomesEmptyString()
    {
        var result = _placeholders.SubstitutePlaceholders("scan_$(barcode).pdf", Context());

        Assert.Equal("scan_.pdf", result);
    }

    [Fact]
    public void BarcodeWithOneBarcodeUsesValue()
    {
        var result = _placeholders.SubstitutePlaceholders("scan_$(barcode).pdf",
            Context(barcodes: [new DetectedBarcode("ABC", "CODE128", 0, false)]));

        Assert.Equal("scan_ABC.pdf", result);
    }

    [Fact]
    public void BarcodeByOneBasedIndexUsesNthValueOrEmpty()
    {
        var ctx = Context(barcodes:
        [
            new DetectedBarcode("A", "CODE128", 0, false),
            new DetectedBarcode("B", "QR", 0, false)
        ]);

        Assert.Equal("B", _placeholders.SubstitutePlaceholders("$(barcode:2)", ctx));
        Assert.Equal("", _placeholders.SubstitutePlaceholders("$(barcode:3)", ctx));
    }

    [Fact]
    public void BarcodeByTypeUsesFirstMatchingType()
    {
        var ctx = Context(barcodes:
        [
            new DetectedBarcode("A", "CODE128", 0, false),
            new DetectedBarcode("Q", "QR", 0, false)
        ]);

        Assert.Equal("Q", _placeholders.SubstitutePlaceholders("$(barcode:type=QR)", ctx));
    }

    [Fact]
    public void BarcodeRegexMatchesWholeValueWhenNoGroupExists()
    {
        var ctx = Context(barcodes:
        [
            new DetectedBarcode("ABC", "CODE128", 0, false),
            new DetectedBarcode("1234567890", "CODE128", 0, false)
        ]);

        Assert.Equal("1234567890", _placeholders.SubstitutePlaceholders("$(barcode:regex=^\\d{10}$)", ctx));
    }

    [Fact]
    public void BarcodeRegexReturnsFirstGroupWhenPresent()
    {
        var ctx = Context(barcodes: [new DetectedBarcode("BC-987", "CODE128", 0, false)]);

        Assert.Equal("987", _placeholders.SubstitutePlaceholders("$(barcode:regex=BC-(\\d+))", ctx));
    }

    [Fact]
    public void SeparatorBarcodeValueHasPriorityForDefaultBarcode()
    {
        var ctx = Context(separatorBarcodeValue: "SEP", barcodes: [new DetectedBarcode("A", "CODE128", 0, false)]);

        Assert.Equal("SEP", _placeholders.SubstitutePlaceholders("$(barcode)", ctx));
    }

    [Fact]
    public void CombinedTemplateResolvesAllTokens()
    {
        var ctx = Context(
            timestamp: new DateTime(2024, 1, 2),
            sequenceIndex: 0,
            barcodes: [new DetectedBarcode("4711", "CODE128", 0, false)]);

        var result = _placeholders.SubstitutePlaceholders("Rechnung_$(YYYY)_$(barcode)_$(nnn).pdf", ctx);

        Assert.Equal("Rechnung_2024_4711_001.pdf", result);
    }

    [Fact]
    public void SanitizeForFileNameReplacesInvalidCharacters()
    {
        Assert.Equal("a_b_c_d_e_f_g_h_i", FileNamePlaceholders.SanitizeForFileName("a/b\\c:d*e?f\"g<h>i"));
    }

    private static ScanContext Context(
        DateTime? timestamp = null,
        int sequenceIndex = 0,
        IReadOnlyList<DetectedBarcode>? barcodes = null,
        string? separatorBarcodeValue = null)
    {
        return new ScanContext
        {
            Timestamp = timestamp ?? new DateTime(2024, 1, 1),
            SequenceIndex = sequenceIndex,
            Profile = new ScanProfile { DisplayName = "Profile/Name" },
            Barcodes = barcodes ?? Array.Empty<DetectedBarcode>(),
            SeparatorBarcodeValue = separatorBarcodeValue
        };
    }
}
