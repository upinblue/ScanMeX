#nullable enable
using NAPS2.EtoForms;
using NAPS2.EtoForms.Desktop;
using NAPS2.EtoForms.Notifications;
using NAPS2.Images;
using NAPS2.Pdf;
using NAPS2.PostScan;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using NSubstitute;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// What the window refuses to do to a document that has already reached an archive, and what it refuses
/// across profiles. Such a document is the record that exactly those pages are in there, so an edit that
/// appears to work while the archive stays as it was would be worse than one that is refused.
/// </summary>
/// <remarks>
/// Having reached an archive, not merely being finished: a profile that only files into a folder
/// finishes a document as soon as it is written, and locking those would leave anyone who uploads
/// nowhere unable to edit a page at all.
/// </remarks>
public class ArchivedDocumentGuardTests : ContextualTests
{
    private readonly DocumentQueue _queue = new();
    private readonly UiImageList _imageList = new();
    private readonly DocumentPageTracker _pageTracker;
    private readonly INotify _notify = Substitute.For<INotify>();
    private readonly ImageListActions _actions;

    public ArchivedDocumentGuardTests()
    {
        _pageTracker = new DocumentPageTracker(_imageList, _queue);
        _actions = new ImageListActions(_imageList, null!, null!, Naps2Config.Stub(), null!, null!,
            _notify, null!, _pageTracker);
    }

    private DocumentPipeline CreatePipeline() => new(
        Substitute.For<ErrorOutput>(),
        Substitute.For<ISaveNotify>(),
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
    public async Task DeletingAPageOfAFinishedDocumentIsRefused()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog, ImageResources.dog_gray);
        Archive(pipeline, Assert.Single(_queue.Documents));

        Select(_imageList.Images[0]);
        _actions.DeleteSelected();

        Assert.Equal(2, _imageList.Images.Count);
        _notify.Received().Refused(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DeletingAMixedSelectionKeepsOnlyTheArchivedPages()
    {
        // Refusing the whole thing would leave the operator to pick the selection apart by hand.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog);
        Archive(pipeline, Assert.Single(_queue.Documents));
        Import(ImageResources.dog_gray);

        Select(_imageList.Images[0], _imageList.Images[1]);
        _actions.DeleteSelected();

        var remaining = Assert.Single(_imageList.Images);
        Assert.NotNull(_pageTracker.DocumentFor(remaining));
        _notify.Received().Refused(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task MovingAPageOfAFinishedDocumentIsRefused()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog, ImageResources.dog_gray);
        Archive(pipeline, Assert.Single(_queue.Documents));
        var first = _imageList.Images[0];

        Select(first);
        _actions.MoveTo(2);

        Assert.Same(first, _imageList.Images[0]);
        _notify.Received().Refused(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task MovingAPageIntoADocumentOfAnotherProfileIsRefused()
    {
        // The profile decides the folder, the name and the archive, so a page changing profile changes
        // all three with nothing on screen saying so.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog, ImageResources.dog_gray);
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog, ImageResources.dog_gray);
        Assert.Equal(2, _queue.Documents.Count);
        var moved = _imageList.Images[0];

        // Dropped inside the second document's run, which is where it would have joined it.
        Select(moved);
        _actions.MoveTo(3);

        Assert.Same(moved, _imageList.Images[0]);
        _notify.Received().Refused(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task PagesOfADocumentBeingFiledRightNowAreLeftAlone()
    {
        // With automatic upload this happens while the operator carries on working, so a document
        // halfway into the archive must not have its pages pulled out from under it.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog, ImageResources.dog_gray);
        Assert.Single(_queue.Documents).Status = DocumentStatus.Working;

        Select(_imageList.Images[0]);
        _actions.DeleteSelected();

        Assert.Equal(2, _imageList.Images.Count);
        _notify.Received().Refused(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ClearingTheWindowIsNotAnEditAndIsAllowed()
    {
        // A finished document keeps its own record of what reached the archive, so clearing the window
        // -- the normal way to start the next batch -- must not be caught by the guard.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog);
        Archive(pipeline, Assert.Single(_queue.Documents));

        Select(_imageList.Images.ToArray());
        _actions.DeleteAll();

        Assert.Empty(_imageList.Images);
        Assert.Equal(DocumentStatus.Done, Assert.Single(_queue.Documents).Status);
    }

    /// <summary>Takes a document all the way into an archive, which is what makes it untouchable.</summary>
    private static void Archive(DocumentPipeline pipeline, ScannedDocument document)
    {
        document.CompletedTargets.Add("SharePoint");
        pipeline.Finish(document);
    }

    private void Select(params UiImage[] pages) =>
        _imageList.UpdateSelection(ListSelection.From(pages));

    private async Task ScanIntoWindow(DocumentPipeline pipeline, ScanProfile profile, params byte[][] images)
    {
        var produced = await pipeline
            .Process(profile, CreateScannedImages(images).ToAsyncEnumerable())
            .ToListAsync();
        _imageList.Mutate(new ImageListMutation.Append(produced.Select(x => new UiImage(x))),
            isPassiveInteraction: true);
    }

    private void Import(params byte[][] images) =>
        _imageList.Mutate(
            new ImageListMutation.Append(CreateScannedImages(images).Select(x => new UiImage(x))),
            isPassiveInteraction: true);

    private ScanProfile Profile() => new()
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
            DocumentNameTemplate = "test$(n).pdf",
            CleanupAfterCompletion = false
        }
    };
}
