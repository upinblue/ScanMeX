#nullable enable
using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.PostScan;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using NSubstitute;
using Xunit;

namespace NAPS2.Lib.Tests.Scan;

/// <summary>
/// The hand-off from saving a document to uploading it. Auto save is what produces the file the upload
/// runs on, so everything here is about the cases where that hand-off must not happen, or must not be
/// the end of the line: a document that failed to save has nothing to upload, and a document whose
/// upload failed has to stay retryable rather than being quietly left unarchived.
/// </summary>
/// <remarks>
/// These behaviours were previously only described by PostScanOrchestratorTests, against fake sinks in a
/// pipeline the shipped app never ran.
/// </remarks>
public class AutoSaverUploadTests : ContextualTests
{
    private readonly ErrorOutput _errorOutput = Substitute.For<ErrorOutput>();
    private readonly ISaveNotify _notify = Substitute.For<ISaveNotify>();
    private readonly DocumentUploadQueue _queue = new();
    private readonly DocumentUploadService _uploadService =
        Substitute.For<DocumentUploadService>(Naps2Config.Stub(), Substitute.For<OperationProgress>(),
            Substitute.For<ISaveNotify>());

    private AutoSaver CreateAutoSaver() => new(
        _errorOutput,
        Substitute.For<DialogHelper>(),
        Substitute.For<OperationProgress>(),
        _notify,
        new PdfExporter(ScanningContext),
        Substitute.For<IOverwritePrompt>(),
        Naps2Config.Stub(),
        ImageContext,
        new UiImageList(),
        _queue,
        _uploadService);

    [Fact]
    public async Task AnUnresolvedPlaceholderFailsVisiblyAndUploadsNothing()
    {
        // $(unknown) survives substitution, so the path would be written literally. Naming the document
        // after an unexpanded placeholder is worse than not saving it, and uploading it is worse still.
        var profile = Profile(Path.Combine(FolderPath, "scan_$(unknown).pdf"));

        await Save(profile);

        _errorOutput.Received().DisplayError(Arg.Is<string>(x => x.Contains("$(unknown)")));
        Assert.Empty(Folder.GetFiles());
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<PendingDocument>());
        Assert.Empty(_queue.Documents);
    }

    [Fact]
    public async Task AFailedSaveUploadsNothing()
    {
        // A file where the target folder has to go: creating the directory fails, so the save fails.
        var blocked = Path.Combine(FolderPath, "sub");
        File.WriteAllText(blocked, "not a folder");
        var profile = Profile(Path.Combine(blocked, "scan.pdf"));

        await Save(profile);

        // Asserted so the test can't pass merely because nothing ran: the save has to have been
        // attempted and failed, leaving no document for the upload to pick up.
        Assert.False(File.Exists(Path.Combine(blocked, "scan.pdf")));
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<PendingDocument>());
        Assert.Empty(_queue.Documents);
    }

    [Fact]
    public async Task ASavedDocumentIsUploadedAutomatically()
    {
        _uploadService.UploadAsync(Arg.Any<PendingDocument>()).Returns(true);
        var profile = Profile(Path.Combine(FolderPath, "scan.pdf"));

        await Save(profile);

        await _uploadService.Received(1).UploadAsync(
            Arg.Is<PendingDocument>(x => x.FileName == "scan.pdf" && x.PageCount == 1));
        Assert.Empty(_queue.Documents);
    }

    /// <summary>
    /// A SAP outage or a network glitch used to leave the document saved locally with nothing recording
    /// that it never reached the archive. In the queue it keeps the upload button enabled and can be
    /// retried once the cause is fixed.
    /// </summary>
    [Fact]
    public async Task AFailedAutomaticUploadIsQueuedForRetry()
    {
        _uploadService.UploadAsync(Arg.Any<PendingDocument>()).Returns(false);
        var profile = Profile(Path.Combine(FolderPath, "scan.pdf"));

        await Save(profile);

        var queued = Assert.Single(_queue.Documents);
        Assert.Equal("scan.pdf", queued.FileName);
        Assert.True(_queue.HasPending);
    }

    [Fact]
    public async Task TheManualTriggerQueuesWithoutUploading()
    {
        var profile = Profile(Path.Combine(FolderPath, "scan.pdf"));
        profile.DocumentWorkflow = new DocumentWorkflowSettings { UploadTrigger = UploadTrigger.Manual };

        await Save(profile);

        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<PendingDocument>());
        Assert.Single(_queue.Documents);
    }

    /// <summary>
    /// Uploading only covers the PDF output: image output writes one file per page, which doesn't map to
    /// one document. Saying nothing would look like an upload that silently didn't happen.
    /// </summary>
    [Fact]
    public async Task ImageOutputReportsThatItCannotBeUploaded()
    {
        var profile = Profile(Path.Combine(FolderPath, "scan.jpg"));

        await Save(profile);

        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<PendingDocument>());
        _notify.Received().DocumentUploadFailed("scan.jpg", Arg.Any<string>());
    }

    [Fact]
    public async Task NothingIsUploadedWhenNoTargetIsEnabled()
    {
        var profile = Profile(Path.Combine(FolderPath, "scan.pdf"), sap: false);

        await Save(profile);

        Assert.Single(Folder.GetFiles());
        await _uploadService.DidNotReceive().UploadAsync(Arg.Any<PendingDocument>());
        Assert.Empty(_queue.Documents);
    }

    private async Task Save(ScanProfile profile)
    {
        var scanned = CreateScannedImages(ImageResources.dog);
        await CreateAutoSaver().Save(profile, profile.AutoSaveSettings!, scanned.ToAsyncEnumerable())
            .ToListAsync();
    }

    private static ScanProfile Profile(string filePath, bool sap = true) => new()
    {
        DisplayName = "Test",
        EnableAutoSave = true,
        AutoSaveSettings = new AutoSaveSettings { FilePath = filePath },
        SapArchiveSettings = sap
            ? new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" }
            : null
    };
}
