#nullable enable

using System.Threading;
using NAPS2.ImportExport;
using NAPS2.PostScan;
using NAPS2.Scan;
using NAPS2.Sap;
using NAPS2.Sdk.Tests;
using Xunit;

namespace NAPS2.Lib.Tests.PostScan;

public class PostScanOrchestratorTests : ContextualTests
{
    [Fact]
    public async Task ProcessAsync_RunsSinksInOrder()
    {
        var calls = new List<string>();
        var orchestrator = new PostScanOrchestrator(new IPostScanSink[]
        {
            new FakeSink(PostScanSinkNames.Sap, calls),
            new FakeAutoSaveSink(calls, true),
            new FakeSink(PostScanSinkNames.SharePoint, calls)
        });

        await orchestrator.ProcessAsync(Profile(enableUploads: true), CreateScannedImages(ImageResources.dog), CancellationToken.None);

        Assert.Equal(new[] { PostScanSinkNames.AutoSave, PostScanSinkNames.SharePoint, PostScanSinkNames.Sap }, calls);
    }

    [Fact]
    public async Task ProcessAsync_SharePointFailureDoesNotStopSap()
    {
        var calls = new List<string>();
        var orchestrator = new PostScanOrchestrator(new IPostScanSink[]
        {
            new FakeAutoSaveSink(calls, true),
            new FakeSink(PostScanSinkNames.SharePoint, calls, success: false),
            new FakeSink(PostScanSinkNames.Sap, calls)
        });

        var results = await orchestrator.ProcessAsync(Profile(enableUploads: true), CreateScannedImages(ImageResources.dog), CancellationToken.None);

        Assert.Contains(results, x => x.SinkName == PostScanSinkNames.SharePoint && !x.Success);
        Assert.Contains(results, x => x.SinkName == PostScanSinkNames.Sap && x.Success);
        Assert.Equal(new[] { PostScanSinkNames.AutoSave, PostScanSinkNames.SharePoint, PostScanSinkNames.Sap }, calls);
    }

    [Fact]
    public async Task ProcessAsync_AutoSaveFailureSkipsUploadSinks()
    {
        var calls = new List<string>();
        var orchestrator = new PostScanOrchestrator(new IPostScanSink[]
        {
            new FakeAutoSaveSink(calls, false),
            new FakeSink(PostScanSinkNames.SharePoint, calls),
            new FakeSink(PostScanSinkNames.Sap, calls)
        });

        var results = await orchestrator.ProcessAsync(Profile(enableUploads: true), CreateScannedImages(ImageResources.dog), CancellationToken.None);

        Assert.Equal(new[] { PostScanSinkNames.AutoSave }, calls);
        Assert.Contains(results, x => x.SinkName == PostScanSinkNames.SharePoint && !x.Success);
        Assert.Contains(results, x => x.SinkName == PostScanSinkNames.Sap && !x.Success);
    }

    [Fact]
    public void PatchTSegmentation_KeepsSeparatorAsFirstPageOfNewSegment()
    {
        var images = CreateScannedImages(ImageResources.dog, ImageResources.patcht, ImageResources.dog_gray, ImageResources.patcht, ImageResources.dog_h_n300);
        images[1] = images[1].WithPostProcessingData(images[1].PostProcessingData with
        {
            Barcode = new Barcode(true, true, "PATCHT", "CODE_39")
        }, true);
        images[3] = images[3].WithPostProcessingData(images[3].PostProcessingData with
        {
            Barcode = new Barcode(true, true, "PATCHT", "CODE_39")
        }, true);
        var orchestrator = new PostScanOrchestrator(Array.Empty<IPostScanSink>());

        var contexts = orchestrator.BuildContextsForTesting(Profile(separator: SaveSeparator.PatchT, barcodeRecognition: true), images);

        Assert.Equal(3, contexts.Count);
        Assert.Null(contexts[0].SeparatorBarcodeValue);
        Assert.Null(contexts[1].SeparatorBarcodeValue);
        Assert.Null(contexts[2].SeparatorBarcodeValue);
    }

    [Fact]
    public async Task AutoSaveSink_UnresolvedPlaceholderFailsClearly()
    {
        var sink = new AutoSaveSink();
        var profile = Profile(filePath: Path.Combine(FolderPath, "scan_$(unknown).pdf"));
        var ctx = new ScanContext { Profile = profile, Timestamp = DateTime.Now, Images = CreateScannedImages(ImageResources.dog) };

        var result = await sink.ExecuteAsync(ctx, Array.Empty<SavedArtifact>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Unaufgelöster Platzhalter", result.Message);
    }

    private ScanProfile Profile(
        bool enableUploads = false,
        SaveSeparator separator = SaveSeparator.FilePerScan,
        bool barcodeRecognition = false,
        string? filePath = null)
    {
        return new ScanProfile
        {
            DisplayName = "Test Profile",
            EnableAutoSave = true,
            AutoSaveSettings = new AutoSaveSettings
            {
                FilePath = filePath ?? Path.Combine(FolderPath, "scan_$(n).pdf"),
                Separator = separator
            },
            EnableSharePointUpload = enableUploads,
            BarcodeRecognitionEnabled = barcodeRecognition,
            SapArchiveSettings = enableUploads ? new SapArchiveProfileSettings { EnableUpload = true, ArchiveId = "PS" } : null
        };
    }

    private sealed class FakeSink : IPostScanSink
    {
        private readonly List<string> _calls;
        private readonly bool _success;

        public FakeSink(string name, List<string> calls, bool success = true)
        {
            Name = name;
            _calls = calls;
            _success = success;
        }

        public string Name { get; }
        public bool IsEnabledFor(ScanProfile profile) => true;

        public Task<PostScanSinkResult> ExecuteAsync(ScanContext ctx, IReadOnlyList<SavedArtifact> artifacts, CancellationToken ct)
        {
            _calls.Add(Name);
            return Task.FromResult(new PostScanSinkResult(Name, _success, Name, null));
        }
    }

    private sealed class FakeAutoSaveSink : IArtifactProducingPostScanSink
    {
        private readonly List<string> _calls;
        private readonly bool _success;

        public FakeAutoSaveSink(List<string> calls, bool success)
        {
            _calls = calls;
            _success = success;
        }

        public string Name => PostScanSinkNames.AutoSave;
        public IReadOnlyList<SavedArtifact> Artifacts { get; private set; } = Array.Empty<SavedArtifact>();
        public bool IsEnabledFor(ScanProfile profile) => true;

        public Task<PostScanSinkResult> ExecuteAsync(ScanContext ctx, IReadOnlyList<SavedArtifact> artifacts, CancellationToken ct)
        {
            _calls.Add(Name);
            Artifacts = _success
                ? new[] { new SavedArtifact(Path.Combine(Path.GetTempPath(), "x.pdf"), "x.pdf", 1, "application/pdf") }
                : Array.Empty<SavedArtifact>();
            return Task.FromResult(new PostScanSinkResult(Name, _success, Name, null));
        }
    }
}
