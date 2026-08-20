#nullable enable
using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
using NAPS2.Images;
using NAPS2.Pdf;
using NAPS2.PostScan;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using NAPS2.Sdk.Tests.Asserts;
using NSubstitute;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// What happens to a document when its pages are worked on in the window. A document used to be a frozen
/// copy of the pages it was split from, so an edit reached the window and nothing else: the archived file
/// still showed the raw scan, and rotating every page of a document made it look as though all of its
/// pages had been deleted, which took the document out of the list.
/// </summary>
public class DocumentPageTrackerTests : ContextualTests
{
    private readonly ErrorOutput _errorOutput = Substitute.For<ErrorOutput>();
    private readonly ISaveNotify _notify = Substitute.For<ISaveNotify>();
    private readonly DocumentQueue _queue = new();
    private readonly UiImageList _imageList = new();
    private readonly DocumentPageTracker _pageTracker;

    public DocumentPageTrackerTests()
    {
        _pageTracker = new DocumentPageTracker(_imageList, _queue);
    }

    private DocumentPipeline CreatePipeline() => new(
        _errorOutput,
        _notify,
        _imageList,
        _queue,
        new DocumentWriter(
            new PdfExporter(ScanningContext),
            Substitute.For<IOverwritePrompt>(),
            Substitute.For<OperationProgress>(),
            Naps2Config.Stub(),
            ImageContext,
            Substitute.For<DialogHelper>()),
        Substitute.For<DocumentUploadService>(Naps2Config.Stub(), Substitute.For<OperationProgress>(),
            Substitute.For<ISaveNotify>()),
        _pageTracker);

