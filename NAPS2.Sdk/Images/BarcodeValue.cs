namespace NAPS2.Images;

/// <summary>
/// A single barcode decoded from a page. A page may carry several of these, for example a production
/// sheet with an order barcode and an article barcode.
/// </summary>
public record BarcodeValue
{
    public BarcodeValue()
    {
    }

    public BarcodeValue(string? text, string? format)
    {
        Text = text;
        Format = format;
    }

    /// <summary>
    /// The decoded barcode content.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// The ZXing symbology name, e.g. CODE_39, CODE_128, EAN_13.
    /// </summary>
    public string? Format { get; init; }
}
