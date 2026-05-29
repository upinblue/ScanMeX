namespace NAPS2.Scan;

/// <summary>
/// Represents a barcode detected on a processed scan page.
/// </summary>
/// <param name="Value">The decoded barcode value.</param>
/// <param name="BarcodeType">The barcode symbology name, for example <c>CODE128</c>, <c>QR</c>, or <c>PATCH_T</c>.</param>
/// <param name="PageIndex">The zero-based page index in the current scan segment.</param>
/// <param name="IsPatchCode">A value indicating whether this barcode is a patch-code separator.</param>
public sealed record DetectedBarcode(string Value, string BarcodeType, int PageIndex, bool IsPatchCode);
