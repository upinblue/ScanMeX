using NAPS2.Images;
using NAPS2.Scan;
using Xunit;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace NAPS2.Sdk.Tests.Images;

/// <summary>
/// A scanned form is not a clean page: it carries ruled tables, dense small print and sensor noise, and
/// ZXing decodes some of that as barcodes. Restricting the search to the symbologies the paperwork
/// actually carries is what keeps those out.
/// </summary>
/// <remarks>
/// Measured on a customer certificate carrying exactly two Code 128 codes, rendered at 300 dpi with
/// scanner noise and a fraction of a degree of skew: restricted to Code 39 + Code 128 every variant
/// yielded those two and nothing else, while with no restriction the same variants yielded three to five
/// -- the extras being EAN-8, UPC-E and ITF reads of the table rules, one of which became the page's
/// primary barcode. That document is a customer file and isn't in the repo, so the page here is built to
/// have the same shape, and it reproduces the effect: unrestricted, the seeds below yield phantom
/// CODABAR and UPC_E values, and they sort ahead of the real codes, which is what makes one of them the
/// page's primary. The unrestricted result is deliberately not asserted -- it is a property of ZXing's
/// heuristics rather than of our code, and pinning it would make a library update look like a
/// regression. What has to hold is the restricted direction.
/// </remarks>
public class PhantomBarcodeTests : ContextualTests
{
    private const int PageW = 2480;
    private const int PageH = 3508;

    private const string LotCode = "010000398340";
    private const string SampleCode = "000100937929";

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(23)]
    [InlineData(1337)]
    public void ANoisyFormYieldsOnlyItsRealBarcodes(int noiseSeed)
    {
        using var image = CreateNoisyForm(noiseSeed);

        var values = BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.Code39, BarcodeSymbology.Code128]
        }).GetAllValues();

        Assert.Equal([LotCode, SampleCode], values.Select(x => x.Text));
        Assert.All(values, x => Assert.Equal("CODE_128", x.Format));
    }

    /// <summary>
    /// The primary is what a profile without a regex files the document under, so a phantom becoming the
    /// primary is the version of this bug that reaches the archive.
    /// </summary>
    [Fact]
    public void ThePrimaryOfANoisyFormIsARealBarcode()
    {
        using var image = CreateNoisyForm(7);

        var barcode = BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.Code39, BarcodeSymbology.Code128]
        });

        Assert.Equal(LotCode, barcode.DetectedText);
    }

    /// <summary>
    /// Lowering the barcode strictness buys a damaged Code 39 back; it must not buy the ruled tables and
    /// dense print of a form back with it. This is the same page and the same seeds as above, read at
    /// every strictness level: the answer has to stay the two codes that were printed on it.
    /// </summary>
    /// <remarks>
    /// This is the guard that actually earns its keep. A tolerant reader that only required a start guard
    /// and a run of decodable characters reads five phantom values out of the customer's own eight-page
    /// document -- "ZZ", "$", "Z", "M" and "%/%$/" -- on pages that carry no barcode at all. Requiring the
    /// terminating group to be character-shaped and followed by a quiet zone, plus a minimum length and
    /// confirmation across scan lines, takes all five out while still recovering the two real values.
    /// </remarks>
    [Theory]
    [InlineData(1, BarcodeStrictness.Tolerant)]
    [InlineData(7, BarcodeStrictness.Tolerant)]
    [InlineData(23, BarcodeStrictness.Tolerant)]
    [InlineData(1337, BarcodeStrictness.Tolerant)]
    [InlineData(1, BarcodeStrictness.VeryTolerant)]
    [InlineData(7, BarcodeStrictness.VeryTolerant)]
    [InlineData(23, BarcodeStrictness.VeryTolerant)]
    [InlineData(1337, BarcodeStrictness.VeryTolerant)]
    public void LoweringTheStrictnessDoesNotInventBarcodesOnANoisyForm(
        int noiseSeed, BarcodeStrictness strictness)
    {
        using var image = CreateNoisyForm(noiseSeed);

        var values = BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.Code39, BarcodeSymbology.Code128],
            Strictness = strictness
        }).GetAllValues();

        Assert.Equal([LotCode, SampleCode], values.Select(x => x.Text));
        Assert.All(values, x => Assert.False(x.IsRecovered));
    }

    /// <summary>
    /// A form-shaped page: the two real barcodes low on the sheet, a block of ruled table lines and rows
    /// of small dark marks standing in for dense print, plus per-pixel noise.
    /// </summary>
    private IMemoryImage CreateNoisyForm(int noiseSeed)
    {
        var black = new bool[PageW, PageH];

        DrawBarcode(black, LotCode, 1500, 1850, 900, 150);
        DrawBarcode(black, SampleCode, 1500, 2100, 900, 150);

        // Ruled table: long horizontal lines with vertical separators, the structure the phantom EAN/UPC
        // and ITF reads came out of.
        for (var row = 0; row < 14; row++)
        {
            var y = 700 + row * 55;
            for (var x = 200; x < 2280; x++)
            {
                black[x, y] = true;
            }
        }
        for (var col = 0; col < 8; col++)
        {
            var x = 200 + col * 260;
            for (var y = 700; y < 700 + 13 * 55; y++)
            {
                black[x, y] = true;
            }
        }

        // Dense small print: short runs of dark pixels at varying widths.
        var rand = new Random(noiseSeed);
        for (var row = 0; row < 26; row++)
        {
            var y = 720 + row * 55;
            if (y >= PageH - 4) break;
            var x = 220;
            while (x < 2260)
            {
                var runWidth = rand.Next(2, 9);
                for (var dx = 0; dx < runWidth && x + dx < PageW; dx++)
                {
                    for (var dy = 0; dy < 22 && y + dy < PageH; dy++)
                    {
                        black[x + dx, y + dy] = true;
                    }
                }
                x += runWidth + rand.Next(2, 11);
            }
        }

        var image = ImageContext.Create(PageW, PageH, ImagePixelFormat.RGB24);
        Render(image, black, noiseSeed);
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

    private static void Render(IMemoryImage image, bool[,] black, int noiseSeed)
    {
        // Seeded separately from the layout so the same page can be re-rendered with different noise.
        var rand = new Random(noiseSeed * 31 + 17);
        using var lockState = image.Lock(LockMode.WriteOnly, out var data);
        unsafe
        {
            for (var y = 0; y < PageH; y++)
            {
                var row = data.ptr + (data.invertY ? PageH - 1 - y : y) * data.stride;
                for (var x = 0; x < PageW; x++)
                {
                    var p = row + x * data.bytesPerPixel;
                    var level = black[x, y] ? 0 : 255;
                    level = Math.Clamp(level + rand.Next(-38, 39), 0, 255);
                    var value = (byte) level;
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
