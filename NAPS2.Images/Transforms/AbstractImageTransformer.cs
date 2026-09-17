using NAPS2.Images.Bitwise;
using NAPS2.Util;

namespace NAPS2.Images.Transforms;

internal abstract class AbstractImageTransformer<TImage> where TImage : IMemoryImage
{
    protected AbstractImageTransformer(ImageContext imageContext)
    {
        ImageContext = imageContext;
    }

    protected ImageContext ImageContext { get; }

    public TImage Apply(TImage image, Transform transform)
    {
        if (image.PixelFormat == ImagePixelFormat.Unknown)
        {
            throw new ArgumentException("Unsupported pixel format for transforms");
        }
        switch (transform)
        {
            case BrightnessTransform brightnessTransform:
                return PerformTransform(image, brightnessTransform);
            case TrueContrastTransform trueContrastTransform:
                return PerformTransform(image, trueContrastTransform);
            case HueTransform hueTransform:
                return PerformTransform(image, hueTransform);
            case SaturationTransform saturationTransform:
                return PerformTransform(image, saturationTransform);
            case SharpenTransform sharpenTransform:
                return PerformTransform(image, sharpenTransform);
            case RotationTransform rotationTransform:
                return PerformTransform(image, rotationTransform);
            case CropTransform cropTransform:
                return PerformTransform(image, cropTransform);
            case ScaleTransform scaleTransform:
                return PerformTransform(image, scaleTransform);
            case ResizeTransform resizeTransform:
                return PerformTransform(image, resizeTransform);
            case ThumbnailTransform thumbnailTransform:
                return PerformTransform(image, thumbnailTransform);
            case BlackWhiteTransform blackWhiteTransform:
                return PerformTransform(image, blackWhiteTransform);
            case GrayscaleTransform grayscaleTransform:
                return PerformTransform(image, grayscaleTransform);
            case ColorBitDepthTransform colorBitDepthTransform:
                return PerformTransform(image, colorBitDepthTransform);
            case CorrectionTransform correctionTransform:
                return PerformTransform(image, correctionTransform);
            default:
                throw new ArgumentException($"Unsupported transform type: {transform.GetType().FullName}");
        }
    }

    private TImage PerformTransform(TImage image, CorrectionTransform transform)
    {
        image.UpdateLogicalPixelFormat();
        if (image.LogicalPixelFormat == ImagePixelFormat.BW1)
        {
            return image;
        }
        // TODO: Include deskew?
        // TODO: Add border detection/removal? After deskew.
        var stopwatch = Stopwatch.StartNew();
        // ColumnColorOp.PerformFullOp(image);
        // Console.WriteLine($"Column color op time: {stopwatch.ElapsedMilliseconds}");
        stopwatch.Restart();
        if (transform.Mode == CorrectionMode.Document)
        {
            WhiteBlackPointOp.PerformFullOp(image, transform.Mode);
            // Console.WriteLine($"White/black point op time: {stopwatch.ElapsedMilliseconds}");
            stopwatch.Restart();
            // A previous version ran a filter pass before white/black point correction with the theory that it could
            // help the accuracy of that correction.
            // But running after is much faster (>2x) as the filter is optimized to skip pure-white blocks.
            // Plus the filter color-distance function assumes a normal 0-255 color range - if that's compressed (and
            // not yet corrected) it could potentially remove fine details.
            var image2 = (TImage) image.CopyBlank();
            new BilateralFilterOp().Perform(image, image2);
            // Console.WriteLine($"Bilateral filter op time: {stopwatch.ElapsedMilliseconds}");
            stopwatch.Restart();
            image.Dispose();
            return image2;
        }
        else
        {
            WhiteBlackPointOp.PerformFullOp(image, transform.Mode);
            // Console.WriteLine($"White/black point op time: {stopwatch.ElapsedMilliseconds}");
            stopwatch.Restart();
            return image;
        }
    }

    protected virtual TImage PerformTransform(TImage image, BrightnessTransform transform)
    {
        float brightnessNormalized = transform.Brightness / 1000f;
        EnsurePixelFormat(ref image);
        new BrightnessBitwiseImageOp(brightnessNormalized).Perform(image);
        return image;
    }

    protected virtual TImage PerformTransform(TImage image, TrueContrastTransform transform)
    {
        if (image.PixelFormat is ImagePixelFormat.BW1)
        {
            // No need to handle black/white since contrast is a null transform
            return image;
        }

        var contrastNormalized = transform.Contrast / 1000f;
        EnsurePixelFormat(ref image);
        new ContrastBitwiseImageOp(contrastNormalized).Perform(image);
        return image;
    }

    protected virtual TImage PerformTransform(TImage image, HueTransform transform)
    {
        if (image.PixelFormat is ImagePixelFormat.BW1 or ImagePixelFormat.Gray8)
        {
            // No need to handle grayscale since hue shifts are null transforms
            return image;
        }

        float hueShiftNormalized = transform.HueShift / 1000f;
        new HueShiftBitwiseImageOp(hueShiftNormalized).Perform(image);
        return image;
    }

    protected virtual TImage PerformTransform(TImage image, SaturationTransform transform)
    {
        if (image.PixelFormat is ImagePixelFormat.BW1 or ImagePixelFormat.Gray8)
        {
            // No need to handle grayscale since saturation is a null transform
            return image;
        }

        float saturationNormalized = transform.Saturation / 1000f;
        new SaturationBitwiseImageOp(saturationNormalized).Perform(image);
        return image;
    }

    protected virtual TImage PerformTransform(TImage image, SharpenTransform transform)
    {
        if (image.PixelFormat is ImagePixelFormat.BW1 or ImagePixelFormat.Gray8)
        {
            // TODO: Handle grayscale
            return image;
        }

        var newImage = image.CopyBlank();

        float sharpnessNormalized = transform.Sharpness / 1000f;
        new SharpenBitwiseImageOp(sharpnessNormalized).Perform(image, newImage);

        image.Dispose();
        return (TImage) newImage;
    }

    protected abstract TImage PerformTransform(TImage image, RotationTransform transform);

    protected virtual TImage PerformTransform(TImage image, CropTransform transform)
    {
        double xScale = image.Width / (double) (transform.OriginalWidth ?? image.Width),
            yScale = image.Height / (double) (transform.OriginalHeight ?? image.Height);

        int x = ((int) Math.Round(transform.Left * xScale)).Clamp(0, image.Width - 1);
        int y = ((int) Math.Round(transform.Top * yScale)).Clamp(0, image.Height - 1);
        int width = (image.Width - (int) Math.Round((transform.Left + transform.Right) * xScale)).Clamp(1,
            image.Width - x);
        int height = (image.Height - (int) Math.Round((transform.Top + transform.Bottom) * yScale)).Clamp(1,
            image.Height - y);

        var result = ImageContext.Create(width, height, image.PixelFormat);
        result.SetResolution(image.HorizontalResolution, image.VerticalResolution);
        new CopyBitwiseImageOp
        {
            SourceXOffset = x,
            SourceYOffset = y,
            Columns = width,
            Rows = height
        }.Perform(image, result);
        image.Dispose();

        return (TImage) result;
    }

    protected virtual TImage PerformTransform(TImage image, ScaleTransform transform)
    {
        var width = (int) Math.Max(Math.Round(image.Width * transform.ScaleFactor), 1);
        var height = (int) Math.Max(Math.Round(image.Height * transform.ScaleFactor), 1);
        return PerformTransform(image, new ResizeTransform(width, height));
    }

