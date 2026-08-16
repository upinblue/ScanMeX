using Eto.Drawing;
using NAPS2.EtoForms.Notifications;

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

    // TextFillColorSecondary: explanatory and caption text, one step down from primary.
    private static readonly Color TextSecondaryLight = Color.FromRgb(0x5d5d5d);
    private static readonly Color TextSecondaryDark = Color.FromRgb(0xc5c5c5);

    // LayerFillColorDefault: the chrome -- toolbars, sidebar, dialogs.
    private static readonly Color SurfaceLight = Color.FromRgb(0xffffff);
    private static readonly Color SurfaceDark = Color.FromRgb(0x202020);

    // SolidBackgroundFillColorBase: the canvas the scanned pages sit on. Deliberately one step back
    // from the chrome, so a white page reads as an object lying on a surface rather than as a hole
    // in the window. This is the "elevation and layering" part of Fluent, and it is also why a
    // document app wants the canvas darker rather than lighter.
    private static readonly Color CanvasLight = Color.FromRgb(0xf3f3f3);
    private static readonly Color CanvasDark = Color.FromRgb(0x191919);

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

    // The Fluent InfoBar severity palette: SystemFillColor<Severity> for the icon, and
    // SystemFillColor<Severity>Background for the surface behind it.
    private static readonly Color SuccessLight = Color.FromRgb(0x0f7b0f);
    private static readonly Color SuccessDark = Color.FromRgb(0x6ccb5f);
    private static readonly Color SuccessBgLight = Color.FromRgb(0xdff6dd);
    private static readonly Color SuccessBgDark = Color.FromRgb(0x393d1b);

    private static readonly Color CautionLight = Color.FromRgb(0x9d5d00);
    private static readonly Color CautionDark = Color.FromRgb(0xfce100);
    private static readonly Color CautionBgLight = Color.FromRgb(0xfff4ce);
    private static readonly Color CautionBgDark = Color.FromRgb(0x433519);

    private static readonly Color CriticalLight = Color.FromRgb(0xc42b1c);
    private static readonly Color CriticalDark = Color.FromRgb(0xff99a4);
    private static readonly Color CriticalBgLight = Color.FromRgb(0xfde7e9);
    private static readonly Color CriticalBgDark = Color.FromRgb(0x442726);

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

    public Color SecondaryTextColor => DarkMode ? TextSecondaryDark : TextSecondaryLight;

    public Color BackgroundColor => DarkMode ? SurfaceDark : SurfaceLight;

    /// <summary>
    /// The background of the page thumbnail area. See <see cref="CanvasLight"/> for why it differs
    /// from <see cref="BackgroundColor"/>.
    /// </summary>
    public Color CanvasColor => DarkMode ? CanvasDark : CanvasLight;

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
            float luma = Luma(accent);
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

    /// <summary>
    /// Text and glyphs drawn on top of <see cref="AccentColor"/> (TextOnAccentFillColorPrimary).
    /// Chosen from the accent's own brightness rather than from the theme: Fluent uses black on the
    /// accent in dark mode because Windows lightens it there, but the user's accent can be any
    /// colour and the contrast guard in <see cref="AccentColor"/> only nudges it, so deciding by
    /// theme puts dark text on a dark blue.
    /// </summary>
    public Color AccentForegroundColor => Luma(AccentColor) > 0.55f ? Colors.Black : Colors.White;

    /// <summary>
    /// The accent thinned almost into the background: a backplate that tints without competing, e.g.
    /// the disc behind an empty state's glyph.
    /// </summary>
    public Color AccentSubtleBackgroundColor => Blend(AccentColor, CanvasColor, 0.88f);

    /// <summary>Hover state of an accent-filled control (AccentFillColorSecondary).</summary>
    public Color AccentHoverColor => Blend(AccentColor, BackgroundColor, 0.1f);

    /// <summary>Pressed state of an accent-filled control (AccentFillColorTertiary).</summary>
    public Color AccentPressedColor => Blend(AccentColor, BackgroundColor, 0.2f);

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

    /// <summary>The tinted surface of a notification reporting an outcome.</summary>
    public Color GetSeverityBackgroundColor(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => DarkMode ? SuccessBgDark : SuccessBgLight,
        NotificationSeverity.Warning => DarkMode ? CautionBgDark : CautionBgLight,
        NotificationSeverity.Error => DarkMode ? CriticalBgDark : CriticalBgLight,
        _ => NotificationBackgroundColor
    };

    /// <summary>The icon colour, and the border of the tinted surface.</summary>
    public Color GetSeverityColor(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => DarkMode ? SuccessDark : SuccessLight,
        NotificationSeverity.Warning => CautionColor,
        NotificationSeverity.Error => DarkMode ? CriticalDark : CriticalLight,
        _ => ForegroundColor
    };

    /// <summary>
    /// The border of a severity-tinted notification: the severity colour thinned into its own
    /// background, so it reads as an edge rather than as a second, louder message.
    /// </summary>
    public Color GetSeverityBorderColor(NotificationSeverity severity) => severity == NotificationSeverity.Neutral
        ? NotificationBorderColor
        : Blend(GetSeverityColor(severity), GetSeverityBackgroundColor(severity), 0.55f);

    public Color LinkColor => DarkMode ? Color.FromRgb(0x60cdff) : Color.FromRgb(0x0067c0);

    /// <summary>Rec. 601 luma. Enough for legibility decisions; this is not colour science.</summary>
    private static float Luma(Color color) => 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;

    private static Color Blend(Color from, Color to, float amount) => new(
        from.R + (to.R - from.R) * amount,
        from.G + (to.G - from.G) * amount,
        from.B + (to.B - from.B) * amount);

    public event EventHandler? ColorSchemeChanged;
}
