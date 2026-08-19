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
/// Which pages the canvas draws under which heading. A section is a run of consecutive pages belonging
/// to one document, so this is also where the rule that a document cannot be interleaved with another
/// one is checked.
/// </summary>
public class DocumentSectionBuilderTests : ContextualTests
{
    private readonly DocumentQueue _queue = new();
    private readonly UiImageList _imageList = new();
    private readonly DocumentPageTracker _pageTracker;
    private readonly DocumentSectionBuilder _builder;

    public DocumentSectionBuilderTests()
    {
        _pageTracker = new DocumentPageTracker(_imageList, _queue);
        var colorScheme = new ColorScheme(Substitute.For<IDarkModeProvider>()) { Config = Naps2Config.Stub() };
        _builder = new DocumentSectionBuilder(_pageTracker, colorScheme);
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
    public void PagesThatBelongToNoDocumentAreNotSectioned()
    {
        // A window holding only imported pages is not a batch whose documents failed to be recognised,
        // and heading all of it "not part of a document" would be an accusation rather than information.
        Import(ImageResources.dog, ImageResources.dog_gray);

        Assert.Empty(_builder.Build(_imageList.Images));
    }

    [Fact]
    public async Task EachDocumentBecomesItsOwnSection()
    {
        await ScanIntoWindow(DocumentSeparationMode.OnePerPage,
            ImageResources.dog, ImageResources.dog_gray, ImageResources.dog);

        var sections = _builder.Build(_imageList.Images);

        Assert.Equal(3, sections.Count);
        Assert.Equal([0, 1, 2], sections.Select(x => x.StartIndex));
        Assert.All(sections, x => Assert.Equal(1, x.Count));
    }

    [Fact]
    public async Task ADocumentsPagesFormOneSection()
    {
        await ScanIntoWindow(DocumentSeparationMode.None,
            ImageResources.dog, ImageResources.dog_gray, ImageResources.dog);

        var section = Assert.Single(_builder.Build(_imageList.Images));

        Assert.Equal(0, section.StartIndex);
        Assert.Equal(3, section.Count);
        Assert.Equal(2, section.EndIndex);
    }

    [Fact]
    public async Task ImportedPagesGetASectionOfTheirOwn()
    {
        await ScanIntoWindow(DocumentSeparationMode.None, ImageResources.dog, ImageResources.dog_gray);
        Import(ImageResources.dog);

        var sections = _builder.Build(_imageList.Images);

        Assert.Equal(2, sections.Count);
        Assert.Equal(2, sections[0].Count);
        Assert.Equal(UiStrings.SectionUnassigned, sections[1].Title);
        Assert.Equal(2, sections[1].StartIndex);
        Assert.Equal(1, sections[1].Count);
    }

    [Fact]
    public async Task ASectionIsHeadedWithTheDocumentsNameAndPageCount()
    {
        await ScanIntoWindow(DocumentSeparationMode.None, ImageResources.dog, ImageResources.dog_gray);

        var section = Assert.Single(_builder.Build(_imageList.Images));

        Assert.Equal("test1.pdf", section.Title);
        Assert.Contains(string.Format(UiStrings.DocumentPageCount, 2), section.Meta);
    }

    [Fact]
    public async Task AProfileWithNowhereToUploadDoesNotClaimToBeWaitingForOne()
    {
        // A save-only profile leaves its documents pending, since nothing takes them further. The heading
        // has to say what that means -- filed -- rather than announce a queue that does not exist.
        await ScanIntoWindow(DocumentSeparationMode.None, ImageResources.dog);

        var section = Assert.Single(_builder.Build(_imageList.Images));

        Assert.Contains(UiStrings.DocumentStatusSaved, section.Meta);
        Assert.DoesNotContain(UiStrings.DocumentStatusSavedWaiting, section.Meta);
    }

    [Fact]
    public async Task DeletingADocumentsLastPageTakesItsSectionWithIt()
    {
        await ScanIntoWindow(DocumentSeparationMode.OnePerPage,
            ImageResources.dog, ImageResources.dog_gray);
        Assert.Equal(2, _builder.Build(_imageList.Images).Count);

        _imageList.Mutate(new ListMutation<UiImage>.DeleteSelected(),
            ListSelection.From([_imageList.Images[0]]));

        var section = Assert.Single(_builder.Build(_imageList.Images));
        Assert.Equal(0, section.StartIndex);
        Assert.Equal(1, section.Count);
    }

    private async Task ScanIntoWindow(DocumentSeparationMode separation, params byte[][] images)
    {
        var produced = await CreatePipeline()
            .Process(Profile(separation), CreateScannedImages(images).ToAsyncEnumerable())
            .ToListAsync();
        _imageList.Mutate(new ImageListMutation.Append(produced.Select(x => new UiImage(x))),
            isPassiveInteraction: true);
    }

    /// <summary>Pages that arrive without going through the pipeline, so they belong to no document.</summary>
    private void Import(params byte[][] images) =>
        _imageList.Mutate(
            new ImageListMutation.Append(CreateScannedImages(images).Select(x => new UiImage(x))),
            isPassiveInteraction: true);

    private ScanProfile Profile(DocumentSeparationMode separation) => new()
    {
        DisplayName = "Test",
        EnableAutoSave = true,
        DocumentWorkflow = new DocumentWorkflowSettings
        {
            Version = DocumentWorkflowSettings.CURRENT_VERSION,
            SeparationMode = separation,
            BarcodeSymbologies = [],
            SaveLocally = true,
            LocalFolder = FolderPath,
            DocumentNameTemplate = "test$(n).pdf",
            CleanupAfterCompletion = false
        }
    };
}
