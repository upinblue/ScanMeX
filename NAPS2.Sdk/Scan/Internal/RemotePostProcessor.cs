using Microsoft.Extensions.Logging;
using NAPS2.Images.Bitwise;

namespace NAPS2.Scan.Internal;

internal class RemotePostProcessor : IRemotePostProcessor
{
    private readonly ScanningContext _scanningContext;
    private readonly ILogger _logger;

    public RemotePostProcessor(ScanningContext scanningContext)
    {
        _scanningContext = scanningContext;
        _logger = scanningContext.Logger;
    }


    //using (var result = PostProcessStep1(output, scanProfile))
    //{
    //    if (blankDetector.ExcludePage(result, scanProfile))
    //    {
    //        return null;
    //    }

    //    ScanBitDepth bitDepth = scanProfile.UseNativeUI ? ScanBitDepth.C24Bit : scanProfile.BitDepth;
    //    var image = new ScannedImage(result, bitDepth, scanProfile.MaxQuality, scanProfile.Quality);
    //    PostProcessStep2(image, result, scanProfile, scanParams, pageNumber);
    //    string tempPath = SaveForBackgroundOcr(result, scanParams);
    //    RunBackgroundOcr(image, scanParams, tempPath);
    //    return image;
    //}

    public ProcessedImage? PostProcess(IMemoryImage image, ScanOptions options,
        PostProcessingContext postProcessingContext)
    {
        // Every page of a scan goes through this on one thread, and which of these steps costs what
        // depends entirely on the paper and the profile -- a page with a barcode on it costs several
        // times one without. When an operator reports that scanning is slow, this line is the difference
        // between guessing and knowing.
        var timer = new StepTimer();
        var pageNumber = postProcessingContext.PageNumber;
        var size = $"{image.Width}x{image.Height}";
        image = DoInitialTransforms(image, options);
        timer.Mark("initial transforms");
        try
        {
            if (options.ExcludeBlankPages)
            {
                var op = new BlankDetectionImageOp(options.BlankPageWhiteThreshold, options.BlankPageCoverageThreshold);
                op.Perform(image);
                timer.Mark("blank detection");
                if (op.IsBlank)
                {
                    // TODO: Consider annotating the image as blank via postprocessingdata rather than excluding here
                    // TODO: In theory we might want to add some functionality to allow the user to correct blank detection
                    // A page that leaves the scan here leaves no other trace: it is not in the window, not
                    // in a document and not in the archive, and to the operator it looks exactly like a
                    // sheet the feeder missed.
                    _logger.LogDebug(
                        "Page {Page} ({Size}) was detected as blank and dropped from the scan. " +
                        "Post-processing took {Total} ms ({Steps}).",
                        pageNumber, size, timer.TotalMs, timer.Describe());
                    return null;
                }
            }

            var scannedImage = _scanningContext.CreateProcessedImage(image, options.MaxQuality,
                options.Quality, options.PageSize);
            timer.Mark("store");
            DoRevertibleTransforms(ref scannedImage, ref image, options, postProcessingContext, timer);
            postProcessingContext.TempPath = SaveForBackgroundOcr(image, options);
            timer.Mark("ocr hand-off");
            _logger.LogDebug("Page {Page} ({Size}) post-processed in {Total} ms ({Steps}).",
                pageNumber, size, timer.TotalMs, timer.Describe());
            return scannedImage;
        }
        finally
        {
            // Can't use "using" as the image reference could change
            image.Dispose();
        }
    }

    /// <summary>
    /// Times the post-processing steps for one page. A step is only named once it has actually run, so a
    /// step the profile skips costs no line rather than showing up as zero; the time of anything between
    /// two marks falls to the later one.
    /// </summary>
    private class StepTimer
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly List<string> _steps = [];
        private long _previous;

        public void Mark(string step)
        {
            var elapsed = _stopwatch.ElapsedMilliseconds;
            _steps.Add($"{step} {elapsed - _previous} ms");
            _previous = elapsed;
        }

        public long TotalMs => _stopwatch.ElapsedMilliseconds;

