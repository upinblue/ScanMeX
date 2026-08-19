using System.Runtime.CompilerServices;
using NAPS2.Images;

namespace NAPS2.PostScan;

/// <summary>
/// Keeps every document's pages in step with the pages in the window: a page deleted, reordered or
/// edited there is a page deleted, reordered or edited in the document that will be archived.
/// </summary>
/// <remarks>
/// A document used to be a frozen copy of the pages it was split from, and the only thing connecting it
/// to the window was that the two held images with the same storage and the same transforms. That is
/// exactly what stops being true the moment anyone straightens a page: the window's copy grows a
/// transform, the document's does not, and the two silently part company -- the archived file kept
/// showing the raw scan, and rotating every page of a document made it look, to the window, as if all of
/// its pages had been deleted.
///
/// Here the document is pointed at the window's own page objects instead, which survive being edited
/// because that is what they are for. It only happens once every page of a document has arrived: a
/// document exists the moment the scan is split, while its pages are still on their way into the window,
/// and a document pointed at half of itself would write half a document.
/// </remarks>
public class DocumentPageTracker : IDisposable
{
    private readonly UiImageList _imageList;
    private readonly DocumentQueue _queue;

    /// <summary>
    /// What each page in the window carried when it first appeared there. An edit replaces that
    /// instance, so the value a page arrived with is the one thing it still has in common with the
    /// document that produced it.
    /// </summary>
    private readonly Dictionary<UiImage, ProcessedImage> _asInserted = new();

    /// <summary>
    /// Which document each page in the window belongs to. Kept here rather than asked of every document
    /// in turn, because the canvas needs the answer for every page it draws.
    /// </summary>
    private Dictionary<UiImage, ScannedDocument> _owners = new();

    /// <summary>
    /// The order the pages were in at the last sync. What moved since is what decides where a page
    /// belongs now; see <see cref="DocumentPageAssignment"/>.
    /// </summary>
    private IReadOnlyList<UiImage> _previousOrder = [];

    private readonly object _lock = new();
    private bool _syncing;

    public DocumentPageTracker(UiImageList imageList, DocumentQueue queue)
    {
        _imageList = imageList;
        _queue = queue;
        _imageList.ImagesUpdated += OnImagesUpdated;
    }

    private void OnImagesUpdated(object? sender, ImageListEventArgs e) => Sync();

    /// <summary>
    /// The document a page in the window belongs to, or null for one that belongs to none -- an imported
    /// page, or one left over from a document that has been discarded.
    /// </summary>
    public ScannedDocument? DocumentFor(UiImage image) =>
        _owners.TryGetValue(image, out var document) ? document : null;

    /// <summary>
    /// Brings every document in line with what the window holds. Called on every change to the image
    /// list, and by the pipeline once a scan has been split -- by which time the pages are usually
    /// already in the window, so nothing else would announce them.
    /// </summary>
    public void Sync()
    {
        var dropped = new List<ScannedDocument>();
        bool changed;
        lock (_lock)
        {
            if (_syncing)
            {
                // Removing a document notifies the queue, which can come back round to here.
                return;
            }
            _syncing = true;
            try
            {
                changed = SyncCore(dropped);
            }
            finally
            {
                _syncing = false;
            }
        }
        foreach (var document in dropped)
        {
            _queue.Remove(document);
        }
        if (changed && dropped.Count == 0)
        {
            _queue.NotifyChanged();
        }
    }

