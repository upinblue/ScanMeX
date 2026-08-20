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

    /// <summary>
    /// How much damage a printed barcode may carry and still be accepted. Anything but
    /// <see cref="BarcodeStrictness.Strict"/> adds a second Code 39 pass that recovers symbols ZXing
    /// discards -- see <see cref="DamagedCode39Reader"/> for what it does and does not allow.
    /// </summary>
    public BarcodeStrictness Strictness { get; set; } = BarcodeStrictness.Strict;

    /// <summary>
    /// The part of the page to look in, or null for the whole page. Null is what every profile written
    /// before the search area existed deserializes to, so nothing starts ignoring part of a page on its
    /// own -- see <see cref="BarcodeSearchArea"/>.
    /// </summary>
    public BarcodeSearchArea? SearchArea { get; set; }

    public DecodingOptions? ZXingOptions { get; set; }
}
