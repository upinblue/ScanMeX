namespace NAPS2.Images;

/// <summary>
/// A separable bicubic (Catmull-Rom) resampler over interleaved 8-bit samples.
/// </summary>
/// <remarks>
/// This exists because GDI+ serializes every interpolated <c>Graphics.DrawImage</c> for the whole
/// process. Measured on an i5-14400 with each task holding its own source and destination bitmap: eight
/// threads resize no faster than one, at every size down to 128x128 -- 48 KB, which fits in L1, so a
/// memory bandwidth limit cannot explain it. That makes every resize in the scan chain a global
/// bottleneck no amount of page-level parallelism can get past, and two of them sit in that chain: the
/// barcode pass's downscaled copy of the page, and the thumbnail.
///
/// The kernel radius is widened by 1/scale when downsampling, which is the part that matters: it turns
/// the filter into a low-pass over exactly the source pixels an output pixel covers, which is what makes
/// print noise between the bars average away. A plain box average is not a substitute -- measured over
/// the customer's own paperwork, it loses barcodes that GDI+ reads, on pages where the downscaled pass is
/// the only one that reads them at all. This filter reads the same merged, ordered barcode list as GDI+
/// on all 35 of those pages.
/// </remarks>
internal static class BicubicResampler
{
    /// <summary>
    /// The Catmull-Rom radius, in destination pixels. Widened by 1/scale when downsampling.
    /// </summary>
    private const double SUPPORT = 2.0;

    /// <summary>
    /// Output rows are resampled in contiguous bands so the work spreads over cores without the
    /// intermediate growing with the page: a band holds only the source rows its own output rows reach
    /// back to. A plain two-pass version needs 83 MB of intermediate for a 600 dpi page, which with
    /// several pages in flight trades one bottleneck for another.
    /// </summary>
    private const int MIN_ROWS_PER_BAND = 16;

    /// <summary>Catmull-Rom, the usual a = -0.5 bicubic.</summary>
    private static double Kernel(double x)
    {
        x = Math.Abs(x);
        if (x < 1)
        {
            return 1.5 * x * x * x - 2.5 * x * x + 1;
        }
        if (x < 2)
        {
            return -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2;
        }
        return 0;
    }

    /// <summary>
    /// The filter taps for every output coordinate along one axis. <see cref="Start"/> is unclamped and
    /// may be negative or past the end; reading clamps to the edge, so a barcode in the outermost pixels
    /// of a page is resampled against the edge rather than against nothing.
    /// </summary>
    private sealed class Weights
    {
        public int[] Start { get; init; } = [];
        public int[] Count { get; init; } = [];
        public float[] Taps { get; init; } = [];
        public int Stride { get; init; }
    }

    private static Weights Build(int srcSize, int dstSize)
    {
        var scale = (double) dstSize / srcSize;
        var support = scale < 1 ? SUPPORT / scale : SUPPORT;
        var stride = (int) Math.Ceiling(support * 2) + 2;
        var start = new int[dstSize];
        var count = new int[dstSize];
        var taps = new float[(long) dstSize * stride];
        for (var i = 0; i < dstSize; i++)
        {
            // The centre of output pixel i, in source coordinates.
            var center = (i + 0.5) / scale - 0.5;
            var left = (int) Math.Ceiling(center - support);
            var right = (int) Math.Floor(center + support);
            var n = 0;
            var sum = 0.0;
            var tapBase = (long) i * stride;
            for (var j = left; j <= right && n < stride; j++)
            {
                var weight = Kernel(scale < 1 ? (j - center) * scale : j - center);
                taps[tapBase + n] = (float) weight;
                sum += weight;
                n++;
            }
            start[i] = left;
            count[i] = n;
            // The taps have to sum to one or the image changes brightness. They only fail to when the
            // kernel is sampled asymmetrically, which is the usual case for a non-integer scale.
            if (sum != 0)
            {
                for (var k = 0; k < n; k++)
                {
                    taps[tapBase + k] /= (float) sum;
                }
            }
        }
        return new Weights { Start = start, Count = count, Taps = taps, Stride = stride };
    }

