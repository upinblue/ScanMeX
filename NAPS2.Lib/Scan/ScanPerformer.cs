using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using NAPS2.ImportExport;
using NAPS2.Ocr;
using NAPS2.Sap;
using NAPS2.Scan.Exceptions;
using NAPS2.Scan.Internal;
#if !MAC
using NAPS2.Scan.Internal.Wia;
using NAPS2.Wia;
#endif

namespace NAPS2.Scan;

internal class ScanPerformer : IScanPerformer
{
    public static Driver ParseDriver(string? value)
    {
        return value switch
        {
            DriverNames.WIA => Driver.Wia,
            DriverNames.SANE => Driver.Sane,
            DriverNames.TWAIN => Driver.Twain,
            DriverNames.ESCL => Driver.Escl,
            DriverNames.APPLE => Driver.Apple,
            _ => Driver.Default
        };
    }

    public static string SystemDefaultDriverName =>
        ScanOptionsValidator.SystemDefaultDriver.ToString().ToLowerInvariant();

    private readonly ScanningContext _scanningContext;
    private readonly IDevicePrompt _devicePrompt;
    private readonly Naps2Config _config;
    private readonly OperationProgress _operationProgress;
    private readonly AutoSaver _autoSaver;
    private readonly IProfileManager _profileManager;
    private readonly ErrorOutput _errorOutput;
    private readonly ScanOptionsValidator _scanOptionsValidator;
    private readonly IScanBridgeFactory _scanBridgeFactory;
    private readonly OcrOperationManager _ocrOperationManager;

    public ScanPerformer(IDevicePrompt devicePrompt, Naps2Config config, OperationProgress operationProgress,
        AutoSaver autoSaver, IProfileManager profileManager, ErrorOutput errorOutput,
        ScanOptionsValidator scanOptionsValidator, IScanBridgeFactory scanBridgeFactory,
        ScanningContext scanningContext, OcrOperationManager ocrOperationManager)
    {
        _devicePrompt = devicePrompt;
        _config = config;
        _operationProgress = operationProgress;
        _autoSaver = autoSaver;
        _profileManager = profileManager;
        _errorOutput = errorOutput;
        _scanOptionsValidator = scanOptionsValidator;
        _scanBridgeFactory = scanBridgeFactory;
        _scanningContext = scanningContext;
        _ocrOperationManager = ocrOperationManager;
    }

    public async Task<DeviceChoice> PromptForDevice(ScanProfile scanProfile, bool allowAlwaysAsk,
        IntPtr dialogParent = default)
    {
        try
        {
            var options = BuildOptions(scanProfile, new ScanParams(), dialogParent, true);
            return await _devicePrompt.PromptForDevice(options, allowAlwaysAsk);
        }
        catch (Exception error)
        {
            HandleError(error);
            return DeviceChoice.None;
        }
    }

    public IAsyncEnumerable<ScanDevice> GetDevices(ScanProfile scanProfile, CancellationToken cancelToken = default)
    {
        var options = BuildOptions(scanProfile, new ScanParams(), IntPtr.Zero, true);
        var controller = CreateScanController(new ScanParams());
        return controller.GetDevices(options, cancelToken);
    }

    public async Task<ScanCaps> GetCaps(ScanProfile scanProfile, CancellationToken cancelToken = default)
    {
        var options = BuildOptions(scanProfile, new ScanParams(), IntPtr.Zero, false);
        options.Device = scanProfile.Device?.ToScanDevice(options.Driver);
        var controller = CreateScanController(new ScanParams());
        return await controller.GetCaps(options, cancelToken);
    }

