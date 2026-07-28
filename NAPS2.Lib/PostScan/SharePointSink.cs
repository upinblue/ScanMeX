using System.Threading;
using NAPS2.ImportExport;
using NAPS2.Scan;
using NAPS2.SharePoint;

namespace NAPS2.PostScan;

public sealed class SharePointSink : IPostScanSink
{
    private readonly SharePointUploadService _uploadService;
    private readonly FileNamePlaceholders _placeholders = new();

    public SharePointSink(SharePointUploadService? uploadService = null)
    {
        _uploadService = uploadService ?? new SharePointUploadService();
    }

    public string Name => PostScanSinkNames.SharePoint;

    public bool IsEnabledFor(ScanProfile profile) => profile.UploadsToSharePoint();

    public async Task<PostScanSinkResult> ExecuteAsync(
        ScanContext ctx,
        IReadOnlyList<SavedArtifact> artifacts,
        CancellationToken ct)
    {
        if (artifacts.Count == 0)
        {
            return new PostScanSinkResult(Name, false, "No saved artifact available for SharePoint upload.", null);
        }

        try
        {
            foreach (var artifact in artifacts)
            {
                var fileName = FileNamePlaceholders.SanitizeForFileName(
                    _placeholders.SubstitutePlaceholders(artifact.FileName, ctx));
                await _uploadService.UploadFileAsync(ctx.Profile.SharePointUploadSettings, artifact.FilePath, fileName,
                    cancellationToken: ct);
            }
            return new PostScanSinkResult(Name, true, $"SharePoint-Upload OK – {artifacts.Count} file(s)", null);
        }
        catch (Exception ex)
        {
            return new PostScanSinkResult(Name, false, ex.Message, null);
        }
    }
}
