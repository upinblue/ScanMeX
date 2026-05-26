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

    public bool IsPatchT => DetectedText == PATCH_T_TEXT;

    public bool IsCode39 => string.Equals(DetectedFormat, CODE_39_FORMAT, StringComparison.OrdinalIgnoreCase);
}