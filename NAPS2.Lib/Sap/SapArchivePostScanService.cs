using System.Text.RegularExpressions;
using Eto.Forms;
using Microsoft.Extensions.Logging;
using NAPS2.EtoForms;
using NAPS2.EtoForms.Ui;
using NAPS2.Images;
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

    public async Task<bool> UploadSavedFileAsync(ScanProfile profile, string filePath, IReadOnlyList<ProcessedImage> images)
    {
        var settings = profile.SapArchiveSettings;
        if (settings?.EnableUpload != true)
        {
            return true;
        }

        var objectKey = ResolveObjectKey(settings, filePath, images);
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            objectKey = PromptForObjectKey(filePath);
        }
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            Log.Logger.LogWarning("SAP ArchiveLink upload skipped for {FilePath}: no object key", filePath);
            return false;
        }

        var connection = _config.Get(c => c.SapConnection);
        var fileName = Path.GetFileName(filePath);
        var description = ResolveDescription(settings.DescriptionTemplate, objectKey);
        var request = new SapUploadRequest(
            connection,
            settings,
            objectKey,
            await File.ReadAllBytesAsync(filePath),
            fileName,
            GetMimeType(filePath),
            description);

        var uploader = SapArchiveUploaderFactory.Create(connection);
        var op = new UploadSapArchiveOperation();
        if (op.Start(uploader, request))
        {
            _operationProgress.ShowProgress(op);
            return await op.Success;
        }
        return false;
    }

    private string? ResolveObjectKey(SapArchiveProfileSettings settings, string filePath, IReadOnlyList<ProcessedImage> images)
    {
        return settings.ObjectKeySource switch
        {
            ObjectKeySource.Fixed => settings.FixedObjectKey?.Trim(),
            ObjectKeySource.FromFilename => ExtractWithRegex(Path.GetFileNameWithoutExtension(filePath), settings.FilenameRegex),
            ObjectKeySource.FromBarcode => ResolveBarcodeObjectKey(settings, images),
            _ => null
        };
    }

    private string? ResolveBarcodeObjectKey(SapArchiveProfileSettings settings, IReadOnlyList<ProcessedImage> images)
    {
        var matches = images
            .Select(x => x.PostProcessingData.Barcode.DetectedText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => ExtractWithRegex(x!, settings.BarcodeRegex))
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
