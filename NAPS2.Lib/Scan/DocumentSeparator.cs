using System.Text.RegularExpressions;
using NAPS2.Images;
using NLog;

namespace NAPS2.Scan;

/// <summary>
/// One document produced by splitting a scan.
/// </summary>
/// <param name="Images">The pages belonging to the document.</param>
/// <param name="SeparatorBarcodeValue">
/// The value read from the barcode that started the document, after the separation pattern was applied.
/// Null when the document wasn't started by a barcode.
/// </param>
/// <param name="StartPageIndex">The zero-based index of the document's first page within the whole scan.</param>
public sealed record DocumentSegment(
    IReadOnlyList<ProcessedImage> Images,
    string? SeparatorBarcodeValue,
    int StartPageIndex);

/// <summary>
/// Splits a scan into documents according to a profile's <see cref="DocumentWorkflowSettings"/>.
/// This is the single place where document boundaries are decided.
/// </summary>
public static class DocumentSeparator
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public static IEnumerable<DocumentSegment> Separate(
        IEnumerable<ProcessedImage> images, DocumentWorkflowSettings settings)
    {
        if (images == null)
        {
            throw new ArgumentNullException(nameof(images));
        }
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var pages = images as IList<ProcessedImage> ?? images.ToList();
        if (settings.SeparationMode == DocumentSeparationMode.None)
        {
            return pages.Count > 0
                ? [new DocumentSegment(pages.ToList(), null, 0)]
                : [];
        }
        if (settings.SeparationMode == DocumentSeparationMode.OnePerPage)
        {
            return pages.Select((page, index) =>
                new DocumentSegment([page], null, index)).ToList();
        }
        return SeparateCore(pages, settings, CompilePattern(settings.SeparationPattern));
    }

    /// <summary>
    /// Applies the separation pattern to a barcode value. Returns null if the value doesn't match, which
    /// means the page is not a document boundary. With no pattern every value matches as-is.
    /// </summary>
    public static string? ApplyPattern(string? value, Regex? pattern)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (pattern == null)
        {
            return value;
        }
        var match = pattern.Match(value);
        if (!match.Success)
        {
            return null;
        }
        // A capturing group lets one barcode both mark the boundary and supply just the part that
        // should end up in the file name; without a group the whole match is used.
        return match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : match.Value;
    }

    public static Regex? CompilePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            _logger.Warn(ex, $"Ignoring invalid separation pattern '{pattern}'");
            // Falling back to "every barcode separates" without saying so looks exactly like the pattern
            // being ignored on purpose, which is the hardest kind of misconfiguration to spot.
            ScanConsole.Barcode(
                $"WARNING: the separation pattern '{pattern}' is not a valid regex ({ex.Message}) and is " +
                "ignored, so every barcode starts a new document.");
            return null;
        }
    }

    private static IEnumerable<DocumentSegment> SeparateCore(
        IList<ProcessedImage> pages, DocumentWorkflowSettings settings, Regex? pattern)
    {
        var current = new List<ProcessedImage>();
        string? currentValue = null;
        // current.Count is not the same question: with KeepSeparatorPage off a document that has only had
        // its separator sheet so far is open but still empty, and a repeat of its barcode must not be
        // treated as the start of a new one.
        var documentIsOpen = false;
        var startPageIndex = 0;

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var page = pages[pageIndex];
            var separatorValue = GetSeparatorValue(page, settings, pattern, pageIndex + 1);

            // The paperwork for one order repeats the order barcode on each of the cover sheets it
            // contains, so a page carrying the value the current document was started with continues that
            // document instead of starting a copy of it under the same name. An empty value (a patch-T
            // sheet) has nothing to compare and always separates.
            if (separatorValue is { Length: > 0 } && settings.NewDocumentOnlyOnValueChange &&
                documentIsOpen && separatorValue == currentValue)
            {
                ScanConsole.Document(
                    $"Page {pageIndex + 1} carries '{separatorValue}' again, which is the value the " +
                    "current document was started with, so it continues that document rather than " +
                    "starting a new one.");
                if (!settings.KeepsSeparatorPage())
                {
                    page.Dispose();
                    continue;
                }
                current.Add(page);
                continue;
            }

            if (separatorValue != null)
            {
                if (current.Count > 0)
                {
                    yield return new DocumentSegment(current, currentValue, startPageIndex);
                    current = [];
                }
                startPageIndex = pageIndex;
                currentValue = separatorValue == string.Empty ? null : separatorValue;
                documentIsOpen = true;

                if (!settings.KeepsSeparatorPage())
                {
                    // The sheet only marks the boundary and is not part of the output.
                    page.Dispose();
                    startPageIndex = pageIndex + 1;
                    continue;
                }
            }
            current.Add(page);
        }

        if (current.Count > 0)
        {
            yield return new DocumentSegment(current, currentValue, startPageIndex);
        }
    }

    /// <summary>
    /// Decides whether a page starts a new document, and with which value. Returns null when it doesn't;
    /// an empty string when it does but carries no usable value (a plain patch-T sheet).
    /// </summary>
    private static string? GetSeparatorValue(
        ProcessedImage page, DocumentWorkflowSettings settings, Regex? pattern, int pageNumber)
    {
        var barcode = page.PostProcessingData.Barcode;
        if (settings.SeparationMode == DocumentSeparationMode.PatchT)
        {
            return barcode.IsPatchT ? string.Empty : null;
        }

        var symbologies = settings.GetEffectiveSymbologies();
        var all = barcode.GetAllValues().Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
        // A page carrying several barcodes is the case this whole selection exists for, so say what was
        // on the page before saying which one won. Without this the operator cannot tell a barcode that
        // was never decoded from one that was decoded and then rejected by the pattern.
        if (all.Count > 1)
        {
            ScanConsole.Barcode(
                $"Page {pageNumber} carries {all.Count} barcodes: " +
                string.Join(", ", all.Select(x => $"{x.Format ?? "?"}:'{x.Text}'")) + "; " +
                (pattern != null
                    ? $"the separation pattern '{settings.SeparationPattern}' decides which one is used."
                    : "no separation pattern is set, so the first one in reading order is used."));
        }

        var rejectedBySymbology = new List<string>();
        var rejectedByPattern = new List<string>();
        foreach (var value in all)
        {
            // An empty symbology list means the operator didn't restrict the type, so any barcode counts.
            // Some scan and import paths don't populate the format, and we can't rule those out by
            // symbology, so an unknown format is accepted rather than silently dropping the separator.
            var formatIsUnknown = string.IsNullOrWhiteSpace(value.Format);
            if (symbologies.Count > 0 && !formatIsUnknown &&
                !symbologies.Any(x => x.Matches(value.Format, value.Text)))
            {
                rejectedBySymbology.Add($"{value.Format}:'{value.Text}'");
                continue;
            }
            var applied = ApplyPattern(value.Text, pattern);
            if (applied != null)
            {
                _logger.Debug(
                    $"Document boundary: text='{value.Text}' format='{value.Format ?? "<unknown>"}' value='{applied}'");
                if (all.Count > 1)
                {
                    ScanConsole.Barcode(
                        $"Page {pageNumber}: barcode '{value.Text}' is the one used" +
                        (pattern != null ? " (it matches the separation pattern)" : "") +
                        $"; it starts a new document as '{applied}'.");
                }
                return applied;
            }
            rejectedByPattern.Add($"'{value.Text}'");
        }

        // Nothing on this page separates. That is a normal, expected outcome for a page in the middle of
        // a document, but it is also what a wrong pattern looks like, so name the values that were turned
        // down rather than returning in silence.
        if (rejectedByPattern.Count > 0)
        {
            ScanConsole.Barcode(
                $"Page {pageNumber}: no barcode matches the separation pattern " +
                $"'{settings.SeparationPattern ?? ""}'; rejected {string.Join(", ", rejectedByPattern)}. " +
                "The page does not start a new document.");
        }
        else if (rejectedBySymbology.Count > 0)
        {
            ScanConsole.Barcode(
                $"Page {pageNumber}: {string.Join(", ", rejectedBySymbology)} " +
                $"do not belong to the selected symbologies ({string.Join("+", symbologies)}), " +
                "so the page does not start a new document.");
        }
        return null;
    }
}
