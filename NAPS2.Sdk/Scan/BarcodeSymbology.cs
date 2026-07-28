using ZXing;

namespace NAPS2.Scan;

/// <summary>
/// A barcode symbology that can be selected in a scan profile.
/// </summary>
public enum BarcodeSymbology
{
    /// <summary>
    /// Patch-T separator sheets. Technically Code 39 carrying the text "PATCHT".
    /// </summary>
    PatchT,

    Code39,

    Code128,

    /// <summary>
    /// EAN-8/EAN-13/UPC-A/UPC-E article codes, which are treated as one selectable group.
    /// </summary>
    EanUpc
}

public static class BarcodeSymbologyMap
{
    /// <summary>
    /// Maps a symbology to the ZXing formats it covers.
    /// </summary>
    public static IEnumerable<BarcodeFormat> ToZXingFormats(this BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.PatchT => [BarcodeFormat.CODE_39],
        BarcodeSymbology.Code39 => [BarcodeFormat.CODE_39],
        BarcodeSymbology.Code128 => [BarcodeFormat.CODE_128],
        BarcodeSymbology.EanUpc =>
            [BarcodeFormat.EAN_8, BarcodeFormat.EAN_13, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E],
        _ => []
    };

    /// <summary>
    /// Determines whether a decoded barcode belongs to the given symbology. Patch-T additionally requires
    /// the well-known separator text, so a regular Code 39 barcode is not mistaken for a separator sheet.
    /// </summary>
    public static bool Matches(this BarcodeSymbology symbology, string? format, string? text)
    {
        if (format == null)
        {
            return false;
        }
        if (symbology == BarcodeSymbology.PatchT)
        {
            return Barcode.IsPatchTText(text);
        }
        return symbology.ToZXingFormats()
            .Any(x => string.Equals(x.ToString(), format, StringComparison.OrdinalIgnoreCase));
    }
}
