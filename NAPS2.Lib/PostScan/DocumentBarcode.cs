namespace NAPS2.PostScan;

/// <summary>
/// Where a barcode value on a document came from.
/// </summary>
public enum DocumentBarcodeSource
{
    /// <summary>
    /// Decoded from the page.
    /// </summary>
    Detected,

    /// <summary>
    /// Typed or corrected by the operator.
    /// </summary>
    Manual
}

/// <summary>
/// One barcode belonging to a document. Detection is not the last word on these: a scanned form can
/// yield values that are not on the paper, and a value that is on the paper can be misread, so the
/// operator can correct or remove them before the document is filed.
/// </summary>
/// <param name="Value">The barcode's text.</param>
/// <param name="Format">The symbology name (CODE_39, CODE_128, ...), or null for a value typed by hand.</param>
/// <param name="PageIndex">Zero-based page within the document, or -1 for a value that isn't on a page.</param>
/// <param name="Source">Whether the value was decoded or entered by hand.</param>
public sealed record DocumentBarcode(
    string Value,
    string? Format,
    int PageIndex,
    DocumentBarcodeSource Source = DocumentBarcodeSource.Detected)
{
    /// <summary>
    /// How the barcode reads in a log line or a tooltip.
    /// </summary>
    public string Describe() =>
        Source == DocumentBarcodeSource.Manual
            ? $"'{Value}' (entered by hand)"
            : $"{Format ?? "?"}:'{Value}' (page {PageIndex + 1})";
}
