using Eto.Drawing;

namespace NAPS2.EtoForms;

/// <summary>
/// The app's colour palette, in Windows 11 Fluent values.
///
/// The named constants below are the Fluent 2 design tokens (the names in comments are the ones
/// Microsoft uses in the WinUI resource dictionary), so a value that looks arbitrary can be checked
/// against the design system rather than guessed at. Everything the UI draws goes through this
/// class -- there is no second place where a colour is picked.
/// </summary>
public class ColorScheme
{
    // TextFillColorPrimary. Fluent's "black" is deliberately not #000000; pure black on white reads
    // as harsher than the rest of the system.
    private static readonly Color TextPrimaryLight = Color.FromRgb(0x1b1b1b);
    private static readonly Color TextPrimaryDark = Color.FromRgb(0xffffff);

    // SolidBackgroundFillColorBase / LayerFillColorDefault
    private static readonly Color SurfaceLight = Color.FromRgb(0xffffff);
    private static readonly Color SurfaceDark = Color.FromRgb(0x202020);

    // CardBackgroundFillColorDefault
    private static readonly Color CardLight = Color.FromRgb(0xf9f9f9);
    private static readonly Color CardDark = Color.FromRgb(0x2b2b2b);

    // DividerStrokeColorDefault, flattened against the surface behind it
    private static readonly Color DividerLight = Color.FromRgb(0xe5e5e5);
    private static readonly Color DividerDark = Color.FromRgb(0x333333);

    // SubtleFillColorSecondary / SubtleFillColorTertiary, flattened: the hover and pressed states
    // of a transparent control such as a toolbar button.
    private static readonly Color SubtleHoverLight = Color.FromRgb(0xf5f5f5);
    private static readonly Color SubtleHoverDark = Color.FromRgb(0x2d2d2d);
    private static readonly Color SubtlePressedLight = Color.FromRgb(0xededed);
    private static readonly Color SubtlePressedDark = Color.FromRgb(0x272727);

    // SystemFillColorCaution
    private static readonly Color CautionLight = Color.FromRgb(0x9d5d00);
    private static readonly Color CautionDark = Color.FromRgb(0xfce100);

    // The Windows 11 default accent, used when the OS doesn't report one.
    private static readonly Color DefaultAccent = Color.FromRgb(0x0078d4);

    private readonly IDarkModeProvider _darkModeProvider;

    public ColorScheme(IDarkModeProvider darkModeProvider)
    {
        _darkModeProvider = darkModeProvider;
        _darkModeProvider.DarkModeChanged += (_, _) => ColorSchemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool DarkMode => (Config ?? throw new InvalidOperationException()).Get(c => c.Theme) switch
    {
        Theme.Light => false,
        Theme.Dark => true,
        _ => _darkModeProvider.IsDarkModeEnabled,
    };

    public Naps2Config? Config { get; set; }

    public void UserThemeChanged()
    {
        ColorSchemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public Color ForegroundColor => DarkMode ? TextPrimaryDark : TextPrimaryLight;

    public Color BackgroundColor => DarkMode ? SurfaceDark : SurfaceLight;

    public Color SeparatorColor => DarkMode ? DividerDark : DividerLight;

    public Color BorderColor => DarkMode ? DividerDark : DividerLight;

    public Color CropColor => DarkMode ? AccentColor : Colors.Black;

    /// <summary>
    /// The user's Windows accent colour, nudged so it stays legible against the current surface:
    /// Windows lets you pick a near-black accent, which would disappear on a dark toolbar, and a
    /// pastel one, which would disappear on a white one.
    /// </summary>
    public Color AccentColor
    {
        get
        {
            var accent = _darkModeProvider.AccentColor ?? DefaultAccent;
            // Rec. 601 luma is enough here; this is a legibility guard, not colour science.
            float luma = 0.299f * accent.R + 0.587f * accent.G + 0.114f * accent.B;
            if (DarkMode && luma < 0.4f)
            {
                return Blend(accent, Colors.White, (0.4f - luma) / 0.4f);
            }
            if (!DarkMode && luma > 0.6f)
            {
                return Blend(accent, Colors.Black, (luma - 0.6f) / 0.4f);
            }
            return accent;
        }
    }

    public Color HighlightBorderColor => AccentColor;

    /// <summary>The fill behind a selected thumbnail: the accent, thinned into the surface.</summary>
    public Color HighlightBackgroundColor => Blend(AccentColor, BackgroundColor, DarkMode ? 0.7f : 0.8f);

    /// <summary>Hover fill for a control that is otherwise transparent, e.g. a toolbar button.</summary>
    public Color SubtleHoverColor => DarkMode ? SubtleHoverDark : SubtleHoverLight;

    /// <summary>Pressed fill for a control that is otherwise transparent.</summary>
    public Color SubtlePressedColor => DarkMode ? SubtlePressedDark : SubtlePressedLight;

    /// <summary>Warning/error state, e.g. the icon shown when no scanner was found.</summary>
    public Color CautionColor => DarkMode ? CautionDark : CautionLight;

    public Color NotificationBackgroundColor => DarkMode ? CardDark : CardLight;

    public Color NotificationBorderColor => DarkMode ? DividerDark : DividerLight;

    public Color LinkColor => DarkMode ? Color.FromRgb(0x60cdff) : Color.FromRgb(0x0067c0);

    private static Color Blend(Color from, Color to, float amount) => new(
        from.R + (to.R - from.R) * amount,
        from.G + (to.G - from.G) * amount,
        from.B + (to.B - from.B) * amount);

    public event EventHandler? ColorSchemeChanged;
}
