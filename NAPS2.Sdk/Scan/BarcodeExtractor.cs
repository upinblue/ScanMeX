using System.Text.RegularExpressions;
using NAPS2.Images;

namespace NAPS2.Scan;

/// <summary>
/// Extracts detected barcode metadata from processed images using ScanMe/NAPS2's existing barcode pipeline.
/// </summary>
public sealed class BarcodeExtractor
{
    private readonly string? _selectionPattern;
    private readonly Regex? _selection;

    /// <summary>
    /// Gets or sets the maximum number of barcode entries returned per page.
    /// </summary>
    public int MaxBarcodesPerPage { get; init; } = 5;

    /// <summary>
    /// The profile's barcode regex, or null to leave the barcodes in reading order.
    /// </summary>
    /// <remarks>
    /// A production paper carries several Code 39 codes -- an order number, an article number, a route
    /// card number -- and which one comes first is a property of where they sit on the sheet, not of what
    /// the operator meant. The regex is the operator's statement of which one identifies the document, so
    /// the value it accepts is the one <c>$(barcode:1)</c> has to yield. An invalid pattern is treated as
    /// no pattern; the separation code reports it to the console for the same string.
    /// </remarks>
    public string? SelectionPattern
    {
        get => _selectionPattern;
        init
        {
            _selectionPattern = value;
            _selection = Compile(value);
        }
    }

    /// <summary>
    /// Extracts detected barcodes from post-processing metadata, with the barcode matching
    /// <see cref="SelectionPattern"/> first and everything else in reading order.
    /// </summary>
    /// <param name="images">The processed images to inspect.</param>
    /// <returns>The detected barcode list.</returns>
    public IReadOnlyList<DetectedBarcode> Extract(IReadOnlyList<ProcessedImage> images)
    {
        if (images == null)
        {
            throw new ArgumentNullException(nameof(images));
        }

        var result = new List<DetectedBarcode>();
        for (var pageIndex = 0; pageIndex < images.Count; pageIndex++)
        {
            var barcode = images[pageIndex].PostProcessingData.Barcode;
            if (MaxBarcodesPerPage <= 0)
            {
                continue;
            }

            // The ordering runs before the cap, so the barcode the profile actually asks for can never be
            // the one that falls off the end of a page carrying more than MaxBarcodesPerPage codes.
            // Matching the pattern outranks being the page's primary, which is only "first in reading
            // order among the selected symbologies"; the primary still wins when no pattern is set.
            var values = barcode.GetAllValues()
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .OrderByDescending(x => Matches(x.Text))
                .ThenByDescending(x => IsPrimary(barcode, x))
                .Take(MaxBarcodesPerPage);
            foreach (var value in values)
            {
                var isPatchT = Barcode.IsPatchTText(value.Text);
                result.Add(new DetectedBarcode(
                    value.Text!,
                    isPatchT ? "PATCH_T" : value.Format ?? string.Empty,
                    pageIndex,
                    isPatchT));
            }
        }
        return PromoteSelected(result);
    }

    /// <summary>
    /// Moves the first barcode matching the pattern to the front of the document's list, so
    /// <c>$(barcode:1)</c> is the value the operator asked for even when the page carrying it isn't the
    /// first one. The rest keep their reading order, which is what the higher indexes mean.
    /// </summary>
    private List<DetectedBarcode> PromoteSelected(List<DetectedBarcode> barcodes)
    {
        if (_selection == null || barcodes.Count < 2)
        {
            return barcodes;
        }
        var index = barcodes.FindIndex(x => Matches(x.Value));
        if (index <= 0)
        {
            return barcodes;
        }
        var selected = barcodes[index];
        barcodes.RemoveAt(index);
        barcodes.Insert(0, selected);
        return barcodes;
    }

    private bool Matches(string? text) =>
        _selection != null && !string.IsNullOrWhiteSpace(text) && _selection.IsMatch(text!);

    private static Regex? Compile(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsPrimary(Barcode barcode, BarcodeValue value) =>
        barcode.IsDetected && string.Equals(value.Text, barcode.DetectedText, StringComparison.Ordinal) &&
        string.Equals(value.Format, barcode.DetectedFormat, StringComparison.Ordinal);
}
