using NAPS2.Images;

namespace NAPS2.PostScan;

/// <summary>
/// Works out which document each page belongs to after the pages in the window have been reordered --
/// which is how a page is moved from one document to another: you drag it there.
/// </summary>
/// <remarks>
/// Position decides, because that is what the operator can see. A document is a run of consecutive pages,
/// so a page dropped inside another document's run is a page of that document now, and the rule that
/// makes this readable rather than clever is that <b>the pages that moved adapt, and the pages that
/// stayed put do not</b>. Which pages moved is not guessed when it is known: a drop, and Move up/Move
/// down, say which pages they acted on, and those of them that changed place are the ones that adapt. The
/// rest -- interleaving, reversing, undo -- has to be read back out of the new order, which is the
/// complement of the longest run of pages still in their old order relative to each other, so moving one
/// page past ten others moves one page rather than eleven.
///
/// Pure on purpose -- no window, no queue -- because the rule is the part worth pinning down.
/// </remarks>
public static class DocumentPageAssignment
{
    /// <summary>
    /// The document each page belongs to after the reordering, in the order the pages are now in.
    /// </summary>
    /// <param name="previousOrder">The pages as they were before. Pages not in it are new, and new pages
    /// never adopt a neighbour's document -- a page just scanned belongs to the document the scan was
    /// split into, and an imported one belongs to none until it is dragged somewhere.</param>
    /// <param name="pages">The pages as they are now.</param>
    /// <param name="owners">Who owns what right now.</param>
    /// <param name="locked">Documents that must neither lose nor gain pages -- the finished ones, whose
    /// pages are already in the archive exactly as they are.</param>
    /// <param name="named">The pages the operator moved, when the command that did it knows which they
    /// were -- a drop, or moving the selection up or down. Null for every other kind of reordering,
    /// which has to be read from the order alone.</param>
    public static IReadOnlyList<ScannedDocument?> Normalize(
        IReadOnlyList<UiImage> previousOrder,
        IReadOnlyList<UiImage> pages,
        IReadOnlyDictionary<UiImage, ScannedDocument> owners,
        ISet<ScannedDocument> locked,
        IReadOnlyCollection<UiImage>? named = null)
    {
        var result = pages
            .Select(x => owners.TryGetValue(x, out var document) ? document : null)
            .ToArray();

        var moved = named != null
            ? PagesThatChangedPlace(previousOrder, pages, named)
            : MovedPages(previousOrder, pages);
        foreach (var index in moved.OrderBy(x => x))
        {
            var current = result[index];
            if (current != null && locked.Contains(current))
            {
                continue;
            }
            var neighbour = NearestSettledOwner(result, moved, index);
            if (neighbour != null && locked.Contains(neighbour))
            {
                // Joining a document that is already archived would say the archive contains this page.
                continue;
            }
            result[index] = neighbour;
        }

        MakeRunsContiguous(result, locked);
        return result;
    }

    /// <summary>
    /// The positions of the pages that actually went somewhere, out of the ones the command named.
    /// </summary>
    /// <remarks>
    /// A drop, and Move up/Move down, know which pages they acted on, so there is nothing to work out.
    /// Reading it back out of the new order instead is ambiguous for exactly the move that matters here:
    /// moving one page across a document boundary is explained equally well by the page next to it
    /// having gone the other way, and <see cref="LongestIncreasingSubsequence"/> then settles it in
    /// favour of whichever document has more pages. The page that stayed put ended up changing document
    /// while the one the operator moved kept its own -- which looks like two pages swapping places
    /// instead of one joining a document.
    ///
    /// Being named is not enough on its own, because a command can name a page and move it nowhere: a
    /// drop back onto the position it came from, Move up on the top page, Move down on the last one.
    /// Taking those at their word would hand a document's last page to the document above it for a
    /// gesture that did nothing at all -- and dropping a document back where it already sits is exactly
    /// what someone trying to merge two of them tries first.
    ///
    /// What counts is a page's place among the pages that were <i>not</i> named: those are the ones it
    /// can be said to have moved past, and the ones it takes its new document from. That also settles
    /// the case where every page was named -- there is nothing left to have moved past, so nothing is
    /// reassigned.
    /// </remarks>
    private static HashSet<int> PagesThatChangedPlace(IReadOnlyList<UiImage> previousOrder,
        IReadOnlyList<UiImage> pages, IReadOnlyCollection<UiImage> named)
    {
        var wasNamed = new HashSet<UiImage>(named);
        var before = PlaceAmongTheRest(previousOrder, wasNamed);
        var after = PlaceAmongTheRest(pages, wasNamed);
        var moved = new HashSet<int>();
        for (int i = 0; i < pages.Count; i++)
        {
            // A page that was not in the window before was just scanned or imported, and one of those
            // never adopts a neighbour whatever else is going on.
            if (wasNamed.Contains(pages[i]) && before.TryGetValue(pages[i], out var was) &&
                after[pages[i]] != was)
            {
                moved.Add(i);
            }
        }
        return moved;
    }

