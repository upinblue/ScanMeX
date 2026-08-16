#nullable enable
using NAPS2.EtoForms;
using NAPS2.EtoForms.Notifications;
using NAPS2.PostScan;
using NAPS2.Sap;
using NAPS2.Scan;
using NAPS2.Sdk.Tests;
using NAPS2.SharePoint;
using NSubstitute;
using Xunit;

namespace NAPS2.Lib.Tests.PostScan;

/// <summary>
/// The single path every document takes to the target systems, used by both the automatic and the manual
/// upload trigger. What matters here is what happens around the two uploads: that one target failing
/// still lets the other run, that the operator is told, and that a staging file is only removed once
/// everything actually succeeded.
/// </summary>
/// <remarks>
/// This replaces the sink-pipeline tests that used to describe the same behaviour against
/// PostScanOrchestrator, which nothing in the shipped app ever constructed.
/// </remarks>
public class DocumentUploadServiceTests : ContextualTests
{
    private readonly ISaveNotify _notify = Substitute.For<ISaveNotify>();

    [Fact]
    public async Task SharePointFailureStillTriesSap()
    {
        var service = CreateService(sharePointError: "403 Forbidden");
        var document = CreateDocument(sharePoint: true, sap: true);

        var success = await service.UploadAsync(document);

        Assert.False(success);
        Assert.Equal(new[] { "SharePoint", "SAP" }, service.AttemptedTargets);
    }

    [Fact]
    public async Task BothFailuresAreReportedTogether()
    {
        var service = CreateService(sharePointError: "403 Forbidden", sapError: "HTTP 500");
        var document = CreateDocument(sharePoint: true, sap: true);

        await service.UploadAsync(document);

        Assert.Equal(DocumentUploadStatus.Failed, document.Status);
        Assert.Contains("403 Forbidden", document.Message);
        Assert.Contains("HTTP 500", document.Message);
        _notify.Received().DocumentUploadFailed(document.FileName, document.Message!);
    }

    [Fact]
    public async Task ASucceededUploadIsMarkedUploadedAndNotified()
    {
        var service = CreateService();
        var document = CreateDocument(sharePoint: true, sap: true);

        var success = await service.UploadAsync(document);

        Assert.True(success);
        Assert.Equal(DocumentUploadStatus.Uploaded, document.Status);
        Assert.Null(document.Message);
        _notify.Received().DocumentUploaded(document.FileName, Arg.Any<string>());
    }

    [Fact]
    public async Task OnlyTheEnabledTargetsAreAttempted()
    {
        var service = CreateService();
        var document = CreateDocument(sharePoint: false, sap: true);

        await service.UploadAsync(document);

        Assert.Equal(new[] { "SAP" }, service.AttemptedTargets);
    }

    /// <summary>
    /// A profile that keeps no local copy writes the file only so it can be uploaded. Deleting it after a
    /// failure would destroy the only copy of a scan that never reached the archive.
    /// </summary>
    [Fact]
    public async Task AStagingFileSurvivesAFailedUpload()
    {
        var service = CreateService(sapError: "HTTP 500");
        var document = CreateDocument(sap: true, deleteAfterUpload: true);

        await service.UploadAsync(document);

        Assert.True(File.Exists(document.FilePath), "the staged file must survive a failed upload");
    }

    [Fact]
    public async Task AStagingFileIsRemovedOnceEveryTargetSucceeded()
    {
        var service = CreateService();
        var document = CreateDocument(sap: true, deleteAfterUpload: true);

        await service.UploadAsync(document);

        Assert.False(File.Exists(document.FilePath));
    }

    [Fact]
    public async Task AKeptLocalCopyIsNotRemovedAfterASuccessfulUpload()
    {
        var service = CreateService();
        var document = CreateDocument(sap: true, deleteAfterUpload: false);

        await service.UploadAsync(document);

        Assert.True(File.Exists(document.FilePath));
    }

    private FakeUploadService CreateService(string? sharePointError = null, string? sapError = null) =>
        new(Naps2Config.Stub(), Substitute.For<OperationProgress>(), _notify, sharePointError, sapError);

    private PendingDocument CreateDocument(
        bool sharePoint = false, bool sap = false, bool deleteAfterUpload = false)
    {
        var path = Path.Combine(FolderPath, "4711.pdf");
        File.WriteAllText(path, "pdf");
        var profile = new ScanProfile
        {
            DisplayName = "Test",
            // The flag is what enables the target; the settings object is always present on a profile.
            EnableSharePointUpload = sharePoint,
            SharePointUploadSettings = new SharePointUploadSettings { SiteUrl = "https://x" },
            SapArchiveSettings = sap ? new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" } : null
        };
        return new PendingDocument
        {
            Profile = profile,
            Context = new ScanContext { Profile = profile, Timestamp = DateTime.Now },
            FilePath = path,
            DeleteFileAfterUpload = deleteAfterUpload
        };
    }

    /// <summary>
    /// Replaces only the two network calls, so everything the service does around them -- the ordering,
    /// the failure aggregation, the notifications and the staging-file cleanup -- is the real code.
    /// </summary>
    private sealed class FakeUploadService : DocumentUploadService
    {
        private readonly string? _sharePointError;
        private readonly string? _sapError;
        private readonly List<string> _attempted = [];

        public FakeUploadService(Naps2Config config, OperationProgress progress, ISaveNotify notify,
            string? sharePointError, string? sapError) : base(config, progress, notify)
        {
            _sharePointError = sharePointError;
            _sapError = sapError;
        }

        public IReadOnlyList<string> AttemptedTargets => _attempted;

        protected override Task<string?> UploadToSharePointAsync(PendingDocument document)
        {
            _attempted.Add("SharePoint");
            return Task.FromResult(_sharePointError);
        }

        protected override Task<string?> UploadToSapAsync(PendingDocument document)
        {
            _attempted.Add("SAP");
            return Task.FromResult(_sapError);
        }
    }
}
