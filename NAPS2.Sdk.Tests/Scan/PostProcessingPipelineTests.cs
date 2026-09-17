using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NAPS2.Scan;
using NAPS2.Scan.Internal;
using Xunit;

namespace NAPS2.Sdk.Tests.Scan;

/// <summary>
/// The pipeline spreads the post-processing of a scan over cores. What has to survive that is not
/// throughput but the two things everything downstream assumes: that the pages arrive in the order they
/// were scanned, and that a page is still there to be processed after the driver's callback has returned.
/// </summary>
/// <remarks>
/// Every test here forces the parallel path rather than letting the machine's cores decide. Without that
/// a two-core build agent would run each page inline, every property below would hold trivially, and the
/// suite would report that a pipeline it never exercised is correct.
/// </remarks>
public class PostProcessingPipelineTests : ContextualTests
{
    private const int Parallelism = 4;

    /// <summary>
    /// Pages finish in a deliberately scrambled order -- the first one takes longest -- and still have to
    /// be handed on in the order they were scanned. Out of order, a stack separates into the wrong
    /// documents and the wrong pages are archived under the wrong barcode.
    /// </summary>
    [Fact]
    public async Task PagesAreHandedOnInScanOrder()
    {
        var delivered = new List<int>();
        var processor = new FakePostProcessor(ScanningContext, context =>
        {
            // The earlier the page, the longer it takes, so finishing order is the reverse of scan order.
            Thread.Sleep(80 - context.PageNumber * 5);
        });
        var pipeline = NewPipeline(processor, (_, context) => delivered.Add(context.PageNumber));

        for (var i = 1; i <= 12; i++)
        {
            using var page = CreatePage();
            pipeline.Submit(page, new PostProcessingContext { PageNumber = i });
        }
        await pipeline.Finish();

        Assert.Equal(Enumerable.Range(1, 12), delivered);
    }

    /// <summary>
    /// The driver may dispose the page the moment the callback returns -- WIA and Twain's native transfer
    /// both do -- so the pipeline has to be holding its own copy by then. Without one this fails with the
    /// page already disposed, which in the app would be a scan that dies partway through a stack.
    /// </summary>
    [Fact]
    public async Task APageOutlivesTheDriverCallbackThatDeliveredIt()
    {
        var seen = 0;
        var processor = new FakePostProcessor(ScanningContext, _ => Thread.Sleep(30))
        {
            // Reading the page is what proves it is still there; a disposed one throws here.
            ReadsTheImage = true
        };
        var pipeline = NewPipeline(processor, (_, _) => Interlocked.Increment(ref seen));

        for (var i = 1; i <= 8; i++)
        {
            // Exactly what WiaScanDriver does: using (image) { callback(image); }
            using (var page = CreatePage())
            {
                pipeline.Submit(page, new PostProcessingContext { PageNumber = i });
            }
        }
        await pipeline.Finish();

        Assert.Equal(8, seen);
    }

    /// <summary>
    /// A page detected as blank is dropped, and the pages after it must not be held up or reordered by
    /// the gap it leaves.
    /// </summary>
    [Fact]
    public async Task ADroppedPageLeavesTheRestInOrder()
    {
        var delivered = new List<int>();
        var processor = new FakePostProcessor(ScanningContext, _ => Thread.Sleep(10))
        {
            // Stand-in for blank detection, which returns null for a page it excludes.
            DropPage = context => context.PageNumber % 3 == 0
        };
        var pipeline = NewPipeline(processor, (_, context) => delivered.Add(context.PageNumber));

        for (var i = 1; i <= 10; i++)
        {
            using var page = CreatePage();
            pipeline.Submit(page, new PostProcessingContext { PageNumber = i });
        }
        await pipeline.Finish();

        Assert.Equal(new[] { 1, 2, 4, 5, 7, 8, 10 }, delivered);
    }

    /// <summary>
    /// A page that fails has to reach the caller as the exception it was, not as something wrapped in
    /// Dataflow's own. The scan's error handling matches on the type.
    /// </summary>
    [Fact]
    public async Task AFailedPageThrowsItsOwnException()
    {
        var processor = new FakePostProcessor(ScanningContext, context =>
        {
            if (context.PageNumber == 3) throw new InvalidOperationException("page 3 is bad");
        });
        var pipeline = NewPipeline(processor, (_, _) => { });

        for (var i = 1; i <= 6; i++)
        {
            using var page = CreatePage();
            pipeline.Submit(page, new PostProcessingContext { PageNumber = i });
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.Finish());
        Assert.Equal("page 3 is bad", ex.Message);
    }

    /// <summary>
    /// The scanner has to wait when the pipeline is full, or a feeder faster than the processing fills
    /// memory with a page every time round. A 600 dpi page is 104 MB, so this is the difference between
    /// a few hundred megabytes and however many pages are in the hopper.
    /// </summary>
    [Fact]
    public async Task NoMorePagesAreHeldAtOnceThanTheBoundAllows()
    {
        var inFlight = 0;
        var highWater = 0;
        var processor = new FakePostProcessor(ScanningContext, _ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref highWater, now);
            Thread.Sleep(25);
            Interlocked.Decrement(ref inFlight);
        });
        var pipeline = NewPipeline(processor, (_, _) => { });

        for (var i = 1; i <= 40; i++)
        {
            using var page = CreatePage();
            pipeline.Submit(page, new PostProcessingContext { PageNumber = i });
        }
        await pipeline.Finish();

        // Submit blocks once the block is full, so what is being worked on at once cannot exceed the
        // parallelism however fast the pages are offered.
        Assert.InRange(highWater, 2, Parallelism);
    }

    /// <summary>
    /// A scan that produced nothing must not leave anything waiting to be awaited.
    /// </summary>
    [Fact]
    public async Task AScanWithNoPagesFinishes()
    {
        var pipeline = NewPipeline(new FakePostProcessor(ScanningContext, _ => { }), (_, _) => { });
        await pipeline.Finish();
        await pipeline.Drain();
    }

    private PostProcessingPipeline NewPipeline(IRemotePostProcessor processor,
        Action<ProcessedImage, PostProcessingContext> callback) =>
        new(processor, new ScanOptions(), callback, NullLogger.Instance, Parallelism);

    private IMemoryImage CreatePage() => ImageContext.Create(64, 64, ImagePixelFormat.RGB24);

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }

    private class FakePostProcessor(ScanningContext scanningContext, Action<PostProcessingContext> work)
        : IRemotePostProcessor
    {
        public bool ReadsTheImage { get; init; }
        public Func<PostProcessingContext, bool> DropPage { get; init; }

        public ProcessedImage PostProcess(IMemoryImage image, ScanOptions options,
            PostProcessingContext postProcessingContext)
        {
            if (ReadsTheImage)
            {
                // Touching the pixels is what a disposed page fails on; reading Width alone would not.
                using var lockState = image.Lock(LockMode.ReadOnly, out var data);
                _ = data.h;
            }
            work(postProcessingContext);
            if (DropPage?.Invoke(postProcessingContext) == true)
            {
                image.Dispose();
                return null;
            }
            var result = scanningContext.CreateProcessedImage(image.Clone());
            image.Dispose();
            return result;
        }
    }
}
