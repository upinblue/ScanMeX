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
/// still lets the other run, and that the operator is told which half got through.
/// </summary>
/// <remarks>
/// The staging file's lifetime used to be decided here too; it now belongs to
/// <see cref="Scan.DocumentPipelineTests"/>, which is where writing and deleting the file happens.
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

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Contains("403 Forbidden", document.Message);
        Assert.Contains("HTTP 500", document.Message);
        _notify.Received().DocumentUploadFailed(document.FileName, document.Message!);
    }

    [Fact]
    public async Task ASucceededUploadIsMarkedDoneAndNotified()
    {
        var service = CreateService();
        var document = CreateDocument(sharePoint: true, sap: true);

        var success = await service.UploadAsync(document);

        Assert.True(success);
        Assert.Equal(DocumentStatus.Done, document.Status);
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
    /// One target failing does not undo the other, so the document has to record which one it reached.
    /// Without that the list can only say "failed", and an operator retrying it has no way to know the
    /// document is already in SharePoint and only SAP is outstanding.
    /// </summary>
    [Fact]
    public async Task TheTargetThatSucceededIsRecordedEvenWhenTheOtherFailed()
    {
        var service = CreateService(sapError: "HTTP 500");
        var document = CreateDocument(sharePoint: true, sap: true);

        await service.UploadAsync(document);

        Assert.Equal(new[] { "SharePoint" }, document.CompletedTargets);
    }

    [Fact]
    public async Task ARetryDoesNotAccumulateTargets()
    {
        var service = CreateService();
        var document = CreateDocument(sharePoint: true, sap: true);

        await service.UploadAsync(document);
        await service.UploadAsync(document);

        Assert.Equal(2, document.CompletedTargets.Count);
    }

    private FakeUploadService CreateService(string? sharePointError = null, string? sapError = null) =>
        new(Naps2Config.Stub(), Substitute.For<OperationProgress>(), _notify, sharePointError, sapError);

    private ScannedDocument CreateDocument(bool sharePoint = false, bool sap = false)
    {
        var path = Path.Combine(FolderPath, "4711.pdf");
        File.WriteAllText(path, "pdf");
        var profile = new ScanProfile
        {
            DisplayName = "Test",
            // The flag is what enables the target; the settings object is always present on a profile.
            EnableSharePointUpload = sharePoint,
            SharePointUploadSettings = new SharePointUploadSettings { SiteUrl = "https://x" },
            SapArchiveSettings = sap
                ? new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" }
                : null
        };
        return new ScannedDocument
        {
            Profile = profile,
            ScannedPages = CreateScannedImages(ImageResources.dog),
            SequenceIndex = 0,
            SavedPath = path
        };
    }

    /// <summary>
    /// Replaces only the two network calls, so everything the service does around them -- the ordering,
    /// the failure aggregation and the notifications -- is the real code.
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

        protected override Task<string?> UploadToSharePointAsync(ScannedDocument document)
        {
            _attempted.Add("SharePoint");
            return Task.FromResult(_sharePointError);
        }

        protected override Task<string?> UploadToSapAsync(ScannedDocument document)
        {
            _attempted.Add("SAP");
            return Task.FromResult(_sapError);
        }
    }
}
