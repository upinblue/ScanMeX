using ZXing.Common;

namespace NAPS2.Scan;

/// <summary>
/// Options for detecting barcodes using ZXing.
/// </summary>
public class BarcodeDetectionOptions
{
    public bool DetectBarcodes { get; set; }

    /// <summary>
    /// Restricts detection to patch-t separator sheets. Kept for existing callers; prefer
    /// <see cref="Symbologies"/>, which also drives which barcode is picked as the primary one.
    /// </summary>
    public bool PatchTOnly { get; set; }

    /// <summary>
    /// The symbologies the profile is interested in. An empty list means "detect anything".
    /// The first decoded barcode matching one of these becomes the page's primary barcode.
    /// </summary>
    public List<BarcodeSymbology> Symbologies { get; set; } = [];

    public DecodingOptions? ZXingOptions { get; set; }
}