    /// <summary>
    /// For each named page, how many pages that were not named come before it.
    /// </summary>
    private static Dictionary<UiImage, int> PlaceAmongTheRest(IReadOnlyList<UiImage> order,
        HashSet<UiImage> named)
    {
        var places = new Dictionary<UiImage, int>();
        var rest = 0;
        foreach (var page in order)
        {
            if (named.Contains(page))
            {
                places[page] = rest;
            }
            else
            {
                rest++;
            }
        }
        return places;
    }

    /// <summary>
    /// The pages that changed position relative to the others: everything that is not part of the
    /// longest subsequence still in its old relative order.
    /// </summary>
    private static HashSet<int> MovedPages(IReadOnlyList<UiImage> previousOrder, IReadOnlyList<UiImage> pages)
    {
        var previousIndex = new Dictionary<UiImage, int>();
        for (int i = 0; i < previousOrder.Count; i++)
        {
            previousIndex[previousOrder[i]] = i;
        }

        var atIndex = new List<int>();
        var wasAtIndex = new List<int>();
        for (int i = 0; i < pages.Count; i++)
        {
            if (previousIndex.TryGetValue(pages[i], out var previous))
            {
                atIndex.Add(i);
                wasAtIndex.Add(previous);
            }
        }

        var stayed = LongestIncreasingSubsequence(wasAtIndex);
        var moved = new HashSet<int>();
        for (int i = 0; i < atIndex.Count; i++)
        {
            if (!stayed.Contains(i))
            {
                moved.Add(atIndex[i]);
            }
        }
        return moved;
    }

    /// <summary>
    /// The positions making up one longest increasing subsequence. Quadratic on purpose: a window holds
    /// pages, not records, and the straightforward version is the one that can be read.
    /// </summary>
    private static HashSet<int> LongestIncreasingSubsequence(List<int> values)
    {
        if (values.Count == 0)
        {
            return [];
        }
        var length = new int[values.Count];
        var previous = new int[values.Count];
        var best = 0;
        for (int i = 0; i < values.Count; i++)
        {
            length[i] = 1;
            previous[i] = -1;
            for (int j = 0; j < i; j++)
            {
                if (values[j] < values[i] && length[j] + 1 > length[i])
                {
                    length[i] = length[j] + 1;
                    previous[i] = j;
                }
            }
            if (length[i] > length[best])
            {
                best = i;
            }
        }
        var result = new HashSet<int>();
        for (int i = best; i >= 0; i = previous[i])
        {
            result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// The document of the nearest page that did not move -- the one before, or failing that the one
    /// after. A page dropped between two documents joins the one above it, because that is the document
    /// whose section it was dropped at the end of.
    /// </summary>
    private static ScannedDocument? NearestSettledOwner(
        ScannedDocument?[] owners, HashSet<int> moved, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (!moved.Contains(i))
            {
                return owners[i];
            }
        }
        for (int i = index + 1; i < owners.Length; i++)
        {
            if (!moved.Contains(i))
            {
                return owners[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Guarantees what the canvas draws: one run of consecutive pages per document. A document left with
    /// pages in several places keeps its longest run, and the others join whatever they now sit behind.
    /// </summary>
    /// <remarks>
    /// Left to right in one pass, always taking the owner already settled to the left, so it cannot
    /// cascade into a loop and cannot create a new gap: the run it merges into is the one directly
    /// before it.
    /// </remarks>
    private static void MakeRunsContiguous(ScannedDocument?[] owners, ISet<ScannedDocument> locked)
    {
        var runs = new List<(ScannedDocument? Owner, int Start, int End)>();
        for (int i = 0; i < owners.Length;)
        {
            int j = i;
            while (j + 1 < owners.Length && ReferenceEquals(owners[j + 1], owners[i]))
            {
                j++;
            }
            runs.Add((owners[i], i, j));
            i = j + 1;
        }

        var keep = new HashSet<int>();
        foreach (var group in runs
                     .Select((run, index) => (run, index))
                     .Where(x => x.run.Owner != null)
                     .GroupBy(x => x.run.Owner))
        {
            // The longest one is the document; the first on a tie, so the outcome does not depend on
            // which of two equal halves the enumeration happened to see first.
            var home = group
                .OrderByDescending(x => x.run.End - x.run.Start)
                .ThenBy(x => x.index)
                .First();
            keep.Add(home.index);
        }

        for (int r = 0; r < runs.Count; r++)
        {
            var (owner, start, end) = runs[r];
            if (owner == null || keep.Contains(r) || locked.Contains(owner))
            {
                continue;
            }
            var target = start > 0 ? owners[start - 1] : end + 1 < owners.Length ? owners[end + 1] : null;
            if (target != null && locked.Contains(target))
            {
                continue;
            }
            for (int i = start; i <= end; i++)
            {
                owners[i] = target;
            }
        }
    }
}
