using NAPS2.Images;
using NAPS2.Scan;
using Xunit;

namespace NAPS2.Sdk.Tests.Images;

/// <summary>
/// A Code 39 barcode whose stop guard is printed wrong. ZXing discards the whole symbol however clean
/// its data characters are, so a profile can be set to accept one anyway -- and these pin both halves of
/// that: what the lowered strictness recovers, and what it still refuses.
/// </summary>
/// <remarks>
/// The damage here is measured, not invented. On a customer's process-order cover sheets the data
/// characters and the start guard decode perfectly while in the stop guard the edge between the fourth
/// and fifth element sits about 1.5 modules too far right: the space comes out 2.5 modules wide instead
/// of 1 and the bar 1.5 instead of 3, with the character's total width unchanged. The same defect sits on
/// every sheet from that source. Those documents are customer files and aren't in the repo, so the pages
/// here are drawn with that exact displacement, and they reproduce the effect -- strict detection reads
/// nothing off them, which is the failure the customer reported.
/// </remarks>
public class DamagedCode39Tests : ContextualTests
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    private static readonly int[] CharacterEncodings =
    [
        0x034, 0x121, 0x061, 0x160, 0x031, 0x130, 0x070, 0x025, 0x124, 0x064,
        0x109, 0x049, 0x148, 0x019, 0x118, 0x058, 0x00D, 0x10C, 0x04C, 0x01C,
        0x103, 0x043, 0x142, 0x013, 0x112, 0x052, 0x007, 0x106, 0x046, 0x016,
        0x181, 0x0C1, 0x1C0, 0x091, 0x190, 0x0D0, 0x085, 0x184, 0x0C4, 0x0A8,
        0x0A2, 0x08A, 0x02A
    ];

    private const int AsteriskEncoding = 0x094;

    private const int Module = 6;
    private const int Narrow = Module;
    private const int Wide = Module * 3;

    private const int PageW = 2480;
    private const int PageH = 3508;

    /// <summary>The value off the customer's page 1, space included -- it really is part of the code.</summary>
    private const string OrderCode = "KO- 3100034339";

    [Fact]
    public void StrictDetectionReadsNothingOffADamagedStopGuard()
    {
        using var image = CreatePage(OrderCode, damageStopGuard: true);

        var barcode = Detect(image, BarcodeStrictness.Strict);

        Assert.Empty(barcode.GetAllValues());
        Assert.Null(barcode.DetectedText);
    }

    [Fact]
    public void TolerantDetectionRecoversADamagedStopGuard()
    {
        using var image = CreatePage(OrderCode, damageStopGuard: true);

        var barcode = Detect(image, BarcodeStrictness.Tolerant);

        var value = Assert.Single(barcode.GetAllValues());
        Assert.Equal(OrderCode, value.Text);
        Assert.Equal("CODE_39", value.Format);
        Assert.True(value.IsRecovered);
        // A recovered value has to reach the rest of the pipeline like any other, or lowering the
        // strictness would fill the console and change nothing about the document.
        Assert.Equal(OrderCode, barcode.DetectedText);
    }

    /// <summary>
    /// The flag says the paper is damaged, so it must not be set for a barcode that simply decoded. It is
    /// what the console warns on, and a warning on every scan is a warning nobody reads.
    /// </summary>
    [Fact]
    public void AnIntactBarcodeIsNotReportedAsRecovered()
    {
        using var image = CreatePage(OrderCode, damageStopGuard: false);

        foreach (var strictness in new[]
                     { BarcodeStrictness.Strict, BarcodeStrictness.Tolerant, BarcodeStrictness.VeryTolerant })
        {
            var value = Assert.Single(Detect(image, strictness).GetAllValues());
            Assert.Equal(OrderCode, value.Text);
            Assert.False(value.IsRecovered);
        }
    }

    /// <summary>
    /// The length guard is most of what separates a damaged barcode from a run of print that happens to
    /// classify as characters, so the two lowered levels have to actually differ on it.
    /// </summary>
    [Fact]
    public void AShortValueNeedsTheLowestStrictness()
    {
        using var image = CreatePage("AB12", damageStopGuard: true);

        Assert.Empty(Detect(image, BarcodeStrictness.Strict).GetAllValues());
        Assert.Empty(Detect(image, BarcodeStrictness.Tolerant).GetAllValues());

        var value = Assert.Single(Detect(image, BarcodeStrictness.VeryTolerant).GetAllValues());
        Assert.Equal("AB12", value.Text);
        Assert.True(value.IsRecovered);
    }

    /// <summary>
    /// Code 39 is the only symbology the tolerant pass touches. Code 128, EAN and UPC all carry a check
    /// character, so accepting a damaged one would mean overruling the code's own statement that it was
    /// misread -- a different proposition entirely from a Code 39, which has no checksum to overrule.
    /// </summary>
    [Fact]
    public void ADamagedBarcodeIsNotRecoveredForAProfileThatDoesNotAskForCode39()
    {
        using var image = CreatePage(OrderCode, damageStopGuard: true);

        var barcode = BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.Code128],
            Strictness = BarcodeStrictness.VeryTolerant
        });

        Assert.Empty(barcode.GetAllValues());
    }

    /// <summary>
    /// Patch-T sheets are reusable blank cards carrying a fixed word, so a damaged one is replaced rather
    /// than decoded harder -- and accepting a damaged one would split documents in the wrong place.
    /// </summary>
    [Fact]
    public void PatchTIsNeverRecovered()
    {
        using var image = CreatePage("PATCHT", damageStopGuard: true);

        var barcode = BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.PatchT],
            Strictness = BarcodeStrictness.VeryTolerant
        });

        Assert.Empty(barcode.GetAllValues());
        Assert.False(barcode.IsPatchT);
    }

    /// <summary>
    /// The barcode is drawn well down a page that is otherwise blank, so a reader that only samples rows
    /// near the middle would miss it. The customer's barcode sits in the top eighth of the sheet.
    /// </summary>
    [Fact]
    public void ADamagedBarcodeIsFoundHighOnThePage()
    {
        using var image = CreatePage(OrderCode, damageStopGuard: true, y: 220);

        var value = Assert.Single(Detect(image, BarcodeStrictness.Tolerant).GetAllValues());

        Assert.Equal(OrderCode, value.Text);
    }

    private static Barcode Detect(IMemoryImage image, BarcodeStrictness strictness) =>
        BarcodeDetector.Detect(image, new BarcodeDetectionOptions
        {
            DetectBarcodes = true,
            Symbologies = [BarcodeSymbology.Code39],
            Strictness = strictness
        });

    /// <summary>
    /// Draws <paramref name="text"/> as Code 39 at a fixed module width. When
    /// <paramref name="damageStopGuard"/> is set, the stop guard's fourth and fifth elements are
    /// redistributed as 2.5 and 1.5 modules instead of 1 and 3, leaving the character the same total
    /// width -- the customer's defect exactly.
    /// </summary>
    private IMemoryImage CreatePage(string text, bool damageStopGuard, int x = 200, int y = 1400)
    {
        var elements = BuildElements(text, damageStopGuard);
        var image = ImageContext.Create(PageW, PageH, ImagePixelFormat.RGB24);
        var black = new bool[PageW, PageH];
        var cursor = x;
        foreach (var (isBar, width) in elements)
        {
            if (isBar)
            {
                for (var bx = cursor; bx < cursor + width && bx < PageW; bx++)
                {
                    for (var by = y; by < y + 400 && by < PageH; by++)
                    {
                        black[bx, by] = true;
                    }
                }
            }
            cursor += width;
        }
        Assert.True(cursor < PageW, "The drawn barcode has to fit on the page with a quiet zone to spare.");
        Fill(image, black);
        return image;
    }

    private static List<(bool IsBar, int Width)> BuildElements(string text, bool damageStopGuard)
    {
        var elements = new List<(bool, int)>();
        var characters = new List<int> { AsteriskEncoding };
        characters.AddRange(text.Select(c => CharacterEncodings[Alphabet.IndexOf(c)]));
        characters.Add(AsteriskEncoding);

        for (var i = 0; i < characters.Count; i++)
        {
            var isStopGuard = i == characters.Count - 1;
            for (var e = 0; e < 9; e++)
            {
                var isWide = (characters[i] & (1 << (8 - e))) != 0;
                var width = isWide ? Wide : Narrow;
                if (isStopGuard && damageStopGuard && e == 3)
                {
                    width = Module * 5 / 2; // the narrow space, printed 2.5 modules wide
                }
                else if (isStopGuard && damageStopGuard && e == 4)
                {
                    width = Module * 3 / 2; // the wide bar, left with 1.5 modules
                }
                elements.Add((e % 2 == 0, width));
            }
            if (i < characters.Count - 1)
            {
                elements.Add((false, Narrow));
            }
        }
        return elements;
    }

    private static void Fill(IMemoryImage image, bool[,] black)
    {
        using var lockState = image.Lock(LockMode.WriteOnly, out var data);
        unsafe
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = data.ptr + (data.invertY ? image.Height - 1 - y : y) * data.stride;
                for (var x = 0; x < image.Width; x++)
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