    public async IAsyncEnumerable<ProcessedImage> PerformScan(ScanProfile scanProfile, ScanParams scanParams,
        IntPtr dialogParent = default, [EnumeratorCancellation] CancellationToken cancelToken = default)
    {
        var options = BuildOptions(scanProfile, scanParams, dialogParent, false);
        // Make sure we get a real driver value (not just "Default")
        options = _scanOptionsValidator.ValidateAll(options, _scanningContext, false);

        if (!await PopulateDevice(scanProfile, options))
        {
            // User cancelled out of a dialog
            ScanConsole.Scan($"Scan aborted: no device selected (profile '{scanProfile.DisplayName}').");
            yield break;
        }

        LogScanStart(scanProfile, options);

        var controller = CreateScanController(scanParams);
        var op = new ScanOperation(options);

        controller.PageStart += (sender, args) => op.NextPage(args.PageNumber);
        controller.ScanEnd += (sender, args) =>
        {
            // Close the progress window before showing the error dialog
            op.Completed();
            if (args.Error != null)
            {
                HandleError(args.Error);
            }
        };
        controller.DeviceUriChanged += (sender, args) =>
        {
            if (scanProfile.Device != null)
            {
                scanProfile.Device = scanProfile.Device with
                {
                    IconUri = args.IconUri,
                    ConnectionUri = args.ConnectionUri
                };
                _profileManager.Save();
            }
        };
        controller.PropagateErrors = false;
        TranslateProgress(controller, op);

        ShowOperation(op, options, scanParams);
        cancelToken.Register(op.Cancel);

        // Logged before auto save, because a profile that clears images after saving produces nothing
        // downstream -- the console still has to show that pages were scanned.
        var images = LogPages(controller.Scan(options, op.CancelToken));

        if (scanProfile.EnableAutoSave && scanProfile.AutoSaveSettings != null && !scanParams.NoAutoSave)
        {
            images = _autoSaver.Save(scanProfile, scanProfile.AutoSaveSettings, images);
        }
        else
        {
            ScanConsole.Profile(scanProfile.EnableAutoSave
                ? "Auto save is enabled but has no settings, so nothing is saved or uploaded."
                : "Auto save is disabled, so this scan is not saved or uploaded automatically.");
        }

        int pageCount = 0;
        try
        {
            await foreach (var image in images)
            {
                pageCount++;
                yield return image;
            }
        }
        finally
        {
            if (pageCount > 0)
            {
                // TODO: Test event logging
                Log.Event(EventType.Scan, new EventParams
                {
                    Name = MiscResources.Scan,
                    Pages = pageCount,
                    DeviceName = scanProfile.Device?.Name,
                    ProfileName = scanProfile.DisplayName,
                    BitDepth = scanProfile.BitDepth.Description()
                });
            }
        }
    }

    /// <summary>
    /// The lowest scan resolution at which barcode separation can be expected to work.
    /// </summary>
    private const int MinReliableBarcodeDpi = 240;