    /// <summary>
    /// Gets a bitmap resized to fit within a thumbnail rectangle.
    /// </summary>
    /// <param name="image">The bitmap to resize.</param>
    /// <param name="transform">The maximum width and height of the thumbnail.</param>
    /// <returns>The thumbnail bitmap.</returns>
    /// <remarks>
    /// Resampled here rather than through ResizeTransform because on Windows that lands in GDI+, which
    /// serializes every interpolated DrawImage for the whole process -- measured at eight threads being
    /// no faster than one, down to bitmaps small enough to sit in L1. A thumbnail is made for every page
    /// of every scan (68 ms for a 300 dpi page, 264 ms for a 600 dpi one), so on a batch it is one of
    /// the two things holding the per-page work on a single core.
    ///
    /// An image with alpha is left on the backend's own path: resampling colour and alpha channels
    /// separately draws halos around transparent edges, where GDI+ premultiplies first. A scan never has
    /// alpha, so this only concerns thumbnails of imported images.
    /// </remarks>
    protected virtual TImage PerformTransform(TImage image, ThumbnailTransform transform)
    {
        var (_, _, width, height) = transform.GetDrawRect(image.Width, image.Height);
        if (image.PixelFormat == ImagePixelFormat.ARGB32)
        {
            return PerformTransform(image, new ResizeTransform(width, height));
        }

        var srcInfo = new PixelInfo(image.Width, image.Height, SubPixelType.Rgb);
        var src = new byte[srcInfo.Length];
        new CopyBitwiseImageOp().Perform(image, src, srcInfo);
        var resampled = BicubicResampler.Resample(src, image.Width, image.Height, 3, width, height);

        var result = ImageContext.Create(width, height, ImagePixelFormat.RGB24);
        new CopyBitwiseImageOp().Perform(resampled, new PixelInfo(width, height, SubPixelType.Rgb), result);
        // The same resolution ResizeTransform would have given it, so a thumbnail is no different from
        // the one this used to produce in any respect a caller can read back.
        result.SetResolution(
            image.HorizontalResolution * image.Width / width,
            image.VerticalResolution * image.Height / height);
        // OriginalFileFormat is deliberately left unset, as ResizeTransform leaves it: it decides how
        // ImageExportHelper stores an image, and a thumbnail that claims to have come from a JPEG would
        // be encoded as one instead of as whichever comes out smaller.
        image.Dispose();
        return (TImage) result;
    }

    protected abstract TImage PerformTransform(TImage image, ResizeTransform transform);

    protected virtual TImage PerformTransform(TImage image, BlackWhiteTransform transform)
    {
        if (image.PixelFormat == ImagePixelFormat.BW1)
        {
            return image;
        }

        var monoBitmap = image.CopyBlankWithPixelFormat(ImagePixelFormat.BW1);
        new CopyBitwiseImageOp
        {
            BlackWhiteThreshold = transform.Threshold / 1000f
        }.Perform(image, monoBitmap);
        image.Dispose();

        return (TImage) monoBitmap;
    }

    protected virtual TImage PerformTransform(TImage image, GrayscaleTransform transform)
    {
        if (image.PixelFormat is ImagePixelFormat.BW1 or ImagePixelFormat.Gray8)
        {
            return image;
        }

        var grayscaleBitmap = image.CopyWithPixelFormat(ImagePixelFormat.Gray8);
        image.Dispose();
        return (TImage) grayscaleBitmap;
    }

    protected virtual TImage PerformTransform(TImage image, ColorBitDepthTransform transform)
    {
        if (image.PixelFormat is ImagePixelFormat.RGB24 or ImagePixelFormat.ARGB32)
        {
            return image;
        }

        var colorBitmap = image.CopyWithPixelFormat(ImagePixelFormat.RGB24);
        image.Dispose();
        return (TImage) colorBitmap;
    }

    /// <summary>
    /// If the provided bitmap is 1-bit (black and white), replace it with a 24-bit bitmap so that image transforms will work. If the bitmap is replaced, the original is disposed.
    /// </summary>
    /// <param name="image">The bitmap that may be replaced.</param>
    protected void EnsurePixelFormat(ref TImage image)
    {
        if (image.PixelFormat == ImagePixelFormat.BW1)
        {
            image = PerformTransform(image, new ColorBitDepthTransform());
        }
    }

    /// <summary>
    /// If the original bitmap is 1-bit (black and white), optimize the result by making it 1-bit too.
    /// </summary>
    /// <param name="original">The original bitmap that is used to determine whether the result should be black and white.</param>
    /// <param name="result">The result that may be replaced.</param>
    protected void OptimizePixelFormat(TImage original, ref TImage result)
    {
        if (original.UpdateLogicalPixelFormat() == ImagePixelFormat.BW1)
        {
            result = PerformTransform(result, new BlackWhiteTransform());
        }
    }
}