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

    public BarcodeValue(string? text, string? format, bool isRecovered)
    {
        Text = text;
        Format = format;
        IsRecovered = isRecovered;
    }

    /// <summary>
    /// The decoded barcode content.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// The ZXing symbology name, e.g. CODE_39, CODE_128, EAN_13.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// Whether this value came from the tolerant Code 39 pass rather than from a symbol that decoded in
    /// full. Only ever true when the profile lowered its barcode strictness. It carries no weight in the
    /// pipeline -- a recovered value names files and archive keys like any other -- but it is on the
    /// record so the console can say which values the operator's own setting let through.
    /// </summary>
    public bool IsRecovered { get; init; }
}
