#nullable enable
using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
using NAPS2.Pdf;
using NAPS2.PostScan;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using NSubstitute;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// The hand-off from producing a document to archiving it: the cases where it must not happen, and the
/// cases where it must not be the end of the line. A document that failed to save has nothing to upload;
/// a document whose upload failed has to stay retryable rather than being quietly left unarchived; and a
/// document that keeps no local copy must not lose the only copy of the scan on the way.
/// </summary>
public class DocumentPipelineUploadTests : ContextualTests
{
    private readonly ErrorOutput _errorOutput = Substitute.For<ErrorOutput>();
    private readonly ISaveNotify _notify = Substitute.For<ISaveNotify>();
    private readonly DocumentQueue _queue = new();
    private readonly UiImageList _imageList = new();
    private readonly DocumentUploadService _uploadService =
        Substitute.For<DocumentUploadService>(Naps2Config.Stub(), Substitute.For<OperationProgress>(),
            Substitute.For<ISaveNotify>());

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
        _uploadService,
        new DocumentPageTracker(_imageList, _queue));

    [Fact]
    public async Task AnUnresolvedPlaceholderFailsVisiblyAndUploadsNothing()
    {
        // $(unknown) survives substitution, so the path would be written literally. Naming a document
        // after an unexpanded placeholder is worse than not writing it, and uploading it is worse still.
        await Run(Profile("scan_$(unknown).pdf"));

        _errorOutput.Received().DisplayError(Arg.Is<string>(x => x.Contains("$(unknown)")));
        Assert.Empty(Folder.GetFiles());
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<ScannedDocument>());
        Assert.Equal(DocumentStatus.Failed, Assert.Single(_queue.Documents).Status);
    }

    [Fact]
    public async Task AFailedSaveUploadsNothing()
    {
        // A file where the target folder has to go: creating the directory fails, so the save fails.
        var blocked = Path.Combine(FolderPath, "sub");
        File.WriteAllText(blocked, "not a folder");

        await Run(Profile("scan.pdf", folder: blocked));

        // Asserted so the test can't pass merely because nothing ran: the save has to have been
        // attempted and failed, leaving no document for the upload to pick up.
        Assert.False(File.Exists(Path.Combine(blocked, "scan.pdf")));
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<ScannedDocument>());
        Assert.Equal(DocumentStatus.Failed, Assert.Single(_queue.Documents).Status);
    }

    [Fact]
    public async Task ASavedDocumentIsUploadedAutomatically()
    {
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(true);

        await Run(Profile("scan.pdf"));

        await _uploadService.Received(1).UploadAsync(
            Arg.Is<ScannedDocument>(x => x.FileName == "scan.pdf" && x.PageCount == 1));
    }

    /// <summary>
    /// A SAP outage or a network glitch used to leave the document saved locally with nothing recording
    /// that it never reached the archive. Left failed in the list it keeps the upload button enabled and
    /// can be retried once the cause is fixed.
    /// </summary>
    [Fact]
    public async Task AFailedAutomaticUploadStaysRetryable()
    {
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(false);

        await Run(Profile("scan.pdf"));

        var document = Assert.Single(_queue.Documents);
        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.True(_queue.HasReadyToUpload);
    }

    [Fact]
    public async Task TheManualTriggerWaitsWithoutUploading()
    {
        var profile = Profile("scan.pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { UploadTrigger = UploadTrigger.Manual };

        await Run(profile);

        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<ScannedDocument>());
        Assert.Equal(DocumentStatus.Pending, Assert.Single(_queue.Documents).Status);
    }

    /// <summary>
    /// The combination this redesign exists for: nothing is kept locally and nothing is uploaded until
    /// the operator presses the button, so no file has been written when the scan finishes.
    /// </summary>
    [Fact]
    public async Task UploadOnlyOnTheButtonWritesNothingUntilItIsPressed()
    {
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(true);
        var profile = Profile("scan.pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with
        {
            SaveLocally = false,
            UploadTrigger = UploadTrigger.Manual
        };

        await Run(profile);

        Assert.Empty(Folder.GetFiles());
        var document = Assert.Single(_queue.Documents);
        Assert.Null(document.SavedPath);
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<ScannedDocument>());

        await CreatePipeline().Advance(document, triggeredByOperator: true);

        await _uploadService.Received(1).UploadAsync(document);
        // Still nothing in the operator's folder, and the staging copy is gone again.
        Assert.Empty(Folder.GetFiles());
    }

    /// <summary>
    /// A document uploaded from the button is named from the state it is in at that moment, not from the
    /// state it was scanned in. This is what makes correcting a misread barcode worth anything.
    /// </summary>
    [Fact]
    public async Task ACorrectedIdentifierRenamesTheDocument()
    {
        string? uploadedName = null;
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(call =>
        {
            uploadedName = call.Arg<ScannedDocument>().FileName;
            return true;
        });
        var profile = Profile("$(id).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with
        {
            SaveLocally = false,
            UploadTrigger = UploadTrigger.Manual,
            IdMode = DocumentIdMode.ManualInput
        };

        await Run(profile);

        var document = Assert.Single(_queue.Documents);
        document.SetIdentifier("4711", DocumentBarcodeSource.Manual);
        await CreatePipeline().Advance(document, triggeredByOperator: true);

        Assert.Equal("4711.pdf", uploadedName);
    }

    /// <summary>
    /// A profile that keeps no local copy writes the file only so it can be uploaded. Deleting it after a
    /// failure would destroy the only copy of a scan that never reached the archive.
    /// </summary>
    [Fact]
    public async Task AStagingFileSurvivesAFailedUpload()
    {
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(false);
        var profile = Profile("scan.pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { SaveLocally = false };

        await Run(profile);

        var document = Assert.Single(_queue.Documents);
        Assert.NotNull(document.SavedPath);
        Assert.True(File.Exists(document.SavedPath), "the staged file must survive a failed upload");
    }

    [Fact]
    public async Task AStagingFileIsRemovedOnceEveryTargetSucceeded()
    {
        string? staged = null;
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(call =>
        {
            staged = call.Arg<ScannedDocument>().SavedPath;
            return true;
        });
        var profile = Profile("scan.pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with { SaveLocally = false };

        await Run(profile);

        Assert.NotNull(staged);
        Assert.False(File.Exists(staged));
    }

    [Fact]
    public async Task AKeptLocalCopyIsNotRemovedAfterASuccessfulUpload()
    {
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(true);

        await Run(Profile("scan.pdf"));

        Assert.True(File.Exists(Path.Combine(FolderPath, "scan.pdf")));
    }

    /// <summary>
    /// Uploading only covers PDF output: image output writes one file per page, which doesn't map to one
    /// document. Saying nothing would look like an upload that silently didn't happen.
    /// </summary>
    [Fact]
    public async Task ImageOutputReportsThatItCannotBeUploaded()
    {
        await Run(Profile("scan.jpg"));

        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<ScannedDocument>());
        _notify.Received().DocumentUploadFailed("scan.jpg", Arg.Any<string>());
    }

    [Fact]
    public async Task NothingIsUploadedWhenNoTargetIsEnabled()
    {
        await Run(Profile("scan.pdf", sap: false));

        Assert.Single(Folder.GetFiles());
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<ScannedDocument>());
    }

    /// <summary>
    /// A profile that requires an identification holds the document rather than filing it under a
    /// stand-in name. Nothing is written either -- a file named after nothing is the thing being avoided.
    /// </summary>
    [Fact]
    public async Task ADocumentWithoutARequiredIdentifierIsHeldBack()
    {
        var profile = Profile("$(id).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with
        {
            IdMode = DocumentIdMode.ManualInput,
            RequireIdentifier = true
        };

        await Run(profile);

        Assert.Empty(Folder.GetFiles());
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<ScannedDocument>());
        var document = Assert.Single(_queue.Documents);
        Assert.Equal(DocumentStatus.NeedsIdentifier, document.Status);
        // Outstanding, but the upload button must not act on it.
        Assert.True(_queue.HasOutstanding);
        Assert.False(_queue.HasReadyToUpload);
    }

    [Fact]
    public async Task EnteringTheIdentifierReleasesTheDocument()
    {
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(true);
        var profile = Profile("$(id).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with
        {
            IdMode = DocumentIdMode.ManualInput,
            RequireIdentifier = true
        };
        await Run(profile);
        var document = Assert.Single(_queue.Documents);

        document.SetIdentifier("4711", DocumentBarcodeSource.Manual);
        await CreatePipeline().Advance(document, triggeredByOperator: true);

        Assert.True(File.Exists(Path.Combine(FolderPath, "4711.pdf")));
        await _uploadService.Received(1).UploadAsync(document);
    }

    /// <summary>
    /// The same correction, but made after the document had already been written. A profile that files
    /// locally and uploads on the button writes the file as soon as the scan finishes, so by the time the
    /// operator fixes a misread barcode there is a file on disk carrying the old name.
    /// </summary>
    /// <remarks>
    /// The file used to be reused verbatim whenever one existed, while the SharePoint folder and the SAP
    /// object key were expanded from the identification at upload time -- so the correction reached the
    /// archive key and not the name, and the two silently parted company. Nothing afterwards can tell
    /// that from a correct scan.
    /// </remarks>
    [Fact]
    public async Task ACorrectionAfterTheDocumentWasWrittenReachesTheFileName()
    {
        string? uploadedName = null;
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(call =>
        {
            uploadedName = call.Arg<ScannedDocument>().FileName;
            return true;
        });
        var profile = Profile("$(id).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with
        {
            UploadTrigger = UploadTrigger.Manual,
            IdMode = DocumentIdMode.ManualInput
        };
        profile.DocumentWorkflow = profile.DocumentWorkflow with { RequireIdentifier = false };

        await Run(profile);
        var document = Assert.Single(_queue.Documents);
        document.SetIdentifier("1234", DocumentBarcodeSource.Manual);
        await CreatePipeline().Advance(document);
        Assert.True(File.Exists(Path.Combine(FolderPath, "1234.pdf")), "the first write should have happened");

        document.SetIdentifier("4711", DocumentBarcodeSource.Manual);
        await CreatePipeline().Advance(document, triggeredByOperator: true);

        Assert.Equal("4711.pdf", uploadedName);
        Assert.True(File.Exists(Path.Combine(FolderPath, "4711.pdf")));
        // The earlier file is the operator's; it is left where it is rather than deleted behind them.
        Assert.True(File.Exists(Path.Combine(FolderPath, "1234.pdf")));
    }

    /// <summary>
    /// The retry case: the upload failed, the operator corrected the barcode, and pressed upload again.
    /// The staged copy is ours, so it is replaced rather than left lying next to the new one.
    /// </summary>
    [Fact]
    public async Task ACorrectionBeforeARetryRestagesTheDocument()
    {
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(false);
        var profile = Profile("$(id).pdf");
        profile.DocumentWorkflow = profile.DocumentWorkflow! with
        {
            SaveLocally = false,
            IdMode = DocumentIdMode.ManualInput
        };

        await Run(profile);
        var document = Assert.Single(_queue.Documents);
        document.SetIdentifier("1234", DocumentBarcodeSource.Manual);
        await CreatePipeline().Advance(document, triggeredByOperator: true);
        var firstStaged = document.SavedPath;
        Assert.NotNull(firstStaged);
        Assert.Equal("1234.pdf", Path.GetFileName(firstStaged));

        string? uploadedName = null;
        _uploadService.UploadAsync(Arg.Any<ScannedDocument>()).Returns(call =>
        {
            uploadedName = call.Arg<ScannedDocument>().FileName;
            return true;
        });
        document.SetIdentifier("4711", DocumentBarcodeSource.Manual);
        await CreatePipeline().Advance(document, triggeredByOperator: true);

        Assert.Equal("4711.pdf", uploadedName);
        Assert.False(File.Exists(firstStaged), "the staged copy under the old name should be gone");
    }

    private async Task Run(ScanProfile profile)
    {
        var scanned = CreateScannedImages(ImageResources.dog);
        await CreatePipeline().Process(profile, scanned.ToAsyncEnumerable()).ToListAsync();
    }

    private ScanProfile Profile(string name, bool sap = true, string? folder = null) => new()
    {
        DisplayName = "Test",
        EnableAutoSave = true,
        DocumentWorkflow = new DocumentWorkflowSettings
        {
            Version = DocumentWorkflowSettings.CURRENT_VERSION,
            SaveLocally = true,
            LocalFolder = folder ?? FolderPath,
            DocumentNameTemplate = name,
            CleanupAfterCompletion = false
        },
        SapArchiveSettings = sap
            ? new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" }
            : null
    };
}
