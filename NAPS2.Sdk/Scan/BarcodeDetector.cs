using NAPS2.Images.Bitwise;
using ZXing;
using ZXing.Common;

namespace NAPS2.Scan;

/// <summary>
/// A wrapper around the ZXing library that detects patch-t and other barcodes.
/// http://www.alliancegroup.co.uk/patch-codes.htm
/// </summary>
internal static class BarcodeDetector
{
    private static readonly BarcodeFormat PATCH_T_FORMAT = BarcodeFormat.CODE_39;

    /// <summary>
    /// The page is decoded a second time at this fraction of its size. Print noise between the bars of a
    /// 300 dpi scan can defeat the local binarizer at full resolution while averaging away here; a real
    /// invoice in the customer's samples decodes only on the smaller copy. Shrinking too far would lose
    /// narrow bars instead, so this stays close to full size.
    /// </summary>
    private const double RETRY_SCALE = 0.6;

    /// <summary>
    /// Below this width the page is already small enough that a downscaled retry would drop bars rather
    /// than noise, so the second pass is skipped.
    /// </summary>
    private const int MIN_WIDTH_FOR_RETRY = 1200;

    public static Barcode Detect(IMemoryImage image, BarcodeDetectionOptions options)
    {
        // TODO: Probably shouldn't have DetectBarcodes be in the options class? The call shouldn't happen at all.
        if (!options.DetectBarcodes)
        {
            return Barcode.NoDetection;
        }

        // A profile that says where on the paper its barcode is gets the rest of the sheet ignored. The
        // crop is what the passes below run on, so nothing outside it can decode -- which is the point:
        // ruled tables and dense print elsewhere on the page can't invent a value any more. The copy is
        // disposed here rather than by the passes, which have to be able to run on the caller's page too.
        var area = options.SearchArea?.Normalized();
        if (area == null || area.IsWholePage)
        {
            return DetectIn(image, options);
        }
        using var cropped = CropToSearchArea(image, area);
        // A crop we couldn't make falls back to the whole page. Reporting nothing at all instead would be
        // the silent failure this restriction exists to avoid, only worse -- it would look like paper
        // with no barcode on it.
        return DetectIn(cropped ?? image, options);
    }

