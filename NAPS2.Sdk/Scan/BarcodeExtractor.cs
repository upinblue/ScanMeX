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
            if (!barcode.IsDetected || string.IsNullOrWhiteSpace(barcode.DetectedText) || MaxBarcodesPerPage <= 0)
            {
                continue;
            }

            result.Add(new DetectedBarcode(
                barcode.DetectedText,
                barcode.IsPatchT ? "PATCH_T" : barcode.DetectedFormat ?? string.Empty,
                pageIndex,
                barcode.IsPatchT));
        }
        return result;
    }
}
