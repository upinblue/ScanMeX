using NAPS2.Images;
using Xunit;

namespace NAPS2.Sdk.Tests.Images;

/// <summary>
/// The resampler that replaces GDI+ in the barcode pass and the thumbnail. What it has to get right is
/// not prettiness but the two things the callers depend on: it must not shift brightness (the taps have
/// to sum to one, or a page slowly darkens every time it is resized), and the banding it uses to spread
/// the work over cores must not change a single output byte.
/// </summary>
public class BicubicResamplerTests
{
    /// <summary>
    /// The banded, ring-buffered implementation against a plainly written one. This is the test that
    /// earns its keep: the ring addresses its slots by source row number and two bands overlap on the
    /// rows between them, so an off-by-one there produces a picture that still looks like the page and is
    /// wrong in a band-shaped stripe. A page tall enough to span several bands is the case that catches
    /// it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void BandingDoesNotChangeTheResult(int channels)
    {
        const int srcW = 137;
        const int srcH = 521;
        const int dstW = 82;
        const int dstH = 313;
        var src = Noise(srcW, srcH, channels, seed: 42);

        var actual = BicubicResampler.Resample(src, srcW, srcH, channels, dstW, dstH);
        var expected = ReferenceResample(src, srcW, srcH, channels, dstW, dstH);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// A flat image has to come out flat and at the same level. If the taps do not sum to one this is
    /// the test that fails, and it is the failure that matters most: every resize would shift the page's
    /// brightness, which for a scan means the binarizer sees something different than it used to.
    /// </summary>
    [Theory]
    [InlineData(200, 300, 100, 150)]
    [InlineData(300, 200, 1000, 700)]
    [InlineData(64, 64, 63, 63)]
    public void AFlatImageStaysFlat(int srcW, int srcH, int dstW, int dstH)
    {
        var src = new byte[srcW * srcH];
        Array.Fill(src, (byte) 173);

        var dst = BicubicResampler.Resample(src, srcW, srcH, 1, dstW, dstH);

        Assert.Equal(dstW * dstH, dst.Length);
        Assert.All(dst, x => Assert.Equal(173, x));
    }

    /// <summary>
    /// The kernel has negative lobes, so an edge overshoots past black and white. Accumulating in bytes
    /// would clip that away tap by tap; the result has to stay in range but only because it was clamped
    /// once, at the end.
    /// </summary>
    [Fact]
    public void AHardEdgeStaysInRangeAndKeepsItsSides()
    {
        const int srcW = 200;
        const int srcH = 8;
        var src = new byte[srcW * srcH];
        for (var y = 0; y < srcH; y++)
        for (var x = 0; x < srcW; x++)
        {
            src[y * srcW + x] = (byte) (x < srcW / 2 ? 0 : 255);
        }

        var dst = BicubicResampler.Resample(src, srcW, srcH, 1, 100, 4);

        // Far from the edge the two sides survive intact; near it the values are somewhere in between,
        // which is the whole point of resampling. Nothing may land outside 0..255 -- a byte cannot say
        // so, but a wrapped value would show up as white pixels in the black half.
        Assert.Equal(0, dst[0]);
        Assert.Equal(255, dst[99]);
        Assert.All(dst[..45], x => Assert.Equal(0, x));
        Assert.All(dst[55..100], x => Assert.Equal(255, x));
    }

    /// <summary>
    /// Channels are filtered independently. A bug that mixes them would tint the whole image, which on a
    /// grayscale scan is invisible and on a colour one is not.
    /// </summary>
    [Fact]
    public void ChannelsDoNotBleedIntoEachOther()
    {
        const int srcW = 40;
        const int srcH = 40;
        var src = new byte[srcW * srcH * 3];
        for (var i = 0; i < srcW * srcH; i++)
        {
            src[i * 3] = 10;
            src[i * 3 + 1] = 120;
            src[i * 3 + 2] = 240;
        }

        var dst = BicubicResampler.Resample(src, srcW, srcH, 3, 17, 17);

        for (var i = 0; i < 17 * 17; i++)
        {
            Assert.Equal(10, dst[i * 3]);
            Assert.Equal(120, dst[i * 3 + 1]);
            Assert.Equal(240, dst[i * 3 + 2]);
        }
    }

    /// <summary>
    /// A source one pixel across has nothing to interpolate between, and the taps that reach past its
    /// edge have to clamp to it rather than read zero -- otherwise the edges of every page darken.
    /// </summary>
    [Fact]
    public void ASinglePixelSourceClampsToItself()
    {
        var dst = BicubicResampler.Resample([200], 1, 1, 1, 5, 5);

        Assert.Equal(25, dst.Length);
        Assert.All(dst, x => Assert.Equal(200, x));
    }

    [Fact]
    public void RejectsInputItCannotResample()
    {
        Assert.Throws<ArgumentException>(() => BicubicResampler.Resample([1], 1, 1, 1, 0, 5));
        Assert.Throws<ArgumentException>(() => BicubicResampler.Resample([1], 1, 1, 5, 5, 5));
        Assert.Throws<ArgumentException>(() => BicubicResampler.Resample([1], 4, 4, 1, 2, 2));
    }

    private static byte[] Noise(int w, int h, int channels, int seed)
    {
        var data = new byte[w * h * channels];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// The same filter written the obvious way: build the whole horizontally resampled image, then
    /// filter it vertically. No banding, no ring, no reuse.
    /// </summary>
    private static byte[] ReferenceResample(byte[] src, int srcW, int srcH, int channels, int dstW, int dstH)
    {
        var mid = new float[(long) dstW * srcH * channels];
        var hw = BuildWeights(srcW, dstW);
        for (var y = 0; y < srcH; y++)
        for (var x = 0; x < dstW; x++)
        for (var c = 0; c < channels; c++)
        {
            var sum = 0f;
            for (var k = 0; k < hw[x].Length; k++)
            {
                var sx = Math.Min(Math.Max(hw[x][k].Index, 0), srcW - 1);
                sum += hw[x][k].Weight * src[((long) y * srcW + sx) * channels + c];
            }
            mid[((long) y * dstW + x) * channels + c] = sum;
        }

        var dst = new byte[(long) dstW * dstH * channels];
        var vw = BuildWeights(srcH, dstH);
        for (var y = 0; y < dstH; y++)
        for (var x = 0; x < dstW; x++)
        for (var c = 0; c < channels; c++)
        {
            var sum = 0f;
            for (var k = 0; k < vw[y].Length; k++)
            {
                var sy = Math.Min(Math.Max(vw[y][k].Index, 0), srcH - 1);
                sum += vw[y][k].Weight * mid[((long) sy * dstW + x) * channels + c];
            }
            var value = sum + 0.5f;
            dst[((long) y * dstW + x) * channels + c] = (byte) (value < 0 ? 0 : value > 255 ? 255 : value);
        }
        return dst;
    }

    private record Tap(int Index, float Weight);

    private static Tap[][] BuildWeights(int srcSize, int dstSize)
    {
        var scale = (double) dstSize / srcSize;
        var support = scale < 1 ? 2.0 / scale : 2.0;
        var stride = (int) Math.Ceiling(support * 2) + 2;
        var result = new Tap[dstSize][];
        for (var i = 0; i < dstSize; i++)
        {
            var center = (i + 0.5) / scale - 0.5;
            var left = (int) Math.Ceiling(center - support);
            var right = (int) Math.Floor(center + support);
            var taps = new List<Tap>();
            var sum = 0.0;
            for (var j = left; j <= right && taps.Count < stride; j++)
            {
                var weight = Kernel(scale < 1 ? (j - center) * scale : j - center);
                taps.Add(new Tap(j, (float) weight));
                sum += weight;
            }
            result[i] = sum == 0
                ? taps.ToArray()
                : taps.Select(t => t with { Weight = t.Weight / (float) sum }).ToArray();
        }
        return result;
    }

    private static double Kernel(double x)
    {
        x = Math.Abs(x);
        if (x < 1) return 1.5 * x * x * x - 2.5 * x * x + 1;
        if (x < 2) return -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2;
        return 0;
    }
}