    /// <summary>
    /// Writes the scan's starting conditions to the console. Most "nothing happened" reports come down to
    /// a profile setting, so the settings that decide whether anything is saved, separated or uploaded are
    /// recorded before the first page arrives.
    /// </summary>
    private static void LogScanStart(ScanProfile scanProfile, ScanOptions options)
    {
        ScanConsole.Scan(
            $"Scan started. Profile='{scanProfile.DisplayName}', Device='{scanProfile.Device?.Name ?? "(none)"}', " +
            $"Driver={options.Driver}, Source={options.PaperSource}, {options.Dpi} dpi, BitDepth={options.BitDepth}");

        var workflow = DocumentWorkflowSettings.ForProfile(scanProfile);
        var symbologies = workflow.GetEffectiveSymbologies();
        ScanConsole.Profile(
            $"Separation={workflow.SeparationMode}, Symbologies={(symbologies.Count == 0 ? "(none)" : string.Join("+", symbologies))}, " +
            $"Pattern='{workflow.SeparationPattern ?? ""}', IdMode={workflow.IdMode}, " +
            $"UploadTrigger={workflow.UploadTrigger}, KeepLocalCopy={workflow.KeepLocalCopy}");
        ScanConsole.Profile(
            $"BarcodeDetection={options.BarcodeDetectionOptions.DetectBarcodes}, " +
            $"AutoSave={scanProfile.EnableAutoSave}, AutoSavePath='{scanProfile.AutoSaveSettings?.FilePath ?? ""}'");

        // Which barcode ends up in $(barcode) and $(barcode:1) is decided by this pattern, and getting it
        // wrong names the file after the article number instead of the order number -- which looks like a
        // correct scan afterwards. Say which pattern is in force, and say when there isn't one.
        if (options.BarcodeDetectionOptions.DetectBarcodes)
        {
            var selection = scanProfile.GetBarcodeSelectionPattern();
            ScanConsole.Profile(string.IsNullOrWhiteSpace(selection)
                ? "No barcode regex is set, so $(barcode) and $(barcode:1) take the first barcode in " +
                  "reading order on the page."
                : $"Barcode regex '{selection}' decides which barcode $(barcode) and $(barcode:1) yield.");
            if (symbologies.Count == 0)
            {
                // With no symbology restriction ZXing reads anything it can, and dense production sheets
                // yield codes that are not on the paper at all.
                ScanConsole.Profile(
                    "WARNING: no barcode type is selected, so every symbology is decoded. Dense sheets can " +
                    "produce phantom EAN/UPC reads; select the types the paperwork actually carries.");
            }
        }

        // Measured against real Code 39 production papers: nothing decodes below 200 dpi, and detection
        // only becomes reliable from 240 dpi. Scanning at a lower resolution with separation enabled looks
        // like a broken detector, so say plainly that the resolution is the reason.
        if (options.BarcodeDetectionOptions.DetectBarcodes && options.Dpi < MinReliableBarcodeDpi)
        {
            ScanConsole.Profile(
                $"WARNING: barcode detection is on but the profile scans at {options.Dpi} dpi. " +
                $"Barcodes are rarely readable below {MinReliableBarcodeDpi} dpi; use 300 dpi for separation.");
        }

        var targets = new List<string>();
        if (scanProfile.UploadsToSharePoint()) targets.Add("SharePoint");
        if (scanProfile.UploadsToSap()) targets.Add("SAP ArchiveLink");
        ScanConsole.Profile(targets.Count == 0
            ? "No upload target is enabled for this profile."
            : $"Upload targets: {string.Join(", ", targets)}");

        // Uploading is driven by auto save: it is the step that produces the file. With auto save off the
        // ticked upload targets do nothing at all, and nothing else in the scan reports that, so a whole
        // batch can be scanned before anyone notices nothing was archived.
        if (targets.Count > 0 && !scanProfile.EnableAutoSave)
        {
            ScanConsole.Profile(
                $"WARNING: {string.Join(" and ", targets)} upload is enabled but auto save is off for this " +
                "profile. Uploading runs on the file auto save writes, so nothing will be saved or uploaded.");
        }
    }

    /// <summary>
    /// Passes pages through unchanged while reporting each one, including whether a barcode was found.
    /// A page without a barcode is reported explicitly -- that is the case operators most often need to
    /// see when separation or SAP object keys don't come out as expected.
    /// </summary>
    private static async IAsyncEnumerable<ProcessedImage> LogPages(IAsyncEnumerable<ProcessedImage> images)
    {
        var pageNumber = 0;
        await foreach (var image in images)
        {
            pageNumber++;
            var barcode = image.PostProcessingData.Barcode;
            var all = barcode.GetAllValues();
            if (!barcode.IsDetected && all.Count == 0)
            {
                ScanConsole.Barcode(barcode.IsDetectionAttempted
                    ? $"Page {pageNumber}: no barcode detected."
                    : $"Page {pageNumber}: barcode detection not enabled.");
            }
            else if (!barcode.IsDetected)
            {
                // Decoded, but none of them belongs to a symbology the profile selected, so the page has
                // no primary barcode. Reporting this as "no barcode detected" would send the operator
                // looking at the scanner and the paper when the profile is what turned the values down.
                ScanConsole.Barcode(
                    $"Page {pageNumber}: {all.Count} barcode(s) decoded but none matches the profile's " +
                    $"selected symbologies: {string.Join(", ", all.Select(x => $"{x.Format ?? "?"}:'{x.Text}'"))}");
            }
            else
            {
                var extra = all.Count > 1
                    ? $" (all: {string.Join(", ", all.Select(x => $"{x.Format}:{x.Text}"))})"
                    : "";
                ScanConsole.Barcode(
                    $"Page {pageNumber}: {barcode.DetectedFormat ?? "?"} '{barcode.DetectedText}'" +
                    $"{(barcode.IsPatchT ? " [patch-T]" : "")}{extra}");
            }
            ScanConsole.Scan($"Page {pageNumber} received.");
            yield return image;
        }
        ScanConsole.Scan($"Scanner finished after {pageNumber} page(s).");
    }

