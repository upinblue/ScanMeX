#nullable enable
using NAPS2.Images;
using NAPS2.PostScan;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// Where a page belongs after it has been dragged somewhere else. This is the rule the whole business of
/// moving pages between documents rests on, so it is checked on its own: no window, no queue, just the
/// order the pages are in and who owned them before.
/// </summary>
public class DocumentPageAssignmentTests : ContextualTests
{
    private readonly ScannedDocument _a;
    private readonly ScannedDocument _b;
    private readonly List<UiImage> _pages;

    public DocumentPageAssignmentTests()
    {
        var profile = new ScanProfile { DisplayName = "Test" };
        _a = new ScannedDocument { Profile = profile, ScannedPages = [], SequenceIndex = 0 };
        _b = new ScannedDocument { Profile = profile, ScannedPages = [], SequenceIndex = 1 };
        // a0 a1 a2 | b0 b1
        _pages = CreateScannedImages(
                ImageResources.dog, ImageResources.dog_gray, ImageResources.dog,
                ImageResources.dog_gray, ImageResources.dog)
            .Select(x => new UiImage(x))
            .ToList();
    }

    private Dictionary<UiImage, ScannedDocument> Owners(params (int Index, ScannedDocument Document)[] owners) =>
        owners.ToDictionary(x => _pages[x.Index], x => x.Document);

    private Dictionary<UiImage, ScannedDocument> StartingOwners() =>
        Owners((0, _a), (1, _a), (2, _a), (3, _b), (4, _b));

    private static List<UiImage> Order(List<UiImage> pages, params int[] indexes) =>
        indexes.Select(i => pages[i]).ToList();

    [Fact]
    public void NothingMovedChangesNothing()
    {
        var order = Order(_pages, 0, 1, 2, 3, 4);

        var result = DocumentPageAssignment.Normalize(order, order, StartingOwners(), new HashSet<ScannedDocument>());

        Assert.Equal([_a, _a, _a, _b, _b], result);
    }

    [Fact]
    public void APageDraggedIntoAnotherDocumentJoinsIt()
    {
        var before = Order(_pages, 0, 1, 2, 3, 4);
        // a1 is dropped between b0 and b1.
        var after = Order(_pages, 0, 2, 3, 1, 4);

        var result = DocumentPageAssignment.Normalize(before, after, StartingOwners(), new HashSet<ScannedDocument>());

        Assert.Equal([_a, _a, _b, _b, _b], result);
    }

    [Fact]
    public void APageDraggedWithinItsOwnDocumentStaysWhereItBelongs()
    {
        var before = Order(_pages, 0, 1, 2, 3, 4);
        var after = Order(_pages, 2, 0, 1, 3, 4);

        var result = DocumentPageAssignment.Normalize(before, after, StartingOwners(), new HashSet<ScannedDocument>());

        Assert.Equal([_a, _a, _a, _b, _b], result);
    }

    [Fact]
    public void DraggingOneDocumentsPageIntoAnotherLeavesBothContiguous()
    {
        // b0 is dropped into the middle of A. Both documents have to come out as one run each,
        // otherwise the canvas would draw A in two pieces under the same heading.
        var before = Order(_pages, 0, 1, 2, 3, 4);
        var after = Order(_pages, 0, 3, 1, 2, 4);

        var result = DocumentPageAssignment.Normalize(before, after, StartingOwners(), new HashSet<ScannedDocument>());

        Assert.Equal([_a, _a, _a, _a, _b], result);
    }

    [Fact]
    public void APageWithNoDocumentJoinsTheOneItIsDroppedInto()
    {
        // The imported page at the end is dragged into the middle of A.
        var owners = StartingOwners();
        var before = Order(_pages, 0, 1, 2, 3, 4);
        var withoutOwner = new Dictionary<UiImage, ScannedDocument>(owners);
        withoutOwner.Remove(_pages[4]);
        var after = Order(_pages, 0, 4, 1, 2, 3);

        var result = DocumentPageAssignment.Normalize(before, after, withoutOwner, new HashSet<ScannedDocument>());

        Assert.Equal([_a, _a, _a, _a, _b], result);
    }

    [Fact]
    public void ThePagesOfAFinishedDocumentAreNeverGivenAway()
    {
        _b.Status = DocumentStatus.Done;
        var before = Order(_pages, 0, 1, 2, 3, 4);
        // b0 is dragged into A's run anyway -- the guard in the window refuses this, and the rule has
        // to hold even if something gets past it.
        var after = Order(_pages, 0, 3, 1, 2, 4);

        var result = DocumentPageAssignment.Normalize(before, after, StartingOwners(), new HashSet<ScannedDocument> { _b });

        Assert.Equal(_b, result[1]);
        Assert.Equal(_b, result[4]);
    }

    [Fact]
    public void AFinishedDocumentNeverGainsAPage()
    {
        _b.Status = DocumentStatus.Done;
        var before = Order(_pages, 0, 1, 2, 3, 4);
        var after = Order(_pages, 0, 2, 3, 1, 4);

        var result = DocumentPageAssignment.Normalize(before, after, StartingOwners(), new HashSet<ScannedDocument> { _b });

        Assert.DoesNotContain(_b, result.Where((_, i) => i != 2 && i != 4));
        Assert.Equal(2, result.Count(x => x == _b));
    }

    [Fact]
    public void EveryDocumentEndsUpAsOneRun()
    {
        // A wholesale reordering -- reversing everything -- must still leave something the canvas can
        // draw, even though which document a page ends up in is not meaningful here.
        var before = Order(_pages, 0, 1, 2, 3, 4);
        var after = Order(_pages, 4, 3, 2, 1, 0);

        var result = DocumentPageAssignment.Normalize(before, after, StartingOwners(), new HashSet<ScannedDocument>());

        var runs = 0;
        for (int i = 0; i < result.Count; i++)
        {
            if (i == 0 || !ReferenceEquals(result[i], result[i - 1]))
            {
                runs++;
            }
        }
        Assert.Equal(result.Where(x => x != null).Distinct().Count(), runs);
    }
}
