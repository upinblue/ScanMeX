using System.Threading;
using Microsoft.Extensions.Logging;
using NAPS2.ImportExport;
using NAPS2.Scan;

namespace NAPS2.PostScan;

public sealed class PostScanOrchestrator
{
    private readonly IReadOnlyList<IPostScanSink> _sinks;
    private readonly BarcodeExtractor _barcodeExtractor;

    public PostScanOrchestrator(IEnumerable<IPostScanSink> sinks, BarcodeExtractor? barcodeExtractor = null)
    {
        _sinks = sinks.ToList();
        _barcodeExtractor = barcodeExtractor ?? new BarcodeExtractor();
    }

    public async Task<IReadOnlyList<PostScanSinkResult>> ProcessAsync(
        ScanProfile profile,
        IReadOnlyList<ProcessedImage> images,
        CancellationToken ct)
    {
        var activeSinks = _sinks.Where(x => x.IsEnabledFor(profile)).ToList();
        if (activeSinks.Count == 0)
        {
            return Array.Empty<PostScanSinkResult>();
        }

        var allBarcodes = ShouldExtractBarcodes(profile)
            ? _barcodeExtractor.Extract(images)
            : Array.Empty<DetectedBarcode>();
        var segments = BuildSegments(profile, images, allBarcodes).ToList();
        var results = new List<PostScanSinkResult>();

        foreach (var segment in segments)
        {
            var artifacts = new List<SavedArtifact>();
            var autoSaveFailed = false;
            foreach (var sink in activeSinks.OrderBy(SinkOrder))
            {
                if (autoSaveFailed && sink.Name != PostScanSinkNames.AutoSave)
                {
                    results.Add(new PostScanSinkResult(sink.Name, false,
                        "Skipped because AutoSave failed and no saved artifact is available.", null));
                    continue;
                }

                var started = DateTime.UtcNow;
                var result = await sink.ExecuteAsync(segment, artifacts, ct);
                var elapsed = DateTime.UtcNow - started;
                Log.Logger.LogInformation("Post-scan sink {SinkName} completed in {ElapsedMs}ms. Success={Success}. Message={Message}",
                    sink.Name, elapsed.TotalMilliseconds, result.Success, result.Message);
                results.Add(result);

                if (sink is IArtifactProducingPostScanSink artifactSink)
                {
                    artifacts.Clear();
                    artifacts.AddRange(artifactSink.Artifacts);
                }
                if (sink.Name == PostScanSinkNames.AutoSave && !result.Success)
                {
                    autoSaveFailed = true;
                }
            }
        }
        return results;
    }

    public IReadOnlyList<ScanContext> BuildContextsForTesting(ScanProfile profile, IReadOnlyList<ProcessedImage> images)
    {
        var barcodes = ShouldExtractBarcodes(profile) ? _barcodeExtractor.Extract(images) : Array.Empty<DetectedBarcode>();
        return BuildSegments(profile, images, barcodes).ToList();
    }

    private bool ShouldExtractBarcodes(ScanProfile profile)
    {
        if (profile.BarcodeRecognitionEnabled || DocumentWorkflowSettings.ForProfile(profile).RequiresBarcodeDetection())
        {
            return true;
        }
        return ContainsBarcodeTemplate(profile.AutoSaveSettings?.FilePath) ||
               ContainsBarcodeTemplate(profile.SharePointUploadSettings?.FolderPath) ||
               ContainsBarcodeTemplate(profile.SharePointUploadSettings?.LibraryNameOrPath) ||
               ContainsBarcodeTemplate(profile.SapArchiveSettings?.ObjectIdTemplate) ||
               ContainsBarcodeTemplate(profile.SapArchiveSettings?.DescriptionTemplate) ||
               ContainsBarcodeTemplate(profile.SapArchiveSettings?.SlugTemplate) ||
               ContainsBarcodeTemplate(profile.SapArchiveSettings?.BarcodeTemplate);
    }

    private static bool ContainsBarcodeTemplate(string? value) =>
        value?.IndexOf("$(barcode", StringComparison.OrdinalIgnoreCase) >= 0;

    private IEnumerable<ScanContext> BuildSegments(
        ScanProfile profile,
        IReadOnlyList<ProcessedImage> images,
        IReadOnlyList<DetectedBarcode> barcodes)
    {
        var now = DateTime.Now;
        var workflow = DocumentWorkflowSettings.ForProfile(profile);
        var sequenceIndex = 0;
        foreach (var segment in DocumentSeparator.Separate(images, workflow))
        {
            yield return CreateContext(profile, segment.Images, barcodes, now, sequenceIndex++,
                segment.SeparatorBarcodeValue, segment.StartPageIndex);
        }
    }

    private static ScanContext CreateContext(
        ScanProfile profile,
        IReadOnlyList<ProcessedImage> images,
        IReadOnlyList<DetectedBarcode> allBarcodes,
        DateTime timestamp,
        int sequenceIndex,
        string? separatorBarcodeValue,
        int segmentStartPage)
    {
        var segmentBarcodes = allBarcodes
            .Where(x => x.PageIndex >= segmentStartPage && x.PageIndex < segmentStartPage + images.Count)
            .Select(x => x with { PageIndex = x.PageIndex - segmentStartPage })
            .ToList();
        if (segmentBarcodes.Count == 0)
        {
            segmentBarcodes = images.Select((img, idx) => new { img, idx })
                .Where(x => x.img.PostProcessingData.Barcode.IsDetected && !string.IsNullOrWhiteSpace(x.img.PostProcessingData.Barcode.DetectedText))
                .Select(x => new DetectedBarcode(
                    x.img.PostProcessingData.Barcode.DetectedText!,
                    x.img.PostProcessingData.Barcode.IsPatchT ? "PATCH_T" : x.img.PostProcessingData.Barcode.DetectedFormat ?? string.Empty,
                    x.idx,
                    x.img.PostProcessingData.Barcode.IsPatchT))
                .ToList();
        }
        var ext = Path.GetExtension(profile.AutoSaveSettings?.FilePath ?? "scan.pdf").TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = "pdf";
        }
        return new ScanContext
        {
            Timestamp = timestamp,
            SequenceIndex = sequenceIndex,
            Profile = profile,
            Images = images,
            Barcodes = segmentBarcodes,
            SeparatorBarcodeValue = separatorBarcodeValue,
            OutputExtension = ext,
            FileFormat = ext
        };
    }

    private static int SinkOrder(IPostScanSink sink) => sink.Name switch
    {
        PostScanSinkNames.AutoSave => 0,
        PostScanSinkNames.SharePoint => 1,
        PostScanSinkNames.Sap => 2,
        _ => 100
    };
}

public static class PostScanSinkNames
{
    public const string AutoSave = "AutoSave";
    public const string SharePoint = "SharePoint";
    public const string Sap = "Sap";
}
