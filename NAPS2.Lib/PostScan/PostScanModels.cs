using System.Threading;
using NAPS2.Scan;

namespace NAPS2.PostScan;

public interface IPostScanSink
{
    string Name { get; }
    bool IsEnabledFor(ScanProfile profile);
    Task<PostScanSinkResult> ExecuteAsync(ScanContext ctx, IReadOnlyList<SavedArtifact> artifacts, CancellationToken ct);
}

public interface IArtifactProducingPostScanSink : IPostScanSink
{
    IReadOnlyList<SavedArtifact> Artifacts { get; }
}

public record SavedArtifact(string FilePath, string FileName, long SizeBytes, string MimeType);

public record PostScanSinkResult(
    string SinkName,
    bool Success,
    string? Message,
    IReadOnlyDictionary<string, string>? Diagnostics);
