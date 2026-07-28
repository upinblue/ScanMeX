using NAPS2.Images;

namespace NAPS2.Scan;

/// <summary>
/// Extracts detected barcode metadata from processed images using ScanMe/NAPS2's existing barcode pipeline.
/// </summary>
public sealed class BarcodeExtractor
{
    /// <summary>
    /// Gets or sets the maximum number of barcode entries returned per page.
    /// </summary>
    public int MaxBarcodesPerPage { get; init; } = 5;

    /// <summary>
    /// Extracts detected barcodes from post-processing metadata in reading order.
    /// </summary>
    /// <param name="images">The processed images to inspect.</param>
    /// <returns>The detected barcode list.</returns>
    public IReadOnlyList<DetectedBarcode> Extract(IReadOnlyList<ProcessedImage> images)
    {
        if (images == null)
        {
            throw new ArgumentNullException(nameof(images));
        }

        var result = new List<DetectedBarcode>();
        for (var pageIndex = 0; pageIndex < images.Count; pageIndex++)
        {
            var barcode = images[pageIndex].PostProcessingData.Barcode;
            if (MaxBarcodesPerPage <= 0)
            {
                continue;
            }

            // The primary detection comes first so $(barcode) keeps resolving to the symbology the
            // profile selected, followed by the page's remaining barcodes in reading order.
            var values = barcode.GetAllValues()
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .OrderByDescending(x => IsPrimary(barcode, x))
                .Take(MaxBarcodesPerPage);
            foreach (var value in values)
            {
                var isPatchT = Barcode.IsPatchTText(value.Text);
                result.Add(new DetectedBarcode(
                    value.Text!,
                    isPatchT ? "PATCH_T" : value.Format ?? string.Empty,
                    pageIndex,
                    isPatchT));
            }
        }
        return result;
    }

    private static bool IsPrimary(Barcode barcode, BarcodeValue value) =>
        barcode.IsDetected && string.Equals(value.Text, barcode.DetectedText, StringComparison.Ordinal) &&
        string.Equals(value.Format, barcode.DetectedFormat, StringComparison.Ordinal);
}
