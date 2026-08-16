using Eto.Drawing;

namespace NAPS2.EtoForms;

/// <summary>
/// Loads icons from the embedded Icons resources and recolours them for the active theme.
///
/// The icons come from the Fluent UI System Icons set and are stored as black glyphs on a
/// transparent background (see tools/icons/Generate-Icons.ps1). Storing one colour and tinting at
/// load time is what makes dark mode work: a fixed dark glyph is invisible on a dark toolbar, and
/// shipping a second light-mode copy of every file would double the icon count and still not follow
/// a changed accent or text colour.
/// </summary>
public class DefaultIconProvider : IIconProvider
{
    /// <summary>
    /// Icons that are not monochrome and must keep the colours they were drawn with: third-party
    /// brand logos, the scanner illustrations in the device list (whose default/lock/wireless
    /// variants encode state through colour) and the application icon. This mirrors the exclusion
    /// list at the top of tools/icons/icon-map.tsv -- an icon regenerated from the Fluent set is
    /// monochrome and belongs in neither list.
    /// </summary>
    private static readonly string[] UntintedPrefixes =
    [
        "apple_mail", "favicon", "gmail", "outlooknew", "outlookweb", "scanner_", "thunderbird"
    ];

    public Bitmap? GetIcon(string name, float scale = 1f, bool oversized = false)
    {
        var bitmap = LoadIcon(name, scale);
        if (bitmap == null)
        {
            return null;
        }
        var tint = GetTintColor(name);
        return tint == null ? bitmap : bitmap.Tint(tint.Value);
    }

    public Icon? GetFormIcon(string name, float scale = 1f)
    {
        var icon = GetIcon(name, scale);
        return icon != null ? new Icon(1f, icon) : null;
    }

    private static Bitmap? LoadIcon(string name, float scale)
    {
        if (scale > 1)
        {
            // TODO: Maybe generalize everything with a numeric pixel size suffix?
            if (name.EndsWith("_small"))
            {
                var norm = (byte[]?) Icons.ResourceManager.GetObject(name.Substring(0, name.Length - 6));
                if (norm != null)
                {
                    return new Bitmap(norm).ResizeTo((int) (16 * scale));
                }
            }
            else if (name.EndsWith("_48"))
            {
                var hires = (byte[]?) Icons.ResourceManager.GetObject(name.Substring(0, name.Length - 3) + "_96");
                if (hires != null)
                {
                    return new Bitmap(hires).ResizeTo((int) (48 * scale));
                }
            }
            else
            {
                var hires = (byte[]?) Icons.ResourceManager.GetObject(name + "_hires");
                if (hires != null)
                {
                    return new Bitmap(hires).ResizeTo((int) (32 * scale));
                }
            }
        }

        var data = (byte[]?) Icons.ResourceManager.GetObject(name);
        if (data != null)
        {
            var bitmap = new Bitmap(data);
            if (scale > 1)
            {
                return bitmap.ResizeTo((int) (bitmap.Width * scale), (int) (bitmap.Height * scale));
            }
            return bitmap;
        }

        return null;
    }

    /// <returns>The colour to draw the glyph in, or null to leave the icon as stored.</returns>
    private static Color? GetTintColor(string name)
    {
        foreach (var prefix in UntintedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }
        }

        // The scheme reads the theme out of the config, which isn't attached until the container is
        // built. Nothing should ask for an icon that early, but an icon in its stored black is a
        // better failure than a startup crash if something ever does.
        if (!EtoPlatform.HasCurrent || EtoPlatform.Current.ColorScheme.Config == null)
        {
            return null;
        }
        var colorScheme = EtoPlatform.Current.ColorScheme;

        // "exclamation" is only ever an error or warning state (ChooseDeviceForm, ErrorForm), and
        // rendering it in the plain text colour is the one case where the tint would cost signal
        // that the old coloured icons carried.
        return name.StartsWith("exclamation", StringComparison.Ordinal)
            ? colorScheme.CautionColor
            : colorScheme.ForegroundColor;
    }

}