    private ScanController CreateScanController(ScanParams scanParams)
    {
        var localPostProcessor = new LocalPostProcessor(_scanningContext, ConfigureOcrController(scanParams));
        var controller = new ScanController(_scanningContext, localPostProcessor, _scanOptionsValidator,
            _scanBridgeFactory);
        return controller;
    }

    private OcrController ConfigureOcrController(ScanParams scanParams)
    {
        OcrController ocrController = new OcrController(_scanningContext);
        if (scanParams.OcrParams?.LanguageCode != null)
        {
            scanParams.OcrCancelToken.Register(() => ocrController.CancelAll());
        }
        _ocrOperationManager.RegisterOcrController(ocrController);
        return ocrController;
    }

    private void HandleError(Exception error)
    {
        if (error is not ScanDriverException)
        {
            Log.ErrorException(error.Message, error);
            _errorOutput.DisplayError(error.Message, error);
        }
        else if (error is ScanDriverUnknownException)
        {
            Log.ErrorException(error.Message, error.InnerException!);
            _errorOutput.DisplayError(error.Message, error);
        }
        else if (error is not AlreadyHandledDriverException)
        {
            _errorOutput.DisplayError(error.Message);
        }
    }

    private void ShowOperation(ScanOperation op, ScanOptions scanOptions, ScanParams scanParams)
    {
        bool isWia10 = scanOptions.Driver == Driver.Wia && scanOptions.WiaOptions.WiaApiVersion == WiaApiVersion.Wia10;
        bool showingTwainProgress = scanOptions.Driver == Driver.Twain && scanOptions.TwainOptions.ShowProgress;
        if (scanParams.NoUI || scanOptions.UseNativeUI && !isWia10 || showingTwainProgress)
        {
            return;
        }

        Invoker.Current.InvokeDispatch(() =>
        {
            if (scanParams.Modal)
            {
                _operationProgress.ShowModalProgress(op);
            }
            else
            {
                _operationProgress.ShowBackgroundProgress(op);
            }
        });
    }

    private void TranslateProgress(ScanController controller, ScanOperation op)
    {
        var smoothProgress = new SmoothProgress();
        controller.PageStart += (_, _) => smoothProgress.Reset();
        controller.PageProgress += (_, args) => smoothProgress.InputProgressChanged(args.Progress);
        controller.ScanEnd += (_, _) => smoothProgress.Reset();
        smoothProgress.OutputProgressChanged +=
            (_, args) => op.Progress((int) Math.Round(args.Value * 1000), 1000);
    }