    [Fact]
    public async Task RotatingEveryPageKeepsTheDocument()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog, ImageResources.dog_gray);

        RotateEverything();

        var document = Assert.Single(_queue.Documents);
        Assert.Equal(2, document.PageCount);
    }

    [Fact]
    public async Task DeletingEveryPageDropsADocumentThatHasNotGoneAnywhere()
    {
        // A document waiting for the upload button is nothing without its pages. One that has already
        // been filed is a different matter -- see AFinishedDocumentSurvivesItsPagesBeingCleared.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, WaitingProfile(), ImageResources.dog, ImageResources.dog_gray);

        DeletePages(_imageList.Images.ToList());

        Assert.Empty(_queue.Documents);
    }

    [Fact]
    public async Task PagesStillOnTheirWayDoNotDropTheDocument()
    {
        // A document exists the moment the scan is split, while its pages are still on their way into
        // the window. "This document has no page in the list" is therefore true for a fraction of a
        // second after every scan, and must not be read as a deletion.
        var pipeline = CreatePipeline();
        await pipeline.Process(Profile("test$(n).pdf"), CreateScannedImages(ImageResources.dog).ToAsyncEnumerable())
            .ToListAsync();

        var document = Assert.Single(_queue.Documents);
        Assert.Equal(1, document.PageCount);
        Assert.False(document.HasAdoptedWindowPages);
    }

    [Fact]
    public async Task DeletingAPageChangesWhatIsWritten()
    {
        // The whole point of the exercise: the file that reaches the archive is what the operator is
        // looking at, not what came off the scanner.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog, ImageResources.dog_gray);
        var document = Assert.Single(_queue.Documents);
        PdfAsserts.AssertPageCount(2, document.SavedPath!);

        DeletePages([_imageList.Images[0]]);
        await pipeline.Advance(document, triggeredByOperator: true);

        Assert.Equal(1, document.PageCount);
        PdfAsserts.AssertPageCount(1, document.SavedPath!);
        PdfAsserts.AssertImages(document.SavedPath!, ImageResources.dog_gray);
    }

    [Fact]
    public async Task EditingAPageMakesTheWrittenFileStale()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog);
        var document = Assert.Single(_queue.Documents);
        Assert.True(document.FileMatchesPages);

        RotateEverything();

        Assert.False(document.FileMatchesPages);
    }

    [Fact]
    public async Task TakingOverTheWindowsPagesDoesNotMakeTheFileStale()
    {
        // Being pointed at the window's page objects is not a change to the document -- at that moment
        // they are the pages the scan produced. Counting it as one would write a second copy of every
        // document that files locally and uploads on the button.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog);
        var document = Assert.Single(_queue.Documents);
        Assert.True(document.HasAdoptedWindowPages);

        await pipeline.Advance(document, triggeredByOperator: true);

        Assert.Single(Folder.GetFiles());
    }

    [Fact]
    public async Task AFinishedDocumentSurvivesItsPagesBeingCleared()
    {
        // It is the record that those pages reached the archive, and clearing the window is the normal
        // way to start the next batch.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog);
        var document = Assert.Single(_queue.Documents);
        pipeline.Finish(document);

        DeletePages(_imageList.Images.ToList());

        Assert.Equal(DocumentStatus.Done, Assert.Single(_queue.Documents).Status);
    }

    [Fact]
    public async Task EditingAFiledDocumentPutsItBackInTheQueue()
    {
        // A profile that only files locally finishes its documents at once. Correcting a page afterwards
        // has to give the operator a way to put the corrected version on disk, or the edit is one the
        // window shows and the folder never hears about.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog);
        var document = Assert.Single(_queue.Documents);
        Assert.Equal(DocumentStatus.Done, document.Status);

        RotateEverything();

        Assert.Equal(DocumentStatus.Pending, document.Status);
        Assert.False(document.FileMatchesPages);

        await pipeline.Advance(document, triggeredByOperator: true);

        Assert.Equal(DocumentStatus.Done, document.Status);
        Assert.True(document.FileMatchesPages);
        // The corrected version is written next to the first one rather than over it: a file in the
        // operator's own folder is theirs.
        Assert.Equal(2, Folder.GetFiles().Length);
    }

    [Fact]
    public async Task RemovingFinishedDocumentsTakesTheirPagesWithThem()
    {
        // Leaving the pages behind would put pages that are already filed into the canvas as belonging
        // to no document -- editable again, and draggable into a document they have nothing to do with.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog, ImageResources.dog_gray);
        Assert.Equal(DocumentStatus.Done, Assert.Single(_queue.Documents).Status);

        var removed = pipeline.RemoveFinished();

        Assert.Equal(1, removed);
        Assert.Empty(_queue.Documents);
        Assert.Empty(_imageList.Images);
    }

    [Fact]
    public async Task RemovingFinishedDocumentsLeavesTheUnfinishedOnesAlone()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog);
        await ScanIntoWindow(pipeline, WaitingProfile(), ImageResources.dog_gray, ImageResources.dog);
        Assert.Equal(2, _queue.Documents.Count);

        var removed = pipeline.RemoveFinished();

        Assert.Equal(1, removed);
        var document = Assert.Single(_queue.Documents);
        Assert.Equal(DocumentStatus.Pending, document.Status);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(2, _imageList.Images.Count);
    }

    [Fact]
    public async Task RemovingFinishedDocumentsDeletesNothingFromDisk()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog);
        var written = Assert.Single(Folder.GetFiles());

        pipeline.RemoveFinished();

        Assert.True(File.Exists(written.FullName));
    }

    [Fact]
    public async Task DraggingAPageIntoAnotherDocumentMovesItThere()
    {
        // Two scans, so the window holds two documents: A with two pages, B with two.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog, ImageResources.dog_gray);
        await ScanIntoWindow(pipeline, ImageResources.dog, ImageResources.dog_gray);
        var documents = _queue.Documents;
        Assert.Equal(2, documents.Count);

        // A's second page is dropped between B's two pages.
        Move(_imageList.Images[1], 3);

        Assert.Equal(1, documents[0].PageCount);
        Assert.Equal(3, documents[1].PageCount);
        // Both files now show something else than their document does.
        Assert.False(documents[0].FileMatchesPages);
        Assert.False(documents[1].FileMatchesPages);
    }

    [Fact]
    public async Task DraggingADocumentsLastPageAwayTakesTheDocumentWithIt()
    {
        var pipeline = CreatePipeline();
        var profile = WaitingProfile();
        await ScanIntoWindow(pipeline, profile, ImageResources.dog);
        await ScanIntoWindow(pipeline, profile, ImageResources.dog_gray, ImageResources.dog);
        Assert.Equal(2, _queue.Documents.Count);

        Move(_imageList.Images[0], 2);

        var document = Assert.Single(_queue.Documents);
        Assert.Equal(3, document.PageCount);
    }

    [Fact]
    public async Task ReorderingWithinADocumentDoesNotMoveThePageAnywhere()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, ImageResources.dog, ImageResources.dog_gray, ImageResources.dog);
        await ScanIntoWindow(pipeline, ImageResources.dog_gray);
        var documents = _queue.Documents;

        // The first document's last page is moved to its front.
        Move(_imageList.Images[2], 0);

        Assert.Equal(3, documents[0].PageCount);
        Assert.Equal(1, documents[1].PageCount);
    }

    [Fact]
    public async Task APageDraggedIntoAnotherDocumentIsOwnedOnlyByThatOne()
    {
        // A profile with nowhere to upload to finishes its documents as soon as they are written, and a
        // finished document is kept even once it has no pages left in the window -- it is the record
        // that a file was filed. That is right when the pages have gone from the window altogether, and
        // wrong when they are still there under another document: the document they were dropped into
        // holds them now, and a second claim on them left the owner map taking whichever document the
        // queue happened to reach last. The page was then drawn under the document it had just left,
        // in the middle of the one it was dropped into -- which reads as that document having been
        // split at the drop position, and is what an operator merging two documents by hand sees.
        var pipeline = CreatePipeline();
        var profile = Profile("test$(n).pdf");
        await ScanIntoWindow(pipeline, profile, ImageResources.dog, ImageResources.dog_gray,
            ImageResources.dog);
        await ScanIntoWindow(pipeline, profile, ImageResources.dog_gray);
        var first = _queue.Documents[0];
        var second = _queue.Documents[1];
        Assert.Equal(DocumentStatus.Done, second.Status);
        var dragged = _imageList.Images[3];

        // Dropped between the first document's first and second page.
        Move(dragged, 1);

        Assert.Same(first, _pageTracker.DocumentFor(dragged));
        Assert.Contains(dragged, first.WindowPages!);
        Assert.DoesNotContain(dragged, second.WindowPages!);
    }

    [Fact]
    public async Task ADocumentDroppedForHavingNoPagesLeftStopsOwningThem()
    {
        // The document is taken out of the queue once the sync is over, so at the moment the owner map
        // is rebuilt it is still in it and still holding the page that was dragged away. Leaving it
        // there heads a section of the canvas with a document that no longer exists, and nothing puts
        // it right afterwards: a change to the queue does not start another sync.
        var pipeline = CreatePipeline();
        var profile = WaitingProfile();
        await ScanIntoWindow(pipeline, profile, ImageResources.dog, ImageResources.dog_gray,
            ImageResources.dog);
        await ScanIntoWindow(pipeline, profile, ImageResources.dog_gray);
        var dragged = _imageList.Images[3];

        Move(dragged, 1);

        var document = Assert.Single(_queue.Documents);
        Assert.Same(document, _pageTracker.DocumentFor(dragged));
    }

    [Fact]
    public async Task ADroppedPageJoinsTheDocumentEvenWhenTheOneItLeftIsTheBiggerOne()
    {
        // The shape that used to come out backwards. Which page moved cannot be read off this order --
        // "the last page went up" and "the page above it went down" describe it equally well -- and the
        // longest-unmoved-run reading picked whichever document had more pages. The operator dragged one
        // page into another document and watched a different page change document instead.
        var pipeline = CreatePipeline();
        var profile = Profile("test$(n).pdf");
        await ScanIntoWindow(pipeline, profile, ImageResources.dog, ImageResources.dog_gray);
        await ScanIntoWindow(pipeline, profile, ImageResources.dog);
        var first = _queue.Documents[0];
        var stayedPut = _imageList.Images[1];
        var dragged = _imageList.Images[2];

        Move(dragged, 1);

        Assert.Same(first, _pageTracker.DocumentFor(dragged));
        Assert.Same(first, _pageTracker.DocumentFor(stayedPut));
        Assert.Equal(3, first.PageCount);
    }

    [Fact]
    public async Task MovingAPageUpAcrossADocumentBoundaryTakesItThere()
    {
        var pipeline = CreatePipeline();
        var profile = WaitingProfile();
        await ScanIntoWindow(pipeline, profile, ImageResources.dog, ImageResources.dog_gray);
        await ScanIntoWindow(pipeline, profile, ImageResources.dog);
        var first = _queue.Documents[0];
        var nudged = _imageList.Images[2];

        Nudge(nudged, down: false);

        Assert.Same(first, _pageTracker.DocumentFor(nudged));
    }

    [Fact]
    public async Task MovingTheLastPageDownLeavesItWhereItIs()
    {
        // Move down on the last page does nothing: the mutation refuses to push it past the end. Naming
        // it as moved anyway would hand its document's only page to the document above, for a keypress
        // that changed nothing on screen.
        var pipeline = CreatePipeline();
        var profile = WaitingProfile();
        await ScanIntoWindow(pipeline, profile, ImageResources.dog, ImageResources.dog_gray);
        await ScanIntoWindow(pipeline, profile, ImageResources.dog);
        var second = _queue.Documents[1];
        var nudged = _imageList.Images[2];

        Nudge(nudged, down: true);

        Assert.Same(second, _pageTracker.DocumentFor(nudged));
        Assert.Equal(2, _queue.Documents.Count);
    }

    [Fact]
    public async Task ADropBackOntoItsOwnPositionMergesNothing()
    {
        // What someone trying to merge two documents by dragging tries first. It has to do nothing
        // rather than quietly give one document's pages to the other: merging is a deliberate action,
        // not a side effect of a drag that went nowhere.
        var pipeline = CreatePipeline();
        var profile = WaitingProfile();
        await ScanIntoWindow(pipeline, profile, ImageResources.dog, ImageResources.dog_gray);
        await ScanIntoWindow(pipeline, profile, ImageResources.dog, ImageResources.dog_gray);
        var second = _queue.Documents[1];
        var pages = new[] { _imageList.Images[2], _imageList.Images[3] };

        Move(pages, 2);

        Assert.Equal(2, _queue.Documents.Count);
        Assert.All(pages, page => Assert.Same(second, _pageTracker.DocumentFor(page)));
    }

    /// <summary>
    /// A drop, the way ImageListActions performs one: the pages that were picked up are named, and then
    /// the window is rearranged.
    /// </summary>
    private void Move(UiImage page, int index) => Move([page], index);

    private void Move(IReadOnlyList<UiImage> pages, int index)
    {
        _pageTracker.ReportMove(pages);
        _imageList.Mutate(new ImageListMutation.MoveTo(index), ListSelection.From(pages));
    }

    /// <summary>Move up / Move down, which name their selection the same way a drop does.</summary>
    private void Nudge(UiImage page, bool down)
    {
        _pageTracker.ReportMove([page]);
        _imageList.Mutate(
            down ? new ListMutation<UiImage>.MoveDown() : new ListMutation<UiImage>.MoveUp(),
            ListSelection.From([page]));
    }

    /// <summary>
    /// Scans and then puts the pages into the window the way the desktop does, so the documents can take
    /// them over.
    /// </summary>
    private Task ScanIntoWindow(DocumentPipeline pipeline, params byte[][] images) =>
        ScanIntoWindow(pipeline, Profile("test$(n).pdf"), images);

    private async Task ScanIntoWindow(DocumentPipeline pipeline, ScanProfile profile, params byte[][] images)
    {
        var produced = await pipeline
            .Process(profile, CreateScannedImages(images).ToAsyncEnumerable())
            .ToListAsync();
        _imageList.Mutate(new ImageListMutation.Append(produced.Select(x => new UiImage(x))),
            isPassiveInteraction: true);
    }

    private void RotateEverything() =>
        _imageList.Mutate(new ImageListMutation.RotateFlip(90),
            ListSelection.From(_imageList.Images));

    private void DeletePages(List<UiImage> pages) =>
        _imageList.Mutate(new ListMutation<UiImage>.DeleteSelected(), ListSelection.From(pages));

    /// <summary>
    /// A profile whose documents wait for the upload button, so they are pending rather than finished.
    /// A profile with nothing to upload to finishes a document the moment it has been written.
    /// </summary>
    private ScanProfile WaitingProfile()
    {
        var profile = Profile("test$(n).pdf");
        profile.SapArchiveSettings = new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" };
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { UploadTrigger = UploadTrigger.Manual };
        return profile;
    }

    private ScanProfile Profile(string name) => new()
    {
        DisplayName = "Test",
        EnableAutoSave = true,
        DocumentWorkflow = new DocumentWorkflowSettings
        {
            Version = DocumentWorkflowSettings.CURRENT_VERSION,
            SeparationMode = DocumentSeparationMode.None,
            BarcodeSymbologies = [],
            SaveLocally = true,
            LocalFolder = FolderPath,
            DocumentNameTemplate = name,
            CleanupAfterCompletion = false
        }
    };
}
