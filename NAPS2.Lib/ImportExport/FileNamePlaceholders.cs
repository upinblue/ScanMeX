using System.Text.RegularExpressions;
using NAPS2.Scan;

namespace NAPS2.ImportExport;

/// <summary>
/// Extends the existing placeholder engine with scan-context-aware barcode placeholders.
/// </summary>
public sealed class FileNamePlaceholders
{
    private static readonly Regex TokenPattern = new(
        @"\$\((barcode:regex=[^)]*\([^)]*\)[^)]*)\)|\$\((barcode(?::[^)]*)?|profile|user|host|ext)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Performs the existing placeholder substitutions.
    /// </summary>
    public string? SubstitutePlaceholders(string? template, DateTime now, bool autoIncrement = false, int i = 0,
        int autoNumberDigits = 0)
    {
        return Placeholders.All.WithDate(now).Substitute(template, autoIncrement, i, autoNumberDigits);
    }

    /// <summary>
    /// Performs existing substitutions plus scan-context-specific barcode and metadata placeholders.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="ctx">The scan context.</param>
    /// <param name="autoIncrement">Whether numeric placeholders should increment if the file exists.</param>
    /// <returns>The substituted text.</returns>
    public string SubstitutePlaceholders(string template, ScanContext ctx, bool autoIncrement = false)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }
        if (ctx == null)
        {
            throw new ArgumentNullException(nameof(ctx));
        }

        var result = Placeholders.All.WithDate(ctx.Timestamp)
            .Substitute(template, autoIncrement, ctx.SequenceIndex) ?? string.Empty;
        return TokenPattern.Replace(result, match => ResolveToken(
            match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value,
            ctx));
    }

    /// <summary>
    /// Replaces invalid file-name characters with underscores.
    /// </summary>
    public static string SanitizeForFileName(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            .ToHashSet();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string ResolveToken(string token, ScanContext ctx)
    {
        if (token.Equals("profile", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeForFileName(ctx.Profile.DisplayName ?? string.Empty);
        }
        if (token.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            return ctx.UserName ?? string.Empty;
        }
        if (token.Equals("host", StringComparison.OrdinalIgnoreCase))
        {
            return ctx.HostName ?? string.Empty;
        }
        if (token.Equals("ext", StringComparison.OrdinalIgnoreCase))
        {
            return ctx.OutputExtension ?? string.Empty;
        }
        if (token.StartsWith("barcode", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveBarcode(token, ctx) ?? string.Empty;
        }
        return string.Empty;
    }

    private static string? ResolveBarcode(string token, ScanContext ctx)
    {
        if (token.Equals("barcode", StringComparison.OrdinalIgnoreCase))
        {
            return ctx.SeparatorBarcodeValue ?? ctx.Barcodes.FirstOrDefault()?.Value;
        }

        var selector = token.Substring("barcode:".Length);
        if (int.TryParse(selector, out var oneBasedIndex))
        {
            return oneBasedIndex > 0 && oneBasedIndex <= ctx.Barcodes.Count
                ? ctx.Barcodes[oneBasedIndex - 1].Value
                : null;
        }

        if (selector.StartsWith("type=", StringComparison.OrdinalIgnoreCase))
        {
            var type = selector.Substring("type=".Length);
            return ctx.Barcodes.FirstOrDefault(x => string.Equals(x.BarcodeType, type, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        if (selector.StartsWith("regex=", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = selector.Substring("regex=".Length);
            foreach (var barcode in ctx.Barcodes)
            {
                var match = Regex.Match(barcode.Value, pattern, RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }
                return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            }
        }
        return null;
    }
}
