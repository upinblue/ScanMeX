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
        new DocumentPageTracker(_imageList, _queue));

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

    private void Move(UiImage page, int index) =>
        _imageList.Mutate(new ImageListMutation.MoveTo(index), ListSelection.From([page]));

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
