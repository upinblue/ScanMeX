using System.Threading;
using NAPS2.ImportExport;
using NAPS2.Sap;
using NAPS2.Scan;

namespace NAPS2.PostScan;

public sealed class SapArchiveSink : IPostScanSink
{
    private readonly SapConnectionConfig _defaultConnection;
    private readonly IReadOnlyDictionary<string, SapConnectionConfig> _connections;
    private readonly Func<SapConnectionConfig, ISapArchiveUploader> _uploaderFactory;
    private readonly FileNamePlaceholders _placeholders = new();

    public SapArchiveSink(
        SapConnectionConfig defaultConnection,
        IEnumerable<SapConnectionConfig>? connections = null,
        Func<SapConnectionConfig, ISapArchiveUploader>? uploaderFactory = null)
    {
        _defaultConnection = defaultConnection;
        _connections = (connections ?? new[] { defaultConnection })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name!)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        _uploaderFactory = uploaderFactory ?? (cfg => new HttpSapArchiveUploader(cfg));
    }

    public string Name => PostScanSinkNames.Sap;

    public bool IsEnabledFor(ScanProfile profile) => profile.UploadsToSap();

    public async Task<PostScanSinkResult> ExecuteAsync(
        ScanContext ctx,
        IReadOnlyList<SavedArtifact> artifacts,
        CancellationToken ct)
    {
        var settings = ctx.Profile.SapArchiveSettings;
        if (settings?.EnableUpload != true)
        {
            return new PostScanSinkResult(Name, true, "SAP ArchiveLink skipped.", null);
        }
        if (artifacts.Count == 0)
        {
            return new PostScanSinkResult(Name, false, "No saved artifact available for SAP ArchiveLink upload.", null);
        }

        var connection = ResolveConnection(settings);
        var uploader = _uploaderFactory(connection);
        var successes = 0;
        var diagnostics = new Dictionary<string, string>();
        foreach (var artifact in artifacts)
        {
            var barcode = ResolveBarcode(settings, ctx);
            if (string.IsNullOrWhiteSpace(barcode))
            {
                return new PostScanSinkResult(Name, false,
                    "SAP ArchiveLink FEHLER – Barcode konnte nicht aufgelöst werden.", diagnostics);
            }

            var objectId = Substitute(settings.ObjectIdTemplate, ctx);
            var slug = string.IsNullOrWhiteSpace(settings.SlugTemplate)
                ? artifact.FileName
                : FileNamePlaceholders.SanitizeForFileName(Substitute(settings.SlugTemplate, ctx));
            var mimeType = string.IsNullOrWhiteSpace(artifact.MimeType)
                ? SapMimeTypeResolver.Resolve(slug)
                : artifact.MimeType;
            var result = await uploader.UploadAsync(new SapUploadRequest(
                connection,
                settings,
                barcode,
                objectId,
                await File.ReadAllBytesAsync(artifact.FilePath, ct),
                slug,
                mimeType), ct);

            diagnostics[artifact.FileName] = result.Success
                ? $"DocId={result.ArchivDocId}; Barcode={barcode}; Archive={settings.ArchiveId}"
                : $"HTTP={result.HttpStatusCode}; Code={result.ErrorCode}; Message={result.ErrorMessage}; TX={result.TransactionId}";
            if (!result.Success)
            {
                return new PostScanSinkResult(Name, false,
                    $"SAP ArchiveLink FEHLER – HTTP {result.HttpStatusCode} {result.ErrorCode}: {result.ErrorMessage} (TX: {result.TransactionId})",
                    diagnostics);
            }
            successes++;
        }
        return new PostScanSinkResult(Name, true,
            $"SAP ArchiveLink OK – {successes} Datei(en), Archiv: {settings.ArchiveId}", diagnostics);
    }

    private SapConnectionConfig ResolveConnection(SapArchiveProfileSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ConnectionName) && _connections.TryGetValue(settings.ConnectionName!, out var cfg))
        {
            return cfg;
        }
        return _defaultConnection;
    }

    private string ResolveBarcode(SapArchiveProfileSettings settings, ScanContext ctx)
    {
        return settings.BarcodeSourceForSap switch
        {
            BarcodeSourceForSap.Fixed => settings.FixedBarcode ?? string.Empty,
            BarcodeSourceForSap.Template => Substitute(settings.BarcodeTemplate, ctx),
            _ => ctx.SeparatorBarcodeValue ?? ctx.Barcodes.FirstOrDefault()?.Value ?? string.Empty
        };
    }

    private string Substitute(string? template, ScanContext ctx)
    {
        return string.IsNullOrWhiteSpace(template) ? string.Empty : _placeholders.SubstitutePlaceholders(template!, ctx);
    }
}
