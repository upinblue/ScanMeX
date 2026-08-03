using NAPS2.Sap;
using Xunit;

namespace NAPS2.Lib.Tests.Sap;

public class SapObjectKeyResolverTests
{
    /// <summary>
    /// The real case this was written for: a production paper carrying several Code 39 barcodes. The
    /// page's primary barcode is the concatenated one, while the separation pattern picked the process
    /// order. Archiving must use the process order, so the file name and the object key agree.
    /// </summary>
    [Fact]
    public void SeparatorBarcodeWinsOverThePagePrimary()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: "90714456",
            pageBarcodes: ["907144560816024365", null, null],
            pattern: null);

        Assert.Equal("90714456", key);
    }

    [Fact]
    public void PageBarcodesAreUsedWhenThereIsNoSeparatorValue()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: null,
            pageBarcodes: ["90714456", null],
            pattern: null);

        Assert.Equal("90714456", key);
    }

    [Fact]
    public void PageBarcodesGiveNoKeyWhenTheDocumentDisagrees()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: null,
            pageBarcodes: ["90714456", "90714457"],
            pattern: null);

        Assert.Null(key);
    }

    [Fact]
    public void TheRegexIsAppliedToTheSeparatorValue()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: "907144560816024365",
            pageBarcodes: [],
            pattern: @"^(\d{8})");

        Assert.Equal("90714456", key);
    }

    /// <summary>
    /// A separator value the regex rejects must not abort the upload outright while the pages still
    /// agree on a usable value.
    /// </summary>
    [Fact]
    public void PageBarcodesAreTheFallbackWhenTheRegexRejectsTheSeparatorValue()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            separatorBarcodeValue: "PATCHT",
            pageBarcodes: ["90714456", "90714456"],
            pattern: @"^(\d{8})$");

        Assert.Equal("90714456", key);
    }

    [Fact]
    public void NoBarcodesAtAllGiveNoKey()
    {
        var key = SapObjectKeyResolver.FromScannedBarcodes(null, [null, null], null);

        Assert.Null(key);
    }

    [Theory]
    [InlineData("90714456", null, "90714456")]
    [InlineData("  90714456  ", null, "90714456")]
    [InlineData("907144560816024365", @"^(\d{8})", "90714456")]
    [InlineData("907144560816024365", @"^\d{8}$", null)]
    [InlineData("ABC-90714456", @"\d{8}", "90714456")]
    public void ExtractWithRegexCases(string value, string pattern, string expected)
    {
        Assert.Equal(expected, SapObjectKeyResolver.ExtractWithRegex(value, pattern));
    }
}