    /// <summary>
    /// Resamples interleaved 8-bit samples. <paramref name="channels"/> is 1 for grayscale, 3 for RGB or
    /// 4 for RGBA; every channel is filtered alike, alpha included, so this must not be handed
    /// premultiplied data.
    /// </summary>
    public static byte[] Resample(byte[] src, int srcW, int srcH, int channels, int dstW, int dstH)
    {
        if (srcW <= 0 || srcH <= 0 || dstW <= 0 || dstH <= 0)
        {
            throw new ArgumentException("Resample dimensions must be positive");
        }
        if (channels is < 1 or > 4)
        {
            throw new ArgumentException($"Unsupported channel count: {channels}");
        }
        var needed = (long) srcW * srcH * channels;
        if (src.Length < needed)
        {
            throw new ArgumentException(
                $"Source is {src.Length} bytes, expected {needed} for {srcW}x{srcH}x{channels}");
        }

        var hw = Build(srcW, dstW);
        var vw = Build(srcH, dstH);
        var dst = new byte[(long) dstW * dstH * channels];
        var dstRowLen = dstW * channels;

        var bands = Math.Max(1, Math.Min(Environment.ProcessorCount, dstH / MIN_ROWS_PER_BAND));
        var rowsPerBand = (dstH + bands - 1) / bands;

        Parallel.For(0, bands, band =>
        {
            var yStart = band * rowsPerBand;
            var yEnd = Math.Min(yStart + rowsPerBand, dstH);
            if (yStart >= yEnd)
            {
                return;
            }
            // A ring of horizontally resampled source rows, just deep enough for the vertical taps. Two
            // neighbouring bands each resample the few rows they overlap on, which is cheaper than
            // coordinating a shared cache would be.
            var ringDepth = vw.Stride;
            var ring = new float[ringDepth][];
            var ringRow = new int[ringDepth];
            for (var i = 0; i < ringDepth; i++)
            {
                ring[i] = new float[dstRowLen];
                ringRow[i] = int.MinValue;
            }
            // The kernel's negative lobes take intermediate sums below zero and above 255, so a row is
            // summed in full precision and rounded exactly once, at the end. Rounding per tap would
            // clip away the overshoot the filter depends on.
            var acc = new float[dstRowLen];

            for (var y = yStart; y < yEnd; y++)
            {
                Array.Clear(acc, 0, dstRowLen);
                var tapBase = (long) y * vw.Stride;
                var vStart = vw.Start[y];
                var vCount = vw.Count[y];

                for (var k = 0; k < vCount; k++)
                {
                    var weight = vw.Taps[tapBase + k];
                    if (weight == 0)
                    {
                        continue;
                    }
                    var sy = Clamp(vStart + k, srcH - 1);
                    var row = GetRow(ring, ringRow, sy, src, srcW, channels, hw, dstW);
                    for (var i = 0; i < dstRowLen; i++)
                    {
                        acc[i] += row[i] * weight;
                    }
                }

                var dstOffset = (long) y * dstRowLen;
                for (var i = 0; i < dstRowLen; i++)
                {
                    var value = acc[i] + 0.5f;
                    dst[dstOffset + i] = (byte) (value < 0 ? 0 : value > 255 ? 255 : value);
                }
            }
        });
        return dst;
    }

    /// <summary>
    /// The given source row, resampled horizontally, from the band's ring if it is already there. A ring
    /// slot is addressed by the source row number so a row is resampled once per band however many
    /// output rows reach back to it.
    /// </summary>
    private static float[] GetRow(float[][] ring, int[] ringRow, int sy, byte[] src, int srcW, int channels,
        Weights hw, int dstW)
    {
        var slot = sy % ring.Length;
        if (ringRow[slot] == sy)
        {
            return ring[slot];
        }
        var row = ring[slot];
        var srcOffset = (long) sy * srcW * channels;
        for (var x = 0; x < dstW; x++)
        {
            var tapBase = (long) x * hw.Stride;
            var hStart = hw.Start[x];
            var hCount = hw.Count[x];
            var outBase = x * channels;
            for (var c = 0; c < channels; c++)
            {
                var sum = 0f;
                for (var k = 0; k < hCount; k++)
                {
                    var sx = Clamp(hStart + k, srcW - 1);
                    sum += hw.Taps[tapBase + k] * src[srcOffset + (long) sx * channels + c];
                }
                row[outBase + c] = sum;
            }
        }
        ringRow[slot] = sy;
        return row;
    }

    private static int Clamp(int value, int max) => value < 0 ? 0 : value > max ? max : value;
}