    private ScanOptions BuildOptions(ScanProfile scanProfile, ScanParams scanParams, IntPtr dialogParent,
        bool isDeviceQuery)
    {
        var separator = scanProfile.AutoSaveSettings?.Separator;
        var workflow = DocumentWorkflowSettings.ForProfile(scanProfile);
        var symbologies = workflow.GetEffectiveSymbologies().ToList();

        var options = new ScanOptions
        {
            Driver = ParseDriver(scanProfile.DriverName),
            WiaOptions =
            {
                WiaApiVersion = scanProfile.WiaVersion,
                OffsetWidth = scanProfile.WiaOffsetWidth
            },
            TwainOptions =
            {
                Dsm = scanProfile.TwainImpl switch
                {
                    TwainImpl.X64 => TwainDsm.NewX64,
                    TwainImpl.OldDsm or TwainImpl.Legacy => TwainDsm.Old,
                    _ => TwainDsm.New
                },
                TransferMode = scanProfile.TwainImpl switch
                {
                    TwainImpl.Default or TwainImpl.X64 => TwainTransferMode.Default,
                    TwainImpl.MemXfer => TwainTransferMode.Memory,
                    _ => TwainTransferMode.Native
                },
                ShowProgress = scanProfile.TwainProgress,
                IncludeWiaDevices = false
            },
            SaneOptions =
            {
                // We use a worker process for SANE so we should clean up after each operation
                KeepInitialized = false
            },
            EsclOptions =
            {
                SecurityPolicy = _config.Get(c => c.EsclSecurityPolicy),
                SearchTimeout = isDeviceQuery ? 60_000 : 5000
            },
            KeyValueOptions = scanProfile.KeyValueOptions != null
                ? new KeyValueScanOptions(scanProfile.KeyValueOptions)
                : new KeyValueScanOptions(),
            ExcludeLocalIPs = true,
            BarcodeDetectionOptions =
            {
                // NeedsBarcodeValues covers separation, the SAP object key and -- the case that used to be
                // missed -- any template that expands to a barcode, such as an auto save path of
                // "$(barcode).pdf" on a profile that doesn't separate.
                DetectBarcodes = scanParams.DetectPatchT ||
                                 scanProfile.NeedsBarcodeValues() ||
                                 separator is SaveSeparator.PatchT or SaveSeparator.Code39Barcode,
                // The profile's symbologies drive which formats ZXing looks for and which barcode wins
                // when a page carries several. Empty means "anything".
                Symbologies = symbologies,
                // Legacy fallback for profiles that only ever asked for patch-t separator sheets.
                PatchTOnly = symbologies.Count == 0 &&
                             (scanParams.DetectPatchT ||
                              separator is SaveSeparator.PatchT or SaveSeparator.Code39Barcode)
            },
            OcrParams = scanParams.OcrParams ?? OcrParams.Empty,
            Brightness = scanProfile.Brightness,
            Contrast = scanProfile.Contrast,
            Dpi = scanProfile.Resolution.Dpi,
            Quality = scanProfile.Quality,
            AutoDeskew = scanProfile.AutoDeskew,
            RotateDegrees = scanProfile.RotateDegrees,
            BitDepth = scanProfile.BitDepth.ToBitDepth(),
            DialogParent = dialogParent,
            MaxQuality = scanProfile.MaxQuality,
            PageAlign = scanProfile.PageAlign.ToHorizontalAlign(),
            PaperSource = scanProfile.PaperSource.ToPaperSource(),
            ScaleRatio = scanProfile.AfterScanScale.ToIntScaleFactor(),
            ThumbnailSize = scanParams.ThumbnailSize,
            ExcludeBlankPages = scanProfile.ExcludeBlankPages,
            FlipDuplexedPages = scanProfile.FlipDuplexedPages,
            BlankPageCoverageThreshold = scanProfile.BlankPageCoverageThreshold,
            BlankPageWhiteThreshold = scanProfile.BlankPageWhiteThreshold,
            BrightnessContrastAfterScan = scanProfile.BrightnessContrastAfterScan,
            CropToPageSize = scanProfile.ForcePageSizeCrop,
            StretchToPageSize = scanProfile.ForcePageSize,
            UseNativeUI = scanProfile.UseNativeUI,
            Device = null, // Set after
            PageSize = null // Set after
        };

        if (separator == SaveSeparator.Code39Barcode)
        {
            _scanningContext.Logger.LogDebug("Code39 separation enabled. Regex: {Regex}", scanProfile.AutoSaveSettings?.Code39SeparationPattern ?? "<none>");
        }

        var pageDimensions = scanProfile.PageSize.PageDimensions() ?? scanProfile.CustomPageSize;
        if (pageDimensions == null)
        {
            throw new ArgumentException("No page size specified");
        }

        options.PageSize =
            new PageSize(pageDimensions.Width, pageDimensions.Height, (PageSizeUnit) pageDimensions.Unit);

        return options;
    }

    private async Task<bool> PopulateDevice(ScanProfile scanProfile, ScanOptions options)
    {
        // If a device wasn't specified, prompt the user to pick one
        if (string.IsNullOrEmpty(scanProfile.Device?.ID))
        {
            options.Device = (await _devicePrompt.PromptForDevice(options, false)).Device;
            if (options.Device == null)
            {
                return false;
            }

            // Persist the device in the profile if configured to do so
            if (_config.Get(c => c.AlwaysRememberDevice))
            {
                scanProfile.Device = ScanProfileDevice.FromScanDevice(options.Device);
                _profileManager.Save();
            }
        }
        else
        {
            options.Device = scanProfile.Device?.ToScanDevice(options.Driver);
        }

        return true;
    }
}