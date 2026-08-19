#nullable enable
using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
using NAPS2.Images;
using NAPS2.Pdf;
using NAPS2.PostScan;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using NAPS2.Sdk.Tests.Asserts;
using NSubstitute;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// Splitting a scan into documents and writing them out. These are the behaviours NAPS2's auto save had
/// and that a scan still has to have, now that the file is produced by the pipeline rather than being
/// the thing a document is.
/// </summary>
public class DocumentPipelineTests : ContextualTests
{
    private readonly DialogHelper _dialogHelper = Substitute.For<DialogHelper>();
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
            _dialogHelper),
        Substitute.For<DocumentUploadService>(Naps2Config.Stub(), Substitute.For<OperationProgress>(),
            Substitute.For<ISaveNotify>()),
        new DocumentPageTracker(_imageList, _queue));

    [Fact]
    public async Task NoPagesWritesNothing()
    {
        var output = await Run(Profile("test$(n).jpg"), CreateScannedImages());

        Assert.Empty(output);
        Assert.Empty(Folder.GetFiles());
        Assert.Empty(_queue.Documents);
    }

    [Fact]
    public async Task SinglePdf()
    {
        var output = await Run(Profile("test$(n).pdf"), CreateScannedImages(ImageResources.dog));

        Assert.Single(output);
        Assert.False(IsDisposed(output[0]));
        Assert.Single(Folder.GetFiles());
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test1.pdf"), ImageResources.dog);
    }

    [Fact]
    public async Task SinglePdf_InMissingSubfolder_CreatesTheFolder()
    {
        // Paths routinely use date placeholders as a subfolder, so on the first scan of each day the
        // target folder does not exist yet.
        var profile = Profile("test$(n).pdf", folder: Path.Combine(FolderPath, "$(YYYY)-$(MM)-$(DD)"));

        await Run(profile, CreateScannedImages(ImageResources.dog));

        var dated = Path.Combine(FolderPath, DateTime.Now.ToString("yyyy-MM-dd"));
        Assert.True(Directory.Exists(dated), "the dated subfolder should have been created");
        PdfAsserts.AssertImages(Path.Combine(dated, "test1.pdf"), ImageResources.dog);
    }

    [Fact]
    public async Task SingleJpeg()
    {
        var output = await Run(Profile("test$(n).jpg"), CreateScannedImages(ImageResources.dog));

        Assert.Single(output);
        Assert.Single(Folder.GetFiles());
        ImageAsserts.Similar(ImageResources.dog, Path.Combine(FolderPath, "test1.jpg"));
    }

    [Fact]
    public async Task OneDocumentPerScan()
    {
        var output = await Run(
            Profile("test$(n).pdf", DocumentSeparationMode.None),
            CreateScannedImages(ImageResources.dog, ImageResources.dog_gray));

        Assert.Equal(2, output.Count);
        Assert.Single(Folder.GetFiles());
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test1.pdf"),
            ImageResources.dog, ImageResources.dog_gray);
    }

    [PlatformFact(exclude: PlatformFlags.ImageSharpImage)]
    public async Task OneTiffPerScanKeepsBothFrames()
    {
        var output = await Run(
            Profile("test$(n).tiff", DocumentSeparationMode.None),
            CreateScannedImages(ImageResources.dog, ImageResources.dog_gray));

        Assert.Equal(2, output.Count);
        Assert.Single(Folder.GetFiles());
        var frames = await ImageContext.LoadFrames(Path.Combine(FolderPath, "test1.tiff")).ToListAsync();
        Assert.Equal(2, frames.Count);
        ImageAsserts.Similar(ImageResources.dog, frames[0], ignoreResolution: true);
        ImageAsserts.Similar(ImageResources.dog_gray, frames[1], ignoreResolution: true);
    }

    [Fact]
    public async Task OneDocumentPerPage()
    {
        var output = await Run(
            Profile("test$(n).pdf", DocumentSeparationMode.OnePerPage),
            CreateScannedImages(ImageResources.dog, ImageResources.dog_gray));

        Assert.Equal(2, output.Count);
        Assert.Equal(2, Folder.GetFiles().Length);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test1.pdf"), ImageResources.dog);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test2.pdf"), ImageResources.dog_gray);
    }

    [Fact]
    public async Task PatchTSheetsSeparateAndAreDropped()
    {
        var scanned = CreateScannedImages(
            ImageResources.dog,
            ImageResources.dog_gray,
            ImageResources.patcht,
            ImageResources.dog_h_n300);
        scanned[2] = WithBarcode(scanned[2], "PATCHT", "CODE_39");

        await Run(Profile("test$(n).pdf", DocumentSeparationMode.PatchT), scanned);

        Assert.Equal(2, Folder.GetFiles().Length);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test1.pdf"),
            ImageResources.dog, ImageResources.dog_gray);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test2.pdf"), ImageResources.dog_h_n300);
    }

    [Fact]
    public async Task ABarcodePageStartsANewDocument()
    {
        var scanned = CreateScannedImages(
            ImageResources.dog,
            ImageResources.dog_gray,
            ImageResources.dog_h_n300,
            ImageResources.dog_s_n300);
        scanned[2] = WithBarcode(scanned[2], "12345678", "CODE_39");

        await Run(Profile("test$(n).pdf", DocumentSeparationMode.Barcode), scanned);

        Assert.Equal(2, Folder.GetFiles().Length);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test1.pdf"),
            ImageResources.dog, ImageResources.dog_gray);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test2.pdf"),
            ImageResources.dog_h_n300, ImageResources.dog_s_n300);
    }

    [Fact]
    public async Task OnlyABarcodeMatchingTheRegexSeparates()
    {
        var profile = Profile("test$(n).pdf", DocumentSeparationMode.Barcode);
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { SeparationPattern = @"\b\d{8}\b" };

        var scanned = CreateScannedImages(
            ImageResources.dog,
            ImageResources.dog_gray,
            ImageResources.dog_h_n300,
            ImageResources.dog_s_n300,
            ImageResources.dog_sh_n1000);
        // Doesn't match the pattern, so it must not split.
        scanned[1] = WithBarcode(scanned[1], "ABC123", "CODE_39");
        // Matches, so the barcode page starts a new document.
        scanned[3] = WithBarcode(scanned[3], "87654321", "CODE_39");

        await Run(profile, scanned);

        Assert.Equal(2, Folder.GetFiles().Length);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test1.pdf"),
            ImageResources.dog, ImageResources.dog_gray, ImageResources.dog_h_n300);
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test2.pdf"),
            ImageResources.dog_s_n300, ImageResources.dog_sh_n1000);
    }

    /// <summary>
    /// Some scan and import paths don't populate the barcode's format, and those can't be ruled out by
    /// symbology without dropping a separator that is really there.
    /// </summary>
    [Fact]
    public async Task ABarcodeWithoutAFormatStillSeparates()
    {
        var scanned = CreateScannedImages(
            ImageResources.dog,
            ImageResources.dog_gray,
            ImageResources.dog_h_n300,
            ImageResources.dog_s_n300);
        scanned[2] = WithBarcode(scanned[2], "12345678", null);

        await Run(Profile("test$(n).pdf", DocumentSeparationMode.Barcode), scanned);

        Assert.Equal(2, Folder.GetFiles().Length);
    }

    [Fact]
    public async Task PromptForFilePath()
    {
        var profile = Profile("test_a_$(n).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { PromptForFilePath = true };
        _dialogHelper.PromptToSavePdfOrImage(Arg.Any<string>(), out Arg.Any<string>()).Returns(x =>
        {
            x[1] = Path.Combine(FolderPath, "test_b_$(n).pdf");
            return true;
        });

        var output = await Run(profile, CreateScannedImages(ImageResources.dog));

        Assert.Single(output);
        Assert.Single(Folder.GetFiles());
        PdfAsserts.AssertImages(Path.Combine(FolderPath, "test_b_1.pdf"), ImageResources.dog);
    }

    [Fact]
    public async Task CancellingTheSaveDialogWritesNothing()
    {
        var profile = Profile("test$(n).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { PromptForFilePath = true };
        _dialogHelper.PromptToSavePdfOrImage(Arg.Any<string>(), out Arg.Any<string>()).Returns(x =>
        {
            x[1] = Path.Combine(FolderPath, "test$(n).pdf");
            return false;
        });

        var output = await Run(profile, CreateScannedImages(ImageResources.dog));

        // The pages still reach the window: nothing was filed, so losing them as well would turn a
        // declined dialog into a lost scan.
        Assert.Single(output);
        Assert.Empty(Folder.GetFiles());
        Assert.Equal(DocumentStatus.Failed, Assert.Single(_queue.Documents).Status);
    }

    /// <summary>
    /// A profile that neither files documents nor uploads them is a real configuration -- scan, look,
    /// decide -- but it is also what a half-configured profile looks like, so the pages have to arrive
    /// in the window either way.
    /// </summary>
    [Fact]
    public async Task AProfileWithNoDestinationStillFillsTheWindow()
    {
        var profile = Profile("test$(n).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { SaveLocally = false };

        var output = await Run(profile, CreateScannedImages(ImageResources.dog, ImageResources.dog_gray));

        Assert.Equal(2, output.Count);
        Assert.Empty(Folder.GetFiles());
    }

    private async Task<List<ProcessedImage>> Run(ScanProfile profile, List<ProcessedImage> scanned)
    {
        return await CreatePipeline().Process(profile, scanned.ToAsyncEnumerable()).ToListAsync();
    }

    private static ProcessedImage WithBarcode(ProcessedImage image, string text, string? format) =>
        image.WithPostProcessingData(image.PostProcessingData with
        {
            Barcode = new Barcode(true, true, text, format)
        }, true);

    private ScanProfile Profile(string name,
        DocumentSeparationMode separation = DocumentSeparationMode.None, string? folder = null) => new()
    {
        DisplayName = "Test",
        EnableAutoSave = true,
        DocumentWorkflow = new DocumentWorkflowSettings
        {
            Version = DocumentWorkflowSettings.CURRENT_VERSION,
            SeparationMode = separation,
            BarcodeSymbologies = separation == DocumentSeparationMode.Barcode
                ? [BarcodeSymbology.Code39]
                : [],
            SaveLocally = true,
            LocalFolder = folder ?? FolderPath,
            DocumentNameTemplate = name,
            CleanupAfterCompletion = false
        }
    };
}