        public string Describe() => _steps.Count > 0 ? string.Join(", ", _steps) : "no steps";
    }

    private IMemoryImage DoInitialTransforms(IMemoryImage original, ScanOptions options)
    {
        if (!options.UseNativeUI && options.BitDepth == BitDepth.BlackAndWhite)
        {
            // Ensure we actually have a black & white image (this is a no-op if we already do)
            original = original.PerformTransform(new BlackWhiteTransform(-options.Brightness));
        }

        var scaled = original;
        if (!options.UseNativeUI && options.ScaleRatio > 1)
        {
            var scaleFactor = 1.0 / options.ScaleRatio;
            scaled = scaled.PerformTransform(new ScaleTransform(scaleFactor));
        }

        if (!options.UseNativeUI && (options.StretchToPageSize || options.CropToPageSize))
        {
            scaled = CropAndStretch(original, options, scaled);
        }

        return scaled;
    }

    private IMemoryImage CropAndStretch(IMemoryImage original, ScanOptions options, IMemoryImage scaled)
    {
        if (original.HorizontalResolution <= 0 || original.VerticalResolution <= 0)
        {
            _logger.LogDebug("Skipping StretchToPageSize/CropToPageSize as there is no resolution data");
            return scaled;
        }

        float width = original.Width / original.HorizontalResolution;
        float height = original.Height / original.VerticalResolution;

        if ((options.PageSize!.Width > options.PageSize.Height) ^ (width > height))
        {
            if (options.CropToPageSize)
            {
                scaled = scaled.PerformTransform(new CropTransform(
                    0,
                    (int) ((width - (float) options.PageSize.HeightInInches) * original.HorizontalResolution),
                    0,
                    (int) ((height - (float) options.PageSize.WidthInInches) * original.VerticalResolution)
                ));
            }
            else
            {
                scaled.SetResolution((float) (original.Width / options.PageSize.HeightInInches),
                    (float) (original.Height / options.PageSize.WidthInInches));
            }
        }
        else
        {
            if (options.CropToPageSize)
            {
                scaled = scaled.PerformTransform(new CropTransform
                (
                    0,
                    (int) ((width - (float) options.PageSize.WidthInInches) * original.HorizontalResolution),
                    0,
                    (int) ((height - (float) options.PageSize.HeightInInches) * original.VerticalResolution)
                ));
            }
            else
            {
                scaled.SetResolution((float) (original.Width / options.PageSize.WidthInInches),
                    (float) (original.Height / options.PageSize.HeightInInches));
            }
        }
        return scaled;
    }

    // TODO: This is more than just transforms.
    private void DoRevertibleTransforms(ref ProcessedImage processedImage, ref IMemoryImage image, ScanOptions options,
        PostProcessingContext postProcessingContext, StepTimer timer)
    {
        var data = processedImage.PostProcessingData with
        {
            PageNumber = postProcessingContext.PageNumber
        };

        if ((!options.UseNativeUI && options.BrightnessContrastAfterScan) ||
            options.Driver is not (Driver.Wia or Driver.Twain))
        {
            processedImage = processedImage.WithTransform(new BrightnessTransform(options.Brightness), true);
            processedImage = processedImage.WithTransform(new TrueContrastTransform(options.Contrast), true);
        }

        if (options.PaperSource == PaperSource.Duplex)
        {
            data = data with
            {
                PageSide = postProcessingContext.PageNumber % 2 == 0 ? PageSide.Back : PageSide.Front
            };
            if (options.FlipDuplexedPages && data.PageSide == PageSide.Back)
            {
                processedImage = processedImage.WithTransform(new RotationTransform(180), true);
            }
        }

        if (options.RotateDegrees != 0)
        {
            processedImage = processedImage.WithTransform(new RotationTransform(options.RotateDegrees), true);
        }

        if (options.AutoDeskew)
        {
            processedImage = processedImage.WithTransform(Deskewer.GetDeskewTransform(image), true);
            timer.Mark("deskew");
        }

        if (!data.Barcode.IsDetected)
        {
            // Even if barcode detection was attempted previously and failed, image adjustments may improve detection.
            data = data with
            {
                Barcode = BarcodeDetector.Detect(image, options.BarcodeDetectionOptions)
            };
            timer.Mark("barcode");
        }
        if (options.ThumbnailSize.HasValue)
        {
            data = data with
            {
                // TODO: Maybe there's a way we can do this without needing to clone
                Thumbnail = image.Clone()
                    .PerformAllTransforms(processedImage.TransformState.Transforms)
                    .PerformTransform(new ThumbnailTransform(options.ThumbnailSize.Value)),
                ThumbnailTransformState = processedImage.TransformState
            };
            timer.Mark("thumbnail");
        }
        processedImage = processedImage.WithPostProcessingData(data, true);
    }

    private string? SaveForBackgroundOcr(IMemoryImage bitmap, ScanOptions options)
    {
        if (!string.IsNullOrEmpty(options.OcrParams.LanguageCode))
        {
            // TODO: If we use tesseract as a library, this is something that that could potentially improve (i.e. not having to save to disk)
            // But then again, that doesn't make as much sense on systems (i.e. linux) where tesseract would be provided as an external package
            return _scanningContext.SaveToTempFile(bitmap);
        }
        return null;
    }
}