using System.Threading;

namespace NAPS2.Images;

/// <summary>
/// Sends changes in the given UiImageList to the given callback when images are updated. Passive updates (e.g.
/// receiving the next image from a scan/import) are throttled to avoid excessive UI updates.
///
/// If any images are present in the list upon construction, they will be immediately sent to the callback. 
/// </summary>
public class ImageListSyncer
{
    private static readonly TimeSpan SyncThrottleInterval = TimeSpan.FromMilliseconds(200);

    private readonly UiImageList _imageList;
    private readonly Action<ListViewDiffs<UiImage>, IReadOnlyList<UiImage>> _diffCallback;
    private readonly SynchronizationContext _syncContext;
    private readonly TimedThrottle _syncThrottle;
    private readonly ImageListDiffer _differ;
    private bool _disposed;
    private bool _syncing;

    public ImageListSyncer(UiImageList imageList,
        Action<ListViewDiffs<UiImage>, IReadOnlyList<UiImage>> diffCallback,
        SynchronizationContext syncContext)
    {
        _imageList = imageList;
        _diffCallback = diffCallback;
        _syncContext = syncContext;
        _imageList.ImagesUpdated += ImagesUpdated;
        _imageList.ImagesThumbnailChanged += ImagesUpdated;
        _imageList.ImagesThumbnailInvalidated += ImagesUpdated;
        _syncThrottle = new TimedThrottle(Sync, SyncThrottleInterval);
        _differ = new ImageListDiffer(_imageList);
        Sync();
    }

    private void ImagesUpdated(object? sender, ImageListEventArgs e)
    {
        if (e.IsPassiveInteraction)
        {
            _syncThrottle.RunAction(_syncContext);
        }
        else
        {
            _syncThrottle.RunActionNow(_syncContext);
        }
    }

    /// <summary>
    /// The pages the view is up to date with -- what it holds once the changes handed to the callback
    /// have been applied. Anything addressing its items by position belongs on this list rather than on
    /// the image list, which runs ahead of it while a scan comes in.
    /// </summary>
    public IReadOnlyList<UiImage> CurrentPages => _differ.CurrentPages;

    /// <summary>
    /// Hands over any changes still waiting on the throttle, now, on the calling thread -- which has to
    /// be the thread the callback otherwise runs on.
    /// </summary>
    /// <remarks>
    /// For the moments where something other than the image list has to act on what the view holds --
    /// a scan being split into documents, which then head the pages by position. Waiting out the
    /// throttle there would mean working out those positions against a view that is a page or two
    /// behind.
    ///
    /// Everywhere else the callback reaches the view by being posted to the sync context, so it only
    /// ever runs on the one thread; calling this from another would be the first time two of them could
    /// overlap, and the view throws rather than being updated twice at once.
    /// </remarks>
    public void Flush() => _syncThrottle.RunActionNow(null);

    private void Sync()
    {
        if (_syncing)
        {
            // Applying a diff can come back round to here through the view's own events. The view is
            // mid-update and already has everything this call would hand it; whatever changed during it
            // announces itself again and arrives on the next run.
            return;
        }
        _syncing = true;
        try
        {
            var diffs = _differ.GetAndFlushDiffs();
            if (diffs.HasAnyDiff)
            {
                _diffCallback(diffs, _differ.CurrentPages);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    public void Dispose()
    {
        lock (this)
        {
            if (_disposed) return;
            _disposed = true;
            _imageList.ImagesUpdated -= ImagesUpdated;
            _imageList.ImagesThumbnailChanged -= ImagesUpdated;
            _imageList.ImagesThumbnailInvalidated -= ImagesUpdated;
        }
    }
}