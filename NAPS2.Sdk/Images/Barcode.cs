namespace NAPS2.Images;

/// <summary>
/// A wrapper around the ZXing library that detects patch-t and other barcodes.
/// http://www.alliancegroup.co.uk/patch-codes.htm
/// </summary>
public record Barcode(bool IsDetectionAttempted, bool IsDetected, string? DetectedText, string? DetectedFormat)
{
    private const string PATCH_T_TEXT = "PATCHT";
    private const string CODE_39_FORMAT = "CODE_39";

    public static readonly Barcode NoDetection = new(false, false, null, null);

    private Barcode() : this(false, false, null, null)
    {
    }

    /// <summary>
    /// Every barcode decoded on the page, in reading order. A page may carry several, e.g. a production
    /// sheet with both an order and an article barcode. <see cref="DetectedText"/> holds the primary one,
    /// which is the first match for the profile's selected symbologies.
    /// </summary>
    public List<BarcodeValue> AllDetections { get; init; } = [];

    public bool IsPatchT => IsPatchTText(DetectedText);

    public bool IsCode39 => string.Equals(DetectedFormat, CODE_39_FORMAT, StringComparison.OrdinalIgnoreCase);

    public static bool IsPatchTText(string? text) =>
        string.Equals(text, PATCH_T_TEXT, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets all decoded barcodes, falling back to the primary detection for images that were serialized
    /// before multi-barcode support existed.
    /// </summary>
    public IReadOnlyList<BarcodeValue> GetAllValues()
    {
        if (AllDetections.Count > 0)
        {
            return AllDetections;
        }
        return IsDetected && !string.IsNullOrWhiteSpace(DetectedText)
            ? [new BarcodeValue(DetectedText, DetectedFormat)]
            : [];
    }
}
