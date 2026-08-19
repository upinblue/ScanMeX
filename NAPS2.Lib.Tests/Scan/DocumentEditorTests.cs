#nullable enable
using NAPS2.EtoForms;
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
/// Splitting a document in two and merging one back into the one before it -- what a missed separator
/// sheet, or one that separated where it should not have, costs to repair.
/// </summary>
public class DocumentEditorTests : ContextualTests
{
    private readonly DocumentQueue _queue = new();
    private readonly UiImageList _imageList = new();
    private readonly DocumentPageTracker _pageTracker;
    private readonly DocumentEditor _editor;
    // One instance, reused: two scans with the same profile are the same profile object in the app, and
    // that is what "the same profile" is compared by.
    private readonly ScanProfile _sharedProfile;

    public DocumentEditorTests()
    {
        _pageTracker = new DocumentPageTracker(_imageList, _queue);
        _editor = new DocumentEditor(_queue, _pageTracker, _imageList);
        _sharedProfile = Profile();
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
    public async Task SplittingMakesTheSelectedPageTheStartOfANewDocument()
    {
        await ScanIntoWindow(ImageResources.dog, ImageResources.dog_gray, ImageResources.dog);

        _editor.SplitAt([_imageList.Images[1]]);

        var documents = _queue.Documents;
        Assert.Equal(2, documents.Count);
        Assert.Equal(1, documents[0].PageCount);
        Assert.Equal(2, documents[1].PageCount);
        // In page order, not appended at the bottom past everything scanned later.
        Assert.Same(_imageList.Images[1], documents[1].WindowPages![0]);
    }

    [Fact]
    public async Task SplittingLeavesBothFilesToBeWrittenAgain()
    {
        await ScanIntoWindow(ImageResources.dog, ImageResources.dog_gray);
        Assert.True(Assert.Single(_queue.Documents).FileMatchesPages);

        _editor.SplitAt([_imageList.Images[1]]);

        var documents = _queue.Documents;
        Assert.False(documents[0].FileMatchesPages);
        // The new one has no file at all yet.
        Assert.Null(documents[1].SavedPath);
    }

    [Fact]
    public async Task TheIdentificationFollowsThePages()
    {
        // The usual reason for splitting by hand is that a barcode was not read as a separator. The half
        // that breaks off has to be filed under the value its own pages carry, the same way the scan
        // would have done it -- and the half left behind must not go on claiming a barcode that left
        // with the other one.
        var pages = CreateScannedImages(ImageResources.dog, ImageResources.dog_gray);
        pages[1] = WithBarcode(pages[1], "SPLIT-1");
        await ScanIntoWindow(CreatePipeline(), _sharedProfile, pages);
        Assert.Equal("SPLIT-1", Assert.Single(_queue.Documents).Identifier);

        _editor.SplitAt([_imageList.Images[1]]);

        Assert.Equal("SPLIT-1", _queue.Documents[1].Identifier);
        Assert.Null(_queue.Documents[0].Identifier);
    }

    [Fact]
    public async Task AnIdentificationTypedByHandSurvivesASplit()
    {
        // It was a correction of exactly this, so re-reading the barcodes must not undo it.
        await ScanIntoWindow(ImageResources.dog, ImageResources.dog_gray);
        var document = Assert.Single(_queue.Documents);
        document.SetIdentifier("4711", DocumentBarcodeSource.Manual);

        _editor.SplitAt([_imageList.Images[1]]);

        Assert.Equal("4711", _queue.Documents[0].Identifier);
    }

    [Fact]
    public async Task SplittingAtTheFirstPageIsNotOffered()
    {
        await ScanIntoWindow(ImageResources.dog, ImageResources.dog_gray);

        Assert.False(_editor.CanSplitAt([_imageList.Images[0]]));
    }

    [Fact]
    public async Task SplittingAnArchivedDocumentIsNotOffered()
    {
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, _sharedProfile, ImageResources.dog, ImageResources.dog_gray);
        pipeline.Finish(Assert.Single(_queue.Documents));

        Assert.False(_editor.CanSplitAt([_imageList.Images[1]]));
    }

    [Fact]
    public async Task MergingGivesADocumentToTheOneAboveIt()
    {
        await ScanIntoWindow(ImageResources.dog, ImageResources.dog_gray);
        await ScanIntoWindow(ImageResources.dog);
        Assert.Equal(2, _queue.Documents.Count);

        _editor.MergeWithPrevious([_imageList.Images[2]]);

        var document = Assert.Single(_queue.Documents);
        Assert.Equal(3, document.PageCount);
        Assert.False(document.FileMatchesPages);
    }

    [Fact]
    public async Task SplittingAndMergingBackLeavesOneDocumentAgain()
    {
        await ScanIntoWindow(ImageResources.dog, ImageResources.dog_gray, ImageResources.dog);

        _editor.SplitAt([_imageList.Images[1]]);
        _editor.MergeWithPrevious([_imageList.Images[1]]);

        Assert.Equal(3, Assert.Single(_queue.Documents).PageCount);
    }

    [Fact]
    public async Task TheFirstDocumentHasNothingToMergeInto()
    {
        await ScanIntoWindow(ImageResources.dog, ImageResources.dog_gray);

        Assert.False(_editor.CanMergeWithPrevious([_imageList.Images[0]]));
    }

    [Fact]
    public async Task MergingAcrossProfilesIsNotOffered()
    {
        // The profile decides the folder, the name and the archive.
        var pipeline = CreatePipeline();
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog);
        await ScanIntoWindow(pipeline, Profile(), ImageResources.dog_gray);

        Assert.False(_editor.CanMergeWithPrevious([_imageList.Images[1]]));
    }

    [Fact]
    public async Task PagesThatBelongToNoDocumentCanNeitherBeSplitNorMerged()
    {
        await ScanIntoWindow(ImageResources.dog);
        _imageList.Mutate(
            new ImageListMutation.Append(CreateScannedImages(ImageResources.dog_gray).Select(x => new UiImage(x))),
            isPassiveInteraction: true);
        var imported = _imageList.Images[1];

        Assert.False(_editor.CanSplitAt([imported]));
        Assert.False(_editor.CanMergeWithPrevious([imported]));
    }

    private static ProcessedImage WithBarcode(ProcessedImage image, string text) =>
        image.WithPostProcessingData(image.PostProcessingData with
        {
            Barcode = new Barcode(true, true, text, "CODE_39")
        }, true);

    private Task ScanIntoWindow(params byte[][] images) =>
        ScanIntoWindow(CreatePipeline(), _sharedProfile, CreateScannedImages(images));

    private Task ScanIntoWindow(DocumentPipeline pipeline, ScanProfile profile, params byte[][] images) =>
        ScanIntoWindow(pipeline, profile, CreateScannedImages(images));

    private async Task ScanIntoWindow(DocumentPipeline pipeline, ScanProfile profile,
        List<ProcessedImage> pages)
    {
        var produced = await pipeline.Process(profile, pages.ToAsyncEnumerable()).ToListAsync();
        _imageList.Mutate(new ImageListMutation.Append(produced.Select(x => new UiImage(x))),
            isPassiveInteraction: true);
    }

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
