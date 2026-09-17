using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NAPS2.Scan.Internal;

internal class RemoteScanController : IRemoteScanController
{
    private readonly IScanDriverFactory _scanDriverFactory;
    private readonly IRemotePostProcessor _remotePostProcessor;
    private readonly ILogger _logger;

    public RemoteScanController(ScanningContext scanningContext)
        : this(new ScanDriverFactory(scanningContext), new RemotePostProcessor(scanningContext),
            scanningContext.Logger)
    {
    }

    public RemoteScanController(IScanDriverFactory scanDriverFactory, IRemotePostProcessor remotePostProcessor,
        ILogger? logger = null)
    {
        _scanDriverFactory = scanDriverFactory;
        _remotePostProcessor = remotePostProcessor;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task GetDevices(ScanOptions options, CancellationToken cancelToken, Action<ScanDevice> callback)
    {
        await _scanDriverFactory.Create(options).GetDevices(options, cancelToken, device =>
        {
            var skipWiaDevices = options.Driver == Driver.Twain && !options.TwainOptions.IncludeWiaDevices;
            if (skipWiaDevices && device.ID.StartsWith("WIA-", StringComparison.InvariantCulture))
            {
                return;
            }
            callback(device);
        });
    }

    public async Task<ScanCaps> GetCaps(ScanOptions options, CancellationToken cancelToken)
    {
        return await _scanDriverFactory.Create(options).GetCaps(options, cancelToken);
    }

    public async Task Scan(ScanOptions options, CancellationToken cancelToken, IScanEvents scanEvents,
        Action<ProcessedImage, PostProcessingContext> callback)
    {
        var driver = _scanDriverFactory.Create(options);
        var progressThrottle = new EventThrottle<double>(scanEvents.PageProgress);
        var driverScanEvents = new ScanEvents(() =>
        {
            scanEvents.PageStart();
            progressThrottle.Reset();
        }, progressThrottle.OnlyIfChanged, scanEvents.DeviceUriChanged);
        int pageNumber = 0;
        // Post-processing a page takes hundreds of milliseconds and used to happen right here, on the
        // thread the driver delivers pages on, so the next page could not even be received until the
        // last one was through. The pipeline spreads that over cores and still hands the pages on in
        // scan order; the page number is assigned here, before anything is queued, because duplex
        // front/back is derived from it.
        var pipeline = new PostProcessingPipeline(_remotePostProcessor, options, callback, _logger);
        try
        {
            await driver.Scan(options, cancelToken, driverScanEvents, image =>
            {
                var postProcessingContext = new PostProcessingContext
                {
                    // Note we still want to increment even if we don't send a page callback (i.e. when blank detection is
                    // on). The page number is only used to determine whether we're on the front or back of a duplex scan.
                    PageNumber = ++pageNumber
                };
                pipeline.Submit(image, postProcessingContext);
            });
        }
        catch (Exception)
        {
            // The scan's own failure is the one worth reporting, so the pages still in the pipeline are
            // let go of quietly rather than allowed to raise a second one over it.
            await pipeline.Drain();
            throw;
        }
        await pipeline.Finish();
    }
}
