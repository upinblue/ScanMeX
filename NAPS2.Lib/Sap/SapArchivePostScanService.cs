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

    /// <summary>
    /// Uploads a saved file to SAP ArchiveLink. Returns null on success, otherwise the reason it failed
    /// so the caller can tell the operator what went wrong rather than just that something did.
    /// </summary>
    public async Task<string?> UploadSavedFileAsync(ScanProfile profile, string filePath,
        IReadOnlyList<ProcessedImage> images, ScanContext ctx)
    {
        var settings = profile.SapArchiveSettings;
        if (settings == null)
        {
            ScanConsole.Upload("SAP ArchiveLink skipped: the profile has no SAP settings.");
            return null;
        }

        var barcode = ResolveBarcode(settings, filePath, images, ctx);
        // ArObject and SapObject are the two values that actually go out as headers; ArDocType is a legacy
        // field the OData upload never sends, so naming it here suggested a setting that has no effect.
        ScanConsole.Upload(
            $"SAP object key from {settings.BarcodeSource}: '{barcode ?? "(none)"}' " +
            $"(Archive='{settings.ArchiveId}', ArObject='{settings.ArObject}', SapObject='{settings.SapObject}')");
        if (string.IsNullOrWhiteSpace(barcode))
        {
            if (settings.BarcodeSource == BarcodeSource.PromptUser)
            {
                barcode = PromptForObjectKey(filePath);
                ScanConsole.Upload($"SAP object key entered by the operator: '{barcode ?? "(cancelled)"}'");
            }
            else
            {
                Log.Logger.LogError("SAP ArchiveLink upload skipped for {FilePath}: no barcode found for source {BarcodeSource}",
                    filePath, settings.BarcodeSource);
                return UiStrings.SapNoObjectKey;
            }
        }
        if (string.IsNullOrWhiteSpace(barcode))
        {
            Log.Logger.LogWarning("SAP ArchiveLink upload skipped for {FilePath}: no barcode", filePath);
            return UiStrings.SapNoObjectKey;
        }

        // A profile saved before the connection moved onto the profile has none of its own and silently
        // uses the global one, which is only validated in its own dialog. Say which one is in use so a
        // profile pointing at the wrong system isn't invisible.
        var connection = settings.Connection;
        if (connection == null)
        {
            connection = _config.Get(c => c.SapConnection);
            ScanConsole.Upload(
                "This profile has no SAP connection of its own, so the global SAP connection from " +
                $"Settings is used: Host='{connection.Host ?? ""}', Client='{connection.Client ?? ""}'.");
        }
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

        ScanConsole.Upload(
            $"SAP request: Host='{connection.Host}', Service='{connection.ServiceName}', Client='{connection.Client}', " +
            $"User='{connection.User}', File='{fileName}', ObjectId='{objectId ?? "(none)"}'");

        // Disposed once the upload has finished: the uploader owns an HttpClient and its handler, and a
        // batch creates one per document. Without this each document leaves a connection pool open until
        // the garbage collector gets to it, which on a station that scans all day adds up.
        using var uploader = new HttpSapArchiveUploader(connection);
        var op = new UploadSapArchiveOperation();
        if (!op.Start(uploader, request))
        {
            ScanConsole.Upload("SAP upload could not be started.");
            return UiStrings.SapUploadNotStarted;
        }
        // Background rather than modal: a batch produces one upload per document, and a modal dialog per
        // document would block the window throughout. The progress notification still opens the full
        // dialog when clicked.
        _operationProgress.ShowBackgroundProgress(op);
        if (await op.Success)
        {
            ScanConsole.Upload($"SAP upload OK. ArchivDocId='{op.Result?.ArchivDocId}'");
            return null;
        }
        var failure = op.FailureMessage ?? UiStrings.SapUploadNotStarted;
        ScanConsole.Upload($"SAP upload failed: {failure}");
        return failure;
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
        var primaries = images.Select(x => x.PostProcessingData.Barcode.DetectedText).ToList();
        // Everything else the pages carry. A page with an order code and a document code has only one of
        // them as its primary, and which one that is comes down to reading order, not to what the
        // operator meant -- so the regex has to be able to reach the others.
        var secondaries = images
            .SelectMany(x => x.PostProcessingData.Barcode.GetAllValues().Select(v => v.Text))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Except(primaries.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal)
            .ToList();
        if (secondaries.Count > 0)
        {
            ScanConsole.Upload(
                $"The document's pages carry {secondaries.Count} further barcode(s) besides the page primaries: " +
                string.Join(", ", secondaries.Select(x => $"'{x}'")) +
                $"; SAP regex '{pattern ?? ""}' selects the object key.");
        }
        var key = SapObjectKeyResolver.FromScannedBarcodes(
            ctx.SeparatorBarcodeValue,
            primaries,
            secondaries,
            pattern);

        var candidates = primaries.Concat(secondaries).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (key == null && candidates.Count > 0)
        {
            // The pages did carry barcodes and none of them survived. Left unsaid this is indistinguishable
            // from a page where nothing was decoded at all, and the operator has no way to tell that the
            // regex is what rejected the value.
            ScanConsole.Upload(
                $"No SAP object key: none of the document's barcodes " +
                $"({string.Join(", ", candidates.Select(x => $"'{x}'"))}) produced a single value under the " +
                $"regex '{pattern ?? ""}'.");
        }

        if (!string.IsNullOrWhiteSpace(ctx.SeparatorBarcodeValue) && key == ctx.SeparatorBarcodeValue?.Trim())
        {
            ScanConsole.Upload($"SAP object key taken from the document's separator barcode: '{key}'");
        }
        else if (!string.IsNullOrWhiteSpace(ctx.SeparatorBarcodeValue))
        {
            ScanConsole.Upload(
                $"Separator barcode '{ctx.SeparatorBarcodeValue}' with SAP regex '{pattern}' gave '{key ?? "(none)"}'.");
        }
        return key;
    }

    private static string? ExtractWithRegex(string value, string? pattern) =>
        SapObjectKeyResolver.ExtractWithRegex(value, pattern);

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

}
