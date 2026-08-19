using NAPS2.Images;
using NAPS2.Scan;

namespace NAPS2.PostScan;

/// <summary>
/// Splitting a document in two and merging one back into the one before it, from the canvas.
/// </summary>
/// <remarks>
/// This is what a missed separator sheet costs to repair. Dragging pages one at a time would do the same
/// job, but a stack whose separator was not read splits in one place and is put right in one action --
/// and the same is true the other way round when a barcode was read that should not have separated
/// anything.
///
/// A document produced here gets its barcodes and its identification through
/// <see cref="DocumentPipeline.AttachBarcodes"/>, the same method the scan itself uses, so a document
/// created by splitting is filed under the value the profile's regex accepts exactly like one the
/// separator produced. Deriving it a second way here is what would let the two drift apart.
/// </remarks>
public class DocumentEditor
{
    private readonly DocumentQueue _queue;
    private readonly DocumentPageTracker _pageTracker;
    private readonly UiImageList _imageList;

    public DocumentEditor(DocumentQueue queue, DocumentPageTracker pageTracker, UiImageList imageList)
    {
        _queue = queue;
        _pageTracker = pageTracker;
        _imageList = imageList;
    }

    /// <summary>
    /// The page a document operation acts on: the first of the selected ones in page order, since the
    /// selection is a set and "here" means the topmost place it covers.
    /// </summary>
    private UiImage? Anchor(IEnumerable<UiImage> selection)
    {
        var order = _imageList.Images;
        return selection
            .Where(order.Contains)
            .OrderBy(order.IndexOf)
            .FirstOrDefault();
    }

    public bool CanSplitAt(IEnumerable<UiImage> selection)
    {
        var page = Anchor(selection);
        if (page == null)
        {
            return false;
        }
        var document = _pageTracker.DocumentFor(page);
        // Splitting at the first page would produce a document with nothing in it and leave the other
        // one exactly as it was.
        return document is { IsFiledRemotely: false, Status: not DocumentStatus.Working } &&
               document.WindowPages is { } pages && pages.Count > 1 && !ReferenceEquals(pages[0], page);
    }

    /// <summary>
    /// Makes the anchor page the first page of a new document, which takes everything from there to the
    /// end of the one it was in.
    /// </summary>
    public void SplitAt(IEnumerable<UiImage> selection)
    {
        if (!CanSplitAt(selection))
        {
            return;
        }
        var page = Anchor(selection)!;
        var document = _pageTracker.DocumentFor(page)!;
        var pages = document.WindowPages!.ToList();
        var at = pages.IndexOf(page);
        var tail = pages.Skip(at).ToList();

        var split = new ScannedDocument
        {
            Profile = document.Profile,
            ScannedPages = [],
            SequenceIndex = document.SequenceIndex + 1,
            Timestamp = document.Timestamp
        };
        // Pointed straight at the pages, which are in the window already: there is no scan coming for it
        // to take them over from.
        split.SetWindowPages(tail);
        Reidentify(split);

        document.SetWindowPages(pages.Take(at).ToList());
        Reidentify(document);
        _queue.InsertAfter(document, split);
        ScanConsole.Document(
            $"Split after page {at} of {document.Describe()}: {split.PageCount} page(s) became " +
            $"{split.Describe()}.");
        _pageTracker.Sync();
    }

    public bool CanMergeWithPrevious(IEnumerable<UiImage> selection) => MergeTarget(selection) != null;

    /// <summary>
    /// Gives the anchor page's document to the document directly above it, so what the separator split
    /// in two is one document again.
    /// </summary>
    public void MergeWithPrevious(IEnumerable<UiImage> selection)
    {
        var target = MergeTarget(selection);
        if (target == null)
        {
            return;
        }
        var (previous, document) = target.Value;
        var merged = previous.WindowPages!.Concat(document.WindowPages!).ToList();
        previous.SetWindowPages(merged);
        Reidentify(previous);
        ScanConsole.Document(
            $"{document.Describe()} was merged into {previous.Describe()}, which now has " +
            $"{previous.PageCount} page(s).");
        // Removing disposes it, which discards its staged file. A file in the operator's own folder is
        // theirs and is left alone, the same as everywhere else.
        _queue.Remove(document);
        _pageTracker.Sync();
    }

    /// <summary>
    /// Re-reads the barcodes a document's pages carry and, unless the operator typed the value in
    /// themselves, the value it is filed under.
    /// </summary>
    /// <remarks>
    /// The identification has to follow the pages: split a document and the half that keeps the cover
    /// sheet keeps the order number, while the half without it must not go on claiming a barcode that
    /// left with the other half. Nothing is decoded here -- the barcodes were read when the pages were
    /// scanned, and this only picks from them the way the scan did. A value entered by hand outlives all
    /// of it, because it was a correction of exactly this.
    /// </remarks>
    private static void Reidentify(ScannedDocument document)
    {
        var typedByHand = document.IdentifierSource == DocumentBarcodeSource.Manual
            ? document.Identifier
            : null;
        DocumentPipeline.AttachBarcodes(document, document.Workflow, null);
        if (typedByHand != null)
        {
            document.SetIdentifier(typedByHand, DocumentBarcodeSource.Manual);
        }
    }

    /// <summary>
    /// The document above the anchor's document and the anchor's document, if merging the two is
    /// something that can be done at all.
    /// </summary>
    private (ScannedDocument Previous, ScannedDocument Document)? MergeTarget(IEnumerable<UiImage> selection)
    {
        var page = Anchor(selection);
        if (page == null)
        {
            return null;
        }
        var document = _pageTracker.DocumentFor(page);
        if (document is not { IsFiledRemotely: false, Status: not DocumentStatus.Working } ||
            document.WindowPages is not { Count: > 0 })
        {
            return null;
        }
        var order = _imageList.Images;
        var first = order.IndexOf(document.WindowPages[0]);
        if (first <= 0)
        {
            return null;
        }
        var previous = _pageTracker.DocumentFor(order[first - 1]);
        if (previous == null || ReferenceEquals(previous, document) || previous.IsFiledRemotely ||
            previous.Status == DocumentStatus.Working ||
            !ReferenceEquals(previous.Profile, document.Profile))
        {
            // Not a document, already archived, or filed by a different profile -- which decides the
            // folder, the name and the archive, so merging across one would change all three silently.
            return null;
        }
        return (previous, document);
    }
}
