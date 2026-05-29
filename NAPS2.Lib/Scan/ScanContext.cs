using NAPS2.Images;

namespace NAPS2.Scan;

/// <summary>
/// Describes the current scan output segment for shared placeholder and upload processing.
/// </summary>
public sealed class ScanContext
{
    /// <summary>
    /// Gets the timestamp associated with this scan segment.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the zero-based position in the current batch.
    /// </summary>
    public int SequenceIndex { get; init; }

    /// <summary>
    /// Gets the profile used for the scan.
    /// </summary>
    public ScanProfile Profile { get; init; } = new();

    /// <summary>
    /// Gets the processed images in this scan segment.
    /// </summary>
    public IReadOnlyList<ProcessedImage> Images { get; init; } = Array.Empty<ProcessedImage>();

    /// <summary>
    /// Gets all detected barcodes in reading order.
    /// </summary>
    public IReadOnlyList<DetectedBarcode> Barcodes { get; init; } = Array.Empty<DetectedBarcode>();

    /// <summary>
    /// Gets the separator barcode value if the current segment was split by a patch/barcode separator.
    /// </summary>
    public string? SeparatorBarcodeValue { get; init; }

    /// <summary>
    /// Gets the output extension without a leading dot.
    /// </summary>
    public string OutputExtension { get; init; } = "pdf";

    /// <summary>
    /// Gets the output file format, for example <c>pdf</c>, <c>tiff</c>, <c>jpg</c>, or <c>png</c>.
    /// </summary>
    public string FileFormat { get; init; } = "pdf";

    /// <summary>
    /// Gets the user name used for template expansion.
    /// </summary>
    public string UserName { get; init; } = Environment.UserName;

    /// <summary>
    /// Gets the host name used for template expansion.
    /// </summary>
    public string HostName { get; init; } = Environment.MachineName;
}
