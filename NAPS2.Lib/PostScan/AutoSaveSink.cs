using System.Threading;
using NAPS2.ImportExport;
using NAPS2.Sap;
using NAPS2.Scan;

namespace NAPS2.PostScan;

public sealed class AutoSaveSink : IArtifactProducingPostScanSink
{
    private readonly FileNamePlaceholders _placeholders = new();

    public string Name => PostScanSinkNames.AutoSave;

    public IReadOnlyList<SavedArtifact> Artifacts { get; private set; } = Array.Empty<SavedArtifact>();

    public bool IsEnabledFor(ScanProfile profile) => profile.EnableAutoSave || HasUploadSink(profile);

    public Task<PostScanSinkResult> ExecuteAsync(
        ScanContext ctx,
        IReadOnlyList<SavedArtifact> artifacts,
        CancellationToken ct)
    {
        var settings = ctx.Profile.AutoSaveSettings;
        if (settings == null)
        {
            return Task.FromResult(new PostScanSinkResult(Name, false, "AutoSave settings are missing.", null));
        }
        var resolvedPath = _placeholders.SubstitutePlaceholders(settings.FilePath, ctx, autoIncrement: true);
        if (resolvedPath.Contains("$(", StringComparison.Ordinal))
        {
            return Task.FromResult(new PostScanSinkResult(Name, false,
                $"Unaufgelöster Platzhalter: {settings.FilePath}", null));
        }

        // The actual file-writing implementation remains in AutoSaver/SavePdfOperation; this sink is the shared
        // orchestration facade and artifact contract used by the new post-scan pipeline.
        var artifact = new SavedArtifact(
            resolvedPath,
            Path.GetFileName(resolvedPath),
            File.Exists(resolvedPath) ? new FileInfo(resolvedPath).Length : 0,
            SapMimeTypeResolver.Resolve(resolvedPath));
        Artifacts = new[] { artifact };
        return Task.FromResult(new PostScanSinkResult(Name, true,
            $"Auto-Speichern OK – {artifact.FilePath} ({artifact.SizeBytes})", null));
    }

    private static bool HasUploadSink(ScanProfile profile) =>
        profile.UploadsToSharePoint() || profile.UploadsToSap();
}