    /// <summary>
    /// Copies the search area out of the page. Nothing is read back from the copy except barcodes, so it
    /// carries no transforms and is disposed as soon as the passes are done with it.
    /// </summary>
    /// <remarks>
    /// Built directly rather than as <c>image.Copy().PerformTransform(new CropTransform(...))</c>: that
    /// route allocates a full-size copy of a 300 dpi page in order to throw most of it away, and this
    /// runs on every page of every scan.
    /// </remarks>
    private static IMemoryImage? CropToSearchArea(IMemoryImage image, BarcodeSearchArea area)
    {
        var (x, y, width, height) = area.ToPixels(image.Width, image.Height);
        if (width <= 0 || height <= 0 || (width == image.Width && height == image.Height))
        {
            return null;
        }
        IMemoryImage? cropped = null;
        try
        {
            cropped = image.ImageContext.Create(width, height, image.PixelFormat);
            cropped.SetResolution(image.HorizontalResolution, image.VerticalResolution);
            new CopyBitwiseImageOp
            {
                SourceXOffset = x,
                SourceYOffset = y,
                Columns = width,
                Rows = height
            }.Perform(image, cropped);
            return cropped;
        }
        catch (Exception)
        {
            cropped?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// The decoding passes themselves, run either on the page or on the part of it the profile restricted
    /// the search to.
    /// </summary>
    /// <remarks>
    /// Positions are relative to whatever is passed in, which holds for a crop as well: every barcode
    /// found came out of the same crop, so their order in it is their order on the page, and the primary
    /// is still the topmost-leftmost one of them.
    /// </remarks>
    private static Barcode DetectIn(IMemoryImage image, BarcodeDetectionOptions options)
    {
        var reader = new BarcodeReader<LuminanceSource>(x => x)
        {
            Options = options.ZXingOptions ?? new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = GetPossibleFormats(options)
            }
        };

        // The page as ZXing wants it: one byte of luminance per pixel. Built once and handed to every
        // pass -- it used to be built again for the tolerant pass, and a third time out of the
        // downscaled copy, which is the same walk over the same 26 MB each time.
        var source = GrayLuminanceSource.FromImage(image);

        // A page may carry several barcodes (e.g. an order and an article code), so we keep all of them
        // and let the profile's symbology selection decide which one is the primary.
        //
        // Neither pass is reliably the better one: the full-resolution pass reads narrow bars the smaller
        // copy blurs away, and the smaller copy reads codes the full-resolution binarizer gives up on. So
        // both run and the results are merged, rather than one being a fallback for the other. Positions
        // from the smaller copy are scaled back up so the merged list is still in page reading order --
        // the primary is whatever comes first in it, so a wrong order picks a wrong barcode.
        var found = DecodeAll(reader, source, 1);
        found.AddRange(DecodeDownscaled(reader, source));
        found.AddRange(DecodeDamagedCode39(source, options));

        // Two passes over the same page report the same barcode twice, and the tolerant pass reports one
        // ZXing already read. Collapse by value, keeping the topmost-leftmost position so the merged list
        // is still in page reading order, and preferring the copy that decoded in full -- a value only
        // counts as recovered when nothing managed to read it properly.
        var all = found
            .OrderBy(x => x.Y)
            .ThenBy(x => x.X)
            .GroupBy(x => (x.Text, x.Format))
            .Select(g => new BarcodeValue(g.Key.Text, g.Key.Format, g.All(x => x.IsRecovered)))
            .ToList();

        var primary = PickPrimary(all, options);
        return new Barcode(true, primary != null, primary?.Text, primary?.Format)
        {
            AllDetections = all
        };
    }

    private record Detection(string? Text, string? Format, float Y, float X, bool IsRecovered = false);

    /// <summary>
    /// The tolerant Code 39 pass, which only runs when the profile lowered its strictness and asked for
    /// Code 39. Its results are merged into the page's list rather than used as a fallback: a page can
    /// carry a readable Code 128 next to a Code 39 whose stop guard is damaged, and a fallback that only
    /// ran when the page yielded nothing would drop the second one without saying so.
    /// </summary>
    private static List<Detection> DecodeDamagedCode39(LuminanceSource source, BarcodeDetectionOptions options)
    {
        var tolerance = Code39Tolerance.For(options.Strictness);
        // Patch-T is deliberately excluded even though it rides on Code 39. A patch-T sheet is a reusable
        // blank card carrying a fixed word, so a damaged one is replaced, not decoded harder -- and
        // accepting a damaged one would separate documents in the wrong place.
        if (tolerance == null || !options.Symbologies.Contains(BarcodeSymbology.Code39))
        {
            return [];
        }
        try
        {
            return DamagedCode39Reader
                .Read(source, tolerance)
                .Select(x => new Detection(x.Text, BarcodeFormat.CODE_39.ToString(), x.Y, x.X, true))
                .ToList();
        }
        catch (Exception)
        {
            // Recovering a damaged barcode is a bonus on top of what the strict passes found; it must
            // never be the reason a page reports nothing at all.
            return [];
        }
    }

    private static List<Detection> DecodeAll(
        BarcodeReader<LuminanceSource> reader, LuminanceSource source, double positionScale)
    {
        var results = reader.DecodeMultiple(source);
        if (results == null || results.Length == 0)
        {
            var single = reader.Decode(source);
            results = single != null ? [single] : [];
        }
        return results
            .Select(x => new Detection(
                x.Text,
                x.BarcodeFormat.ToString(),
                (float) (GetReadingOrderY(x) / positionScale),
                (float) (GetReadingOrderX(x) / positionScale)))
            .ToList();
    }

    private static List<Detection> DecodeDownscaled(BarcodeReader<LuminanceSource> reader, GrayLuminanceSource source)
    {
        if (source.Width < MIN_WIDTH_FOR_RETRY)
        {
            return [];
        }
        try
        {
            return DecodeAll(reader, source.Downscaled(RETRY_SCALE), RETRY_SCALE);
        }
        catch (Exception)
        {
            // A page we can't scale is not a reason to lose the barcodes the full-resolution pass
            // already found.
            return [];
        }
    }

    private static IList<BarcodeFormat>? GetPossibleFormats(BarcodeDetectionOptions options)
    {
        if (options.Symbologies.Count > 0)
        {
            return options.Symbologies.SelectMany(x => x.ToZXingFormats()).Distinct().ToList();
        }
        // Legacy callers only ask for patch-t, which is carried by Code 39.
        return options.PatchTOnly ? [PATCH_T_FORMAT] : null;
    }

    private static BarcodeValue? PickPrimary(List<BarcodeValue> all, BarcodeDetectionOptions options)
    {
        if (all.Count == 0)
        {
            return null;
        }
        foreach (var symbology in options.Symbologies)
        {
            var match = all.FirstOrDefault(x => symbology.Matches(x.Format, x.Text));
            if (match != null)
            {
                return match;
            }
        }
        if (options.Symbologies.Count > 0)
        {
            // The profile asked for specific symbologies and none of them matched. Still report the page's
            // barcodes via AllDetections, but don't let an unrelated code act as the primary value.
            return null;
        }
        return all.FirstOrDefault(x => x.Format == PATCH_T_FORMAT.ToString()) ?? all[0];
    }

    private static float GetReadingOrderY(Result result) =>
        result.ResultPoints is { Length: > 0 } points ? points.Min(p => p.Y) : 0;

    private static float GetReadingOrderX(Result result) =>
        result.ResultPoints is { Length: > 0 } points ? points.Min(p => p.X) : 0;
    
    /// <summary>
    /// The page as one byte of luminance per pixel, which is the only form any of the passes reads it
    /// in. Sharing one of these across the passes is what keeps the page from being walked over once per
    /// pass, and it is what the downscaled pass resamples -- two thirds of the bytes GDI+ used to
    /// resample were the colour channels the decode then threw away.
    /// </summary>
    private class GrayLuminanceSource : LuminanceSource
    {
        public static GrayLuminanceSource FromImage(IMemoryImage image)
        {
            var dstPixelInfo = new PixelInfo(image.Width, image.Height, SubPixelType.Gray);
            var matrix = new byte[dstPixelInfo.Length];
            new CopyBitwiseImageOp().Perform(image, matrix, dstPixelInfo);
            return new GrayLuminanceSource(matrix, image.Width, image.Height);
        }

        public GrayLuminanceSource(byte[] matrix, int width, int height) : base(width, height)
        {
            Matrix = matrix;
        }

        /// <summary>
        /// A smaller copy of the page, filtered rather than sampled: the kernel widens with the scale, so
        /// an output pixel averages every source pixel it covers. That averaging is the whole reason the
        /// second pass exists -- print noise between the bars defeats the binarizer at full size and
        /// disappears here -- so a cheaper resampling is not a substitute. Measured over the customer's
        /// paperwork, a box filter loses barcodes on pages where this pass is the only one that reads
        /// them.
        /// </summary>
        public GrayLuminanceSource Downscaled(double scale)
        {
            var width = Math.Max((int) Math.Round(Width * scale), 1);
            var height = Math.Max((int) Math.Round(Height * scale), 1);
            return new GrayLuminanceSource(
                BicubicResampler.Resample(Matrix, Width, Height, 1, width, height), width, height);
        }

        public override byte[] getRow(int y, byte[]? row)
        {
            row ??= new byte[Width];
            Array.Copy(Matrix, y * Width, row, 0, Width);
            return row;
        }

        public override byte[] Matrix { get; }

        // Required by ZXing's multi-barcode reader, which crops the image to search for further
        // barcodes around one it already found. Without this it throws and no barcode is detected at all.
        public override bool CropSupported => true;

        public override LuminanceSource crop(int left, int top, int width, int height)
        {
            if (left < 0 || top < 0 || width < 0 || height < 0 ||
                left + width > Width || top + height > Height)
            {
                throw new ArgumentException("Crop rectangle does not fit within image data.");
            }
            var cropped = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                Array.Copy(Matrix, (top + y) * Width + left, cropped, y * width, width);
            }
            return new GrayLuminanceSource(cropped, width, height);
        }
    }
}