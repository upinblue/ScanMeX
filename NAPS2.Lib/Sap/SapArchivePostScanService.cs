using System.Text.RegularExpressions;
using Eto.Forms;
using Microsoft.Extensions.Logging;
using NAPS2.EtoForms;
using NAPS2.EtoForms.Ui;
using NAPS2.Images;
using NAPS2.ImportExport;
using NAPS2.Scan;

namespace NAPS2.Sap;

internal class SapArchivePostScanService
{
    private readonly Naps2Config _config;
    private readonly OperationProgress _operationProgress;

    public SapArchivePostScanService(Naps2Config config, OperationProgress operationProgress)
    {
        _config = config;
        _operationProgress = operationProgress;
    }

    public async Task<bool> UploadSavedFileAsync(ScanProfile profile, string filePath, IReadOnlyList<ProcessedImage> images,
        ScanContext ctx)
    {
        var settings = profile.SapArchiveSettings;
        if (settings == null)
        {
            return true;
        }

        var barcode = ResolveBarcode(settings, filePath, images, ctx);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            if (settings.BarcodeSource == BarcodeSource.PromptUser)
            {
                barcode = PromptForObjectKey(filePath);
            }
            else
            {
                Log.Logger.LogError("SAP ArchiveLink upload skipped for {FilePath}: no barcode found for source {BarcodeSource}",
                    filePath, settings.BarcodeSource);
                return false;
            }
        }
        if (string.IsNullOrWhiteSpace(barcode))
        {
            Log.Logger.LogWarning("SAP ArchiveLink upload skipped for {FilePath}: no barcode", filePath);
            return false;
        }

        var connection = settings.Connection ?? _config.Get(c => c.SapConnection);
        var fileName = Path.GetFileName(filePath);
        var objectId = ResolveObjectId(settings.ObjectId, barcode, ctx);
        var request = new SapUploadRequest(
            connection,
            settings,
            barcode,
            objectId,
            await File.ReadAllBytesAsync(filePath),
            fileName,
            SapMimeTypeResolver.Resolve(filePath));

        var uploader = new HttpSapArchiveUploader(connection);
        var op = new UploadSapArchiveOperation();
        if (op.Start(uploader, request))
        {
            _operationProgress.ShowProgress(op);
            return await op.Success;
        }
        return false;
    }

    private string? ResolveBarcode(SapArchiveProfileSettings settings, string filePath, IReadOnlyList<ProcessedImage> images,
        ScanContext ctx)
    {
        return settings.BarcodeSource switch
        {
            BarcodeSource.Fixed => Substitute(settings.FixedBarcode, ctx)?.Trim(),
            BarcodeSource.FromFilename => ExtractWithRegex(Path.GetFileNameWithoutExtension(filePath), Substitute(settings.BarcodeRegex, ctx)),
            BarcodeSource.FromScannedBarcode => ResolveScannedBarcode(settings, images, ctx),
            _ => null
        };
    }

    private string? ResolveScannedBarcode(SapArchiveProfileSettings settings, IReadOnlyList<ProcessedImage> images,
        ScanContext ctx)
    {
        var pattern = Substitute(settings.BarcodeRegex, ctx);
        var matches = images
            .Select(x => x.PostProcessingData.Barcode.DetectedText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => ExtractWithRegex(x!, pattern))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? ExtractWithRegex(string value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        var match = Regex.Match(value, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }
        for (var i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success)
            {
                return match.Groups[i].Value.Trim();
            }
        }
        return match.Value.Trim();
    }

    private string? PromptForObjectKey(string filePath)
    {
        return Invoker.Current.InvokeGet(() =>
        {
            var form = new SapObjectKeyPromptForm(_config, Path.GetFileName(filePath));
            form.ShowModal(Application.Instance?.MainForm);
            return form.ObjectKey;
        });
    }

    private static string? ResolveObjectId(string? template, string barcode, ScanContext ctx)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }
        return Substitute(template, ctx)?
            .Replace("{barcode}", barcode, StringComparison.OrdinalIgnoreCase)
            .Replace("$(barcode)", barcode, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Substitute(string? template, ScanContext ctx)
    {
        return string.IsNullOrWhiteSpace(template) ? template : new FileNamePlaceholders().SubstitutePlaceholders(template!, ctx);
    }

    private static string ResolveDescription(string? template, string objectKey)
    {
        var value = string.IsNullOrWhiteSpace(template) ? "ScanMe {date} {objectkey}" : template!;
        return value
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{user}", Environment.UserName, StringComparison.OrdinalIgnoreCase)
            .Replace("{objectkey}", objectKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMimeType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
    }
}