    private bool SyncCore(List<ScannedDocument> dropped)
    {
        var images = _imageList.Images;
        var present = new HashSet<UiImage>(images);
        var order = new Dictionary<UiImage, int>();
        for (int i = 0; i < images.Count; i++)
        {
            order[images[i]] = i;
            if (!_asInserted.ContainsKey(images[i]))
            {
                _asInserted[images[i]] = images[i].GetImageWeakReference().ProcessedImage;
            }
        }
        foreach (var gone in _asInserted.Keys.Where(x => !present.Contains(x)).ToList())
        {
            _asInserted.Remove(gone);
        }

        var changed = false;
        foreach (var document in _queue.Documents)
        {
            // Finished ones too: a profile that only files locally finishes a document the moment it is
            // written, which can be before its pages have reached the window, and a document that never
            // took them over would leave its own pages drawn as belonging to nothing.
            if (document.HasAdoptedWindowPages)
            {
                continue;
            }
            changed |= TryAdopt(document, images, order);
        }

        // Who owns what before the reordering is taken into account.
        var before = new Dictionary<UiImage, ScannedDocument>();
        var beforeCounts = new Dictionary<ScannedDocument, int>();
        foreach (var document in _queue.Documents)
        {
            foreach (var page in (document.WindowPages ?? []).Where(present.Contains))
            {
                before[page] = document;
            }
            beforeCounts[document] = document.PageCount;
        }
        // Documents that reached an archive neither lose nor gain pages: they are the record that exactly
        // those pages are in there. Being finished is not enough -- a save-only profile finishes a
        // document as soon as it is written, and locking those would mean nobody who uploads nowhere
        // could ever edit a page again.
        // Being written or uploaded right now counts too: its pages are in use at this moment.
        var locked = _queue.Documents
            .Where(x => x.IsFiledRemotely || x.Status == DocumentStatus.Working)
            .ToHashSet();

        var assignment = DocumentPageAssignment.Normalize(_previousOrder, images, before, locked);
        var pagesByDocument = new Dictionary<ScannedDocument, List<UiImage>>();
        for (int i = 0; i < images.Count; i++)
        {
            if (assignment[i] is { } document)
            {
                pagesByDocument.GetOrSet(document, () => []).Add(images[i]);
            }
        }

        foreach (var document in _queue.Documents)
        {
            if (locked.Contains(document) || !document.HasAdoptedWindowPages)
            {
                continue;
            }
            var pages = pagesByDocument.Get(document) ?? [];
            if (pages.Count == 0)
            {
                if (document.Status == DocumentStatus.Done)
                {
                    // Already filed. The queue entry is the record that it was, and clearing the window
                    // is the normal way to start the next batch.
                    continue;
                }
                ScanConsole.Document(
                    $"{document.Describe()}: it has no pages left in the window, so the document is gone " +
                    "too.");
                dropped.Add(document);
                changed = true;
                continue;
            }
            if (document.SetWindowPages(pages))
            {
                changed = true;
                ReportChange(document, beforeCounts.Get(document), pages.Count);
                if (document.Status == DocumentStatus.Done)
                {
                    // It was filed, and the file no longer shows what the document contains. Pending
                    // again, so the upload button has something to do and files it as it now stands.
                    document.Status = DocumentStatus.Pending;
                    ScanConsole.Document(
                        $"{document.Describe()}: it had been filed already; press upload to file it " +
                        "again as it now stands.");
                }
            }
        }
        _previousOrder = images;

        // Rebuilt rather than patched: it has to agree with what the documents say afterwards, and a
        // stale entry here would draw a page into the wrong section of the canvas.
        var owners = new Dictionary<UiImage, ScannedDocument>();
        foreach (var document in _queue.Documents)
        {
            foreach (var page in document.WindowPages ?? [])
            {
                if (present.Contains(page))
                {
                    owners[page] = document;
                }
            }
        }
        changed |= owners.Count != _owners.Count ||
                   owners.Any(x => !_owners.TryGetValue(x.Key, out var d) || d != x.Value);
        _owners = owners;
        return changed;
    }

    /// <summary>
    /// Says what happened to a document's pages, and whether that leaves its file out of date.
    /// </summary>
    /// <remarks>
    /// A page leaving a document is not distinguishable here from a page being deleted -- both are "it
    /// is not ours any more" -- so the line names both possibilities rather than picking one and being
    /// wrong half the time.
    /// </remarks>
    private static void ReportChange(ScannedDocument document, int before, int after)
    {
        ScanConsole.Document(after switch
        {
            _ when after < before =>
                $"{document.Describe()}: {before - after} page(s) left it -- deleted from the window or " +
                $"moved to another document -- leaving {after}.",
            _ when after > before =>
                $"{document.Describe()}: {after - before} page(s) were moved into it, giving it {after}.",
            _ => $"{document.Describe()}: its pages changed in the window."
        });
        if (document.SavedPath != null)
        {
            ScanConsole.Document(
                $"{document.Describe()}: '{document.FileName}' no longer shows what the document " +
                "contains, so it is written again the next time the document is filed.");
        }
    }

    /// <summary>
    /// Points a document at the window's page objects, but only once every one of its pages is there.
    /// </summary>
    private bool TryAdopt(ScannedDocument document, IReadOnlyList<UiImage> images,
        Dictionary<UiImage, int> order)
    {
        if (document.WindowPageCandidates.Count == 0)
        {
            // Nothing was ever handed to a window -- the command line scanner. The document keeps the
            // scan's own copies for good, which is what it is written from there.
            return false;
        }
        var wanted = new HashSet<ProcessedImage>(document.WindowPageCandidates, SameInstance.Comparer);
        var found = images.Where(x => wanted.Contains(_asInserted[x])).OrderBy(x => order[x]).ToList();
        if (found.Count < document.WindowPageCandidates.Count)
        {
            // Still arriving. Saying nothing here is deliberate: this is true for a fraction of a second
            // after every scan, and a console line per page on the way in would bury the scan itself.
            return false;
        }
        document.SetWindowPages(found);
        ScanConsole.Document(
            $"{document.Describe()}: its {found.Count} page(s) in the window are now the document's own, " +
            "so corrections made there reach the archived file.");
        return true;
    }

    public void Dispose()
    {
        _imageList.ImagesUpdated -= OnImagesUpdated;
    }

    /// <summary>
    /// Reference identity. <see cref="ProcessedImage"/> compares by storage and transforms, which two
    /// clones of the same page share -- exactly the case that has to be told apart here.
    /// </summary>
    public sealed class SameInstance : IEqualityComparer<ProcessedImage>
    {
        public static readonly IEqualityComparer<ProcessedImage> Comparer = new SameInstance();

        public bool Equals(ProcessedImage? x, ProcessedImage? y) => ReferenceEquals(x, y);

        public int GetHashCode(ProcessedImage obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
