using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace NAPS2.Scan.Internal;

/// <summary>
/// Post-processes the pages of a scan on several threads and hands them on in the order they were
/// scanned.
/// </summary>
/// <remarks>
/// Everything a page goes through between the scanner and the window -- the transforms, blank detection,
/// writing it to its backing file, barcode detection, the thumbnail -- used to run on the thread the
/// driver delivers pages on, one page at a time, while the rest of the machine did nothing. On the
/// customer's own paperwork that is 228 ms a page at 300 dpi and 317 ms at 600 dpi, so a fast duplex
/// feeder outruns it and the operator waits at the end of the stack for work that could have happened
/// during it.
///
/// Order is what makes this safe to do at all. TransformBlock hands its results on in the order it
/// received them whatever order they finish in, so the pages reach the window, the document splitter and
/// the archive in scan order exactly as before. The page number is still assigned on the driver's thread,
/// before anything is queued, because duplex front/back is derived from it.
/// </remarks>
internal sealed class PostProcessingPipeline
{
    /// <summary>
    /// How much memory the pages in flight may take up together. A 300 dpi page is 26 MB and a 600 dpi
    /// one is 104 MB, so a fixed number of pages would mean a gigabyte at the high end; this keeps the
    /// same budget either way by running fewer pages at once when they are bigger.
    /// </summary>
    private const long MEMORY_BUDGET_BYTES = 384L * 1024 * 1024;

    /// <summary>
    /// Beyond this the steps stop scaling -- they are memory-bound rather than compute-bound -- and each
    /// further page in flight is only more memory held. Measured at 4 to 8 being worth little over 4.
    /// </summary>
    private const int MAX_PARALLELISM = 4;

    private readonly IRemotePostProcessor _postProcessor;
    private readonly ScanOptions _options;
    private readonly Action<ProcessedImage, PostProcessingContext> _callback;
    private readonly ILogger _logger;

    private TransformBlock<PageInFlight, PageDone>? _worker;
    private ActionBlock<PageDone>? _sink;
    private bool _inline;
    private bool _started;

    private readonly int? _maxParallelism;

    /// <param name="maxParallelism">
    /// How many pages may be processed at once, instead of what the machine's cores allow. The memory
    /// budget still applies on top. Only tests pass this: it is what lets them exercise the parallel path
    /// on a machine with few cores, where the pipeline would otherwise run every page inline and the
    /// properties worth testing -- that order survives, that a page outlives the driver's callback --
    /// would hold trivially.
    /// </param>
    public PostProcessingPipeline(IRemotePostProcessor postProcessor, ScanOptions options,
        Action<ProcessedImage, PostProcessingContext> callback, ILogger logger, int? maxParallelism = null)
    {
        _postProcessor = postProcessor;
        _options = options;
        _callback = callback;
        _logger = logger;
        _maxParallelism = maxParallelism;
    }

    private record PageInFlight(IMemoryImage Image, PostProcessingContext Context);

    private record PageDone(ProcessedImage? Image, PostProcessingContext Context);

    /// <summary>
    /// Takes one scanned page. Called on the driver's thread, one page at a time, and blocks while the
    /// pipeline is full -- which is the point: the scanner waiting is better than the pages piling up in
    /// memory.
    /// </summary>
    public void Submit(IMemoryImage image, PostProcessingContext context)
    {
        if (!_started)
        {
            Start(image);
            _started = true;
        }
        if (_inline)
        {
            Deliver(Process(new PageInFlight(image, context)));
            return;
        }
        // The drivers disagree about who owns the image once the callback returns -- WIA and Twain's
        // native transfer dispose it immediately afterwards, Twain's memory transfer and ESCL hand it
        // over -- so a page that outlives the callback has to be our own copy. It is a 7 ms memcpy for a
        // 300 dpi page against the 228 ms of work it buys a thread for. Unifying that contract across
        // four drivers, two of which cannot be tested here, is the change this deliberately is not.
        var owned = image.Clone();
        if (!_worker!.SendAsync(new PageInFlight(owned, context)).GetAwaiter().GetResult())
        {
            // The pipeline has faulted; the exception comes out of Finish. Nothing will process this
            // page, so nothing else will dispose it.
            owned.Dispose();
        }
    }

    /// <summary>
    /// Waits for every page still being processed and hands the last of them on. Rethrows whatever a page
    /// failed with, so a failure reaches the caller as it did when this ran inline.
    /// </summary>
    public async Task Finish()
    {
        if (_worker == null)
        {
            return;
        }
        _worker.Complete();
        try
        {
            await _sink!.Completion;
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
    }

    /// <summary>
    /// Lets the pipeline run down after the scan has already failed, so the pages in it are disposed
    /// rather than left to the finalizer. Whatever they fail with is dropped: the scan's own error is the
    /// one worth reporting.
    /// </summary>
    public async Task Drain()
    {
        if (_worker == null)
        {
            return;
        }
        _worker.Complete();
        try
        {
            await _sink!.Completion;
        }
        catch (Exception)
        {
            // Deliberately ignored; see above.
        }
    }

    private void Start(IMemoryImage firstPage)
    {
        var pageBytes = (long) firstPage.Width * firstPage.Height * BytesPerPixel(firstPage.PixelFormat);
        var parallelism = ChooseParallelism(pageBytes, _maxParallelism);
        if (parallelism <= 1)
        {
            _inline = true;
            _logger.LogDebug(
                "Post-processing one page at a time ({Cores} core(s), {PageMb} MB a page). This is what " +
                "a scan costs end to end, so a fast feeder will wait for it.",
                Environment.ProcessorCount, pageBytes / (1024 * 1024));
            return;
        }
        _logger.LogDebug(
            "Post-processing up to {Parallelism} page(s) at a time ({Cores} core(s), {PageMb} MB a page). " +
            "Pages are still handed on in scan order.",
            parallelism, Environment.ProcessorCount, pageBytes / (1024 * 1024));

        _worker = new TransformBlock<PageInFlight, PageDone>(Process, new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = parallelism,
            // Counts the pages being processed as well as those waiting, so this is the ceiling on how
            // many full-size pages are held at once.
            BoundedCapacity = parallelism + 1,
            EnsureOrdered = true
        });
        _sink = new ActionBlock<PageDone>(Deliver, new ExecutionDataflowBlockOptions
        {
            // The pages have to reach the caller one at a time and in order; what they carry by this
            // point is a ProcessedImage backed by a file, so holding a few costs nothing.
            MaxDegreeOfParallelism = 1,
            BoundedCapacity = 4
        });
        _worker.LinkTo(_sink, new DataflowLinkOptions { PropagateCompletion = true });
    }

    private static int ChooseParallelism(long pageBytes, int? requested)
    {
        var byCores = requested ?? Math.Max(Environment.ProcessorCount / 2, 1);
        // One less than the budget allows, because BoundedCapacity is parallelism + 1.
        var byMemory = Math.Max((int) (MEMORY_BUDGET_BYTES / Math.Max(pageBytes, 1)) - 1, 1);
        return Math.Min(Math.Min(byCores, byMemory), requested ?? MAX_PARALLELISM);
    }

    private static int BytesPerPixel(ImagePixelFormat format) => format switch
    {
        ImagePixelFormat.ARGB32 => 4,
        ImagePixelFormat.RGB24 => 3,
        ImagePixelFormat.Gray8 => 1,
        // A bilevel page is an eighth of a byte a pixel, but it is about to be read as one, so count it
        // as one rather than let a stack of them run at a parallelism nothing else is sized for.
        _ => 1
    };

    private PageDone Process(PageInFlight page)
    {
        return new PageDone(_postProcessor.PostProcess(page.Image, _options, page.Context), page.Context);
    }

    private void Deliver(PageDone page)
    {
        // A page detected as blank comes back null. It still passed through in its turn, so the ones
        // after it are not held up by it.
        if (page.Image != null)
        {
            _callback(page.Image, page.Context);
        }
    }
}
