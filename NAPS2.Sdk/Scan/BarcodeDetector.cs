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

    public static Barcode Detect(IMemoryImage image, BarcodeDetectionOptions options)
    {
        // TODO: Probably shouldn't have DetectBarcodes be in the options class? The call shouldn't happen at all.
        if (!options.DetectBarcodes)
        {
            return Barcode.NoDetection;
        }

        var reader = new BarcodeReader<IMemoryImage>(x => new MemoryImageLuminanceSource(x))
        {
            Options = options.ZXingOptions ?? new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = GetPossibleFormats(options)
            }
        };

        // A page may carry several barcodes (e.g. an order and an article code), so we keep all of them
        // and let the profile's symbology selection decide which one is the primary.
        var results = reader.DecodeMultiple(image);
        if (results == null || results.Length == 0)
        {
            var single = reader.Decode(image);
            results = single != null ? [single] : [];
        }

        var all = results
            .OrderBy(GetReadingOrderY)
            .ThenBy(GetReadingOrderX)
            .Select(x => new BarcodeValue(x.Text, x.BarcodeFormat.ToString()))
            .ToList();
        var primary = PickPrimary(all, options);
        return new Barcode(true, primary != null, primary?.Text, primary?.Format)
        {
            AllDetections = all
        };
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
    
    private class MemoryImageLuminanceSource : LuminanceSource
    {
        public MemoryImageLuminanceSource(IMemoryImage image)
            : base(image.Width, image.Height)
        {
            var dstPixelInfo = new PixelInfo(image.Width, image.Height, SubPixelType.Gray);
            var matrix = new byte[dstPixelInfo.Length];
            new CopyBitwiseImageOp().Perform(image, matrix, dstPixelInfo);
            Matrix = matrix;
        }

        private MemoryImageLuminanceSource(byte[] matrix, int width, int height) : base(width, height)
        {
            Matrix = matrix;
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
            return new MemoryImageLuminanceSource(cropped, width, height);
        }
    }
}