namespace NAPS2.Scan;

/// <summary>
/// How much damage a printed barcode may carry and still be accepted.
/// </summary>
/// <remarks>
/// Anything but <see cref="Strict"/> only ever adds Code 39 readings that ZXing refused. Code 128, EAN
/// and UPC all carry a check character, so "tolerating" one of those would mean accepting a code whose
/// own checksum says it was misread -- a different and far more dangerous proposition than accepting a
/// Code 39 whose guard character is damaged, which has no checksum to overrule in the first place.
/// </remarks>
public enum BarcodeStrictness
{
    /// <summary>
    /// Only barcodes that decode completely, guard characters included. The default, and what every
    /// profile saved before this setting existed reads as.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Additionally accept a Code 39 barcode whose stop guard is damaged, when it carries at least six
    /// characters and the same value was read on at least four scan lines.
    /// </summary>
    Tolerant = 1,

    /// <summary>
    /// As above, down to four characters, and allowing the damaged guard to be more misshapen than
    /// <see cref="Tolerant"/> does. This is where the margin against print noise gets thin, so it is a
    /// level to move to because Tolerant demonstrably misses codes, not as a precaution.
    /// </summary>
    VeryTolerant = 2
}

/// <summary>
/// The thresholds each lowered strictness level puts on a recovered reading.
/// </summary>
/// <param name="MinCharacters">
/// How many characters a symbol has to carry. This is the strongest of the three guards by a distance:
/// measured on a form-shaped noisy page under four noise seeds, the longest thing the print noise ever
/// produced was three characters, while the customer's real codes are fourteen.
/// </param>
/// <param name="MinScanLines">
/// How many scan lines have to agree on the value. Weaker than it looks -- noise reached five lines on
/// the same measurement, against thirty-five for a real barcode -- so it narrows the gap rather than
/// deciding anything on its own.
/// </param>
/// <param name="MaxTerminatorWidthDeviation">
/// How far the damaged terminating group's width may differ from the characters before it. This is what
/// separates a mangled stop guard from the barcode simply running out into the rest of the page, so
/// widening it is the other axis along which a level can be more forgiving.
/// </param>
internal record Code39Tolerance(int MinCharacters, int MinScanLines, double MaxTerminatorWidthDeviation)
{
    public static Code39Tolerance? For(BarcodeStrictness strictness) => strictness switch
    {
        BarcodeStrictness.Tolerant => new Code39Tolerance(6, 4, 0.25),
        BarcodeStrictness.VeryTolerant => new Code39Tolerance(4, 3, 0.40),
        _ => null
    };
}
