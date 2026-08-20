#nullable enable
using NAPS2.Images;
using NAPS2.Scan;
using Xunit;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace NAPS2.Sdk.Tests.Images;

/// <summary>
/// Restricting detection to part of the page. Paperwork that always carries its barcode in the same
/// place can have the rest of the sheet ignored, which is the strongest defence there is against a
/// phantom read out of a ruled table -- the code that isn't looked at cannot be decoded at all.
/// </summary>
/// <remarks>
/// The page here carries two real barcodes, one near the top and one near the bottom, so every case
/// below is checked against something that is on the paper rather than against nothing: an area that
/// finds "no barcode" has to be the one that also fails to find the code it excluded, and an area that
/// finds one has to find the right one.
/// </remarks>
public class BarcodeSearchAreaDetectionTests : ContextualTests
{
    private const int PageW = 2480;
    private const int PageH = 3508;

    private const string HeaderCode = "010000398340";
    private const string FooterCode = "000100937929";

    /// <summary>
    /// The baseline: with no restriction the page yields both codes, so anything missing below is missing
    /// because of the area and not because the page was never readable.
    /// </summary>
    [Fact]
    public void WithNoSearchAreaTheWholePageIsRead()
    {
        using var image = CreatePage();

        var values = BarcodeDetector.Detect(image, Options(null)).GetAllValues();

        Assert.Equal([HeaderCode, FooterCode], values.Select(x => x.Text));
    }

    [Fact]
    public void ASearchAreaAtTheTopOfThePageIgnoresTheBarcodeAtTheBottom()
    {
        using var image = CreatePage();

        var barcode = BarcodeDetector.Detect(image, Options(BarcodeSearchArea.TopHeader));

        Assert.Equal([HeaderCode], barcode.GetAllValues().Select(x => x.Text));
        Assert.Equal(HeaderCode, barcode.DetectedText);
    }

    [Fact]
    public void ASearchAreaAtTheBottomOfThePageIgnoresTheBarcodeAtTheTop()
    {
        using var image = CreatePage();

        var barcode = BarcodeDetector.Detect(image, Options(BarcodeSearchArea.BottomFooter));

        Assert.Equal([FooterCode], barcode.GetAllValues().Select(x => x.Text));
        Assert.Equal(FooterCode, barcode.DetectedText);
    }

    /// <summary>
    /// An area that carries neither code reports nothing rather than reaching outside itself. This is the
    /// direction that costs an operator a document, so it is stated explicitly: a barcode outside the
    /// area is not detected, and the console line about the area is the only thing that says why.
    /// </summary>
    [Fact]
    public void ASearchAreaWithNoBarcodeInItFindsNothing()
    {
        using var image = CreatePage();

        var barcode = BarcodeDetector.Detect(image,
            Options(new BarcodeSearchArea { X = 0, Y = 0.4, Width = 1, Height = 0.2 }));

        Assert.True(barcode.IsDetectionAttempted);
        Assert.False(barcode.IsDetected);
        Assert.Empty(barcode.GetAllValues());
    }

    /// <summary>
    /// An area covering the page is the same instruction as no area at all, and must not go through the
    /// crop -- copying a 300 dpi page in order to hand it back unchanged is work on every page of every
    /// scan.
    /// </summary>
    [Fact]
    public void AnAreaCoveringTheWholePageReadsTheWholePage()
    {
        using var image = CreatePage();

        var values = BarcodeDetector.Detect(image, Options(BarcodeSearchArea.WholePage)).GetAllValues();

        Assert.Equal([HeaderCode, FooterCode], values.Select(x => x.Text));
    }

    /// <summary>
    /// The crop is a copy, and the page it was taken from belongs to the caller: the same image goes on
    /// to the thumbnail, to the file that is archived, and to the window. A crop that consumed it -- which
    /// is what <c>PerformTransform</c> does -- would take the scan with it.
    /// </summary>
    [Fact]
    public void TheSearchAreaLeavesThePageAlone()
    {
        using var image = CreatePage();

        BarcodeDetector.Detect(image, Options(BarcodeSearchArea.TopHeader));

        Assert.Equal(PageW, image.Width);
        Assert.Equal(PageH, image.Height);
        // Still readable, and still the whole page: nothing was cropped out of the caller's image.
        var values = BarcodeDetector.Detect(image, Options(null)).GetAllValues();
        Assert.Equal([HeaderCode, FooterCode], values.Select(x => x.Text));
    }

    /// <summary>
    /// A tiny area is a legitimate setting -- a barcode occupies a small part of a sheet -- and the
    /// downscaled second pass is skipped below its width threshold, so this checks the small-crop path
    /// reads its barcode at all.
    /// </summary>
    [Fact]
    public void ASearchAreaTightAroundTheBarcodeStillReadsIt()
    {
        using var image = CreatePage();

        var barcode = BarcodeDetector.Detect(image,
            Options(new BarcodeSearchArea { X = 0.2, Y = 0.06, Width = 0.55, Height = 0.09 }));

        Assert.Equal(HeaderCode, barcode.DetectedText);
    }

    private static BarcodeDetectionOptions Options(BarcodeSearchArea? area) => new()
    {
        DetectBarcodes = true,
        Symbologies = [BarcodeSymbology.Code128],
        SearchArea = area
    };

    /// <summary>
    /// A clean A4 page at 300 dpi with one barcode in the top quarter and one in the bottom quarter.
    /// </summary>
    private IMemoryImage CreatePage()
    {
        var black = new bool[PageW, PageH];
        DrawBarcode(black, HeaderCode, 620, 250, 1000, 200);
        DrawBarcode(black, FooterCode, 620, 2900, 1000, 200);

        var image = ImageContext.Create(PageW, PageH, ImagePixelFormat.RGB24);
        Render(image, black);
        return image;
    }

    private static void DrawBarcode(bool[,] black, string text, int x0, int y0, int width, int height)
    {
        BitMatrix matrix = new Code128Writer().encode(text, BarcodeFormat.CODE_128, width, height);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                if (matrix[x, y] && x0 + x < black.GetLength(0) && y0 + y < black.GetLength(1))
                {
                    black[x0 + x, y0 + y] = true;
                }
            }
        }
    }

    private static void Render(IMemoryImage image, bool[,] black)
    {
        using var lockState = image.Lock(LockMode.WriteOnly, out var data);
        unsafe
        {
            for (var y = 0; y < PageH; y++)
            {
                var row = data.ptr + (data.invertY ? PageH - 1 - y : y) * data.stride;
                for (var x = 0; x < PageW; x++)
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
