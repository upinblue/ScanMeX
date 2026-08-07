using System.Text.RegularExpressions;

namespace NAPS2.Sap;

/// <summary>
/// Works out which value a document is archived under in SAP. Pure logic, separated from the upload so
/// it can be tested: getting this wrong files documents under the wrong object key, which is not
/// something the operator can see afterwards.
/// </summary>
public static class SapObjectKeyResolver
{
    /// <summary>
    /// Picks the object key for a document whose profile takes the key from the scanned barcodes.
    /// </summary>
    /// <param name="separatorBarcodeValue">
    /// The barcode that started the document, already filtered by the profile's separation pattern. This
    /// is what the file was named after, so preferring it keeps the file name and the object key
    /// identical. Null when the profile doesn't separate by barcode.
    /// </param>
    /// <param name="pageBarcodes">The primary barcode of each page, used when there is no separator value.</param>
    /// <param name="pattern">The profile's SAP barcode regex, or null/empty to take values as they are.</param>
    /// <returns>The object key, or null when none could be determined.</returns>
    public static string? FromScannedBarcodes(
        string? separatorBarcodeValue, IEnumerable<string?> pageBarcodes, string? pattern) =>
        FromScannedBarcodes(separatorBarcodeValue, pageBarcodes, [], pattern);

    /// <summary>
    /// Picks the object key for a document whose profile takes the key from the scanned barcodes.
    /// </summary>
    /// <param name="separatorBarcodeValue">
    /// The barcode that started the document, already filtered by the profile's separation pattern. This
    /// is what the file was named after, so preferring it keeps the file name and the object key
    /// identical. Null when the profile doesn't separate by barcode.
    /// </param>
    /// <param name="pageBarcodes">The primary barcode of each page, used when there is no separator value.</param>
    /// <param name="secondaryPageBarcodes">
    /// The document's remaining barcodes -- everything decoded on its pages that is not the page's primary
    /// barcode. A production paper often carries several Code 39 codes and the regex is how the operator
    /// says which of them is the object key, so a key the primaries cannot supply is looked for here
    /// before giving up. Only consulted when a regex is configured; without one there is nothing to tell
    /// these barcodes apart and the primary is the only defensible choice.
    /// </param>
    /// <param name="pattern">The profile's SAP barcode regex, or null/empty to take values as they are.</param>
    /// <returns>The object key, or null when none could be determined.</returns>
    public static string? FromScannedBarcodes(
        string? separatorBarcodeValue, IEnumerable<string?> pageBarcodes,
        IEnumerable<string?> secondaryPageBarcodes, string? pattern)
    {
        if (!string.IsNullOrWhiteSpace(separatorBarcodeValue))
        {
            var fromSeparator = ExtractWithRegex(separatorBarcodeValue!, pattern);
            if (!string.IsNullOrWhiteSpace(fromSeparator))
            {
                return fromSeparator;
            }
        }

        // No separation, or the separator value was filtered out by the regex. Fall back to the page
        // barcodes, which only yields a key when the document agrees on a single value.
        var fromPrimaries = SingleMatch(pageBarcodes, pattern);
        if (fromPrimaries != null)
        {
            return fromPrimaries;
        }

        // The primaries gave nothing. When the operator configured a regex, it is a statement about which
        // barcode on the page is the object key -- and that barcode is often not the first one in reading
        // order, which is all the primary is. Widening the search here rather than earlier keeps every
        // document that resolves today resolving to exactly the same key.
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        return SingleMatch(secondaryPageBarcodes, pattern);
    }

    /// <summary>
    /// The one value these barcodes agree on after the regex is applied, or null if they supply none or
    /// disagree. Archiving under a guessed key is invisible afterwards, so ambiguity is refused.
    /// </summary>
    private static string? SingleMatch(IEnumerable<string?> barcodes, string? pattern)
    {
        var matches = barcodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => ExtractWithRegex(x!, pattern))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Applies an optional regex to a barcode value. With no pattern the trimmed value is used as-is;
    /// with a pattern, capturing group 1 wins over the whole match so one barcode can both identify the
    /// document and contribute only part of itself to the key.
    /// </summary>
    public static string? ExtractWithRegex(string value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        var match = Regex.Match(value, pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }
        for (var i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success)
            {
                return match.Groups[i].Value.Trim();
            }
        }
        return match.Value.Trim();
    }
}
