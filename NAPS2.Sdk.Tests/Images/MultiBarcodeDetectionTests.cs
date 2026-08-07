using NAPS2.Images;
using NAPS2.Scan;
using Xunit;
using ZXing.Common;
using ZXing.OneD;

namespace NAPS2.Sdk.Tests.Images;

/// <summary>
/// A page can carry more than one barcode -- a production paper with an order code and a document code
/// is the usual case. Everything downstream (which barcode separates, which one becomes the SAP object
/// key) can only pick the right one if detection reports all of them, so this covers the multi-decode
/// itself rather than any one caller.
/// </summary>
public class MultiBarcodeDetectionTests : ContextualTests
{
    // A4 at 300 dpi, the resolution barcode separation is documented to need.
    private const int PageW = 2480;
    private const int PageH = 3508;

    [Theory]
    [InlineData("stacked", 200, 300, 200, 2000)]
    [InlineData("close together", 200, 300, 200, 700)]
    [InlineData("side by side", 150, 400, 1400, 400)]
    [InlineData("opposite corners", 150, 300, 1400, 2900)]
    public void BothBarcodesAreReported(string _, int x1, int y1, int x2, int y2)
    {
        using var image = CreatePage(("ABC123", x1, y1, 1000, 180), ("XYZ789", x2, y2, 1000, 180));

        var all = Detect(image);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.Text == "ABC123");
        Assert.Contains(all, x => x.Text == "XYZ789");
    }

    [Fact]
    public void ThreeBarcodesAreAllReportedInReadingOrder()
    {
        using var image = CreatePage(
            ("ABC123", 200, 300, 1000, 200),
            ("XYZ789", 200, 900, 1000, 200),
            ("QRS456", 200, 1500, 1000, 200));

        var all = Detect(image);

        Assert.Equal(["ABC123", "XYZ789", "QRS456"], all.Select(x => x.Text));
    }

    /// <summary>
    /// The primary is only the first match for the profile's symbologies, so it says nothing about which
    /// barcode identifies the document. Callers that need a specific one have to go through AllDetections.
    /// </summary>
    [Fact]
    public void ThePrimaryIsTheFirstBarcodeInReadingOrder()
    {
        using var image = CreatePage(("ABC123", 200, 300, 1000, 200), ("XYZ789", 200, 900, 1000, 200));

        var barcode = BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.Code39]
        });

        Assert.Equal("ABC123", barcode.DetectedText);
        Assert.Equal(2, barcode.GetAllValues().Count);
    }

    /// <summary>
    /// A page is decoded twice, once at full size and once shrunk, because a real invoice page in the
    /// customer's samples only yields its barcode on the smaller copy. Both passes read the same codes
    /// here, so this pins the merge: a code found by both must appear once, not twice, and the list must
    /// still be in page reading order -- the first entry is what becomes the page's primary barcode.
    /// </summary>
    [Fact]
    public void TheTwoDetectionPassesAreMergedWithoutDuplicatesOrReordering()
    {
        using var image = CreatePage(
            ("ABC123", 200, 300, 1200, 220),
            ("XYZ789", 200, 1200, 1200, 220),
            ("QRS456", 200, 2100, 1200, 220));

        var all = Detect(image);

        Assert.Equal(["ABC123", "XYZ789", "QRS456"], all.Select(x => x.Text));
        Assert.Equal(all.Select(x => x.Text).Distinct().Count(), all.Count);
    }

    /// <summary>
    /// The downscaled pass is skipped for images that are already small, where shrinking would drop bars
    /// rather than noise. Detection still has to work on them.
    /// </summary>
    [Fact]
    public void ASmallImageIsStillDecoded()
    {
        using var image = ImageContext.Create(1000, 400, ImagePixelFormat.RGB24);
        Draw(image, [("ABC123", 50, 100, 900, 200)]);

        var all = Detect(image);

        Assert.Equal(["ABC123"], all.Select(x => x.Text));
    }

    private static IReadOnlyList<BarcodeValue> Detect(IMemoryImage image) =>
        BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.Code39]
        }).GetAllValues();

    private IMemoryImage CreatePage(params (string Text, int X, int Y, int W, int H)[] barcodes)
    {
        var image = ImageContext.Create(PageW, PageH, ImagePixelFormat.RGB24);
        Draw(image, barcodes);
        return image;
    }

    private static void Draw(IMemoryImage image, (string Text, int X, int Y, int W, int H)[] barcodes)
    {
        var pageW = image.Width;
        var pageH = image.Height;
        var writer = new Code39Writer();
        var black = new bool[pageW, pageH];
        foreach (var bc in barcodes)
        {
            BitMatrix matrix = writer.encode(bc.Text, ZXing.BarcodeFormat.CODE_39, bc.W, bc.H);
            for (var y = 0; y < matrix.Height; y++)
            {
                for (var x = 0; x < matrix.Width; x++)
                {
                    if (matrix[x, y] && bc.X + x < pageW && bc.Y + y < pageH)
                    {
                        black[bc.X + x, bc.Y + y] = true;
                    }
                }
            }
        }
        using var lockState = image.Lock(LockMode.WriteOnly, out var data);
        unsafe
        {
            for (var y = 0; y < pageH; y++)
            {
                var row = data.ptr + (data.invertY ? pageH - 1 - y : y) * data.stride;
                for (var x = 0; x < pageW; x++)
                {
                    var p = row + x * data.bytesPerPixel;
                    var value = (byte) (black[x, y] ? 0 : 255);
                    if (data.invertColorSpace)
                    {
                        value = (byte) (255 - value);
                    }
                    p[data.rOff] = value;
                    p[data.gOff] = value;
                    p[data.bOff] = value;
                }
            }
        }
    }
}
