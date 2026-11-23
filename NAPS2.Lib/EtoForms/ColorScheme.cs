using Eto.Drawing;
using NAPS2.EtoForms.Notifications;

namespace NAPS2.EtoForms;

/// <summary>
/// The app's colour palette: Leonardo, the up in blue design language, on top of Windows 11 Fluent.
///
/// Surfaces, text, lines, the accent, selection and links come from <see cref="LeonardoTokens"/>
/// (generated from tools/leonardo/leonardo.tokens.json): cool neutrals with a hint of blue, and brand
/// blue as the accent -- far blue on a dark ground. Status colours stay Fluent's, because Leonardo
/// leaves status to the platform on Windows; the names in comments are the WinUI resources they come
/// from. Everything the UI draws goes through this class -- there is no second place where a colour
/// is picked.
///
/// In a Windows contrast theme every property returns a system colour instead (see
/// <see cref="HighContrast"/>): the theme is an accessibility setting the user chose, and an app that
/// keeps drawing its own palette over it is unreadable for exactly the people who chose it.
/// </summary>
public class ColorScheme
{
    // The Fluent InfoBar severity palette: SystemFillColor<Severity> for the icon, and
    // SystemFillColor<Severity>Background for the surface behind it. Leonardo's own status tokens
    // mirror these values but are web-only; on Windows the platform's colours are the rule.
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

    /// <summary>
    /// True while Windows runs a contrast theme. Every colour then comes from the system, whatever the
    /// app's own theme setting says.
    /// </summary>
    public bool HighContrast => _darkModeProvider.IsHighContrastEnabled;

    public Naps2Config? Config { get; set; }

    public void UserThemeChanged()
    {
        ColorSchemeChanged?.Invoke(this, EventArgs.Empty);
    }

    // The system colour is only asked for in a contrast theme: Eto's SystemColors need a running
    // platform, which tests building a ColorScheme over a stub provider don't have.
    private Color Pick(Color light, Color dark, Func<Color> system) =>
        HighContrast ? system() : DarkMode ? dark : light;

    public Color ForegroundColor =>
        Pick(LeonardoTokens.Light.TextPrimary, LeonardoTokens.Dark.TextPrimary, () => SystemColors.ControlText);

    /// <summary>Explanatory and caption text, one step down from primary.</summary>
    public Color SecondaryTextColor =>
        Pick(LeonardoTokens.Light.TextSecondary, LeonardoTokens.Dark.TextSecondary, () => SystemColors.ControlText);

    /// <summary>
    /// Leonardo's meta role: counts, dates and other small print. In a contrast theme there is no
    /// dimmer text than the window text -- grey is reserved for disabled controls there.
    /// </summary>
    public Color TertiaryTextColor =>
        Pick(LeonardoTokens.Light.TextTertiary, LeonardoTokens.Dark.TextTertiary, () => SystemColors.ControlText);

    /// <summary>The chrome: toolbars, sidebar, panels, dialogs.</summary>
    public Color BackgroundColor =>
        Pick(LeonardoTokens.Light.Surface, LeonardoTokens.Dark.Surface, () => SystemColors.ControlBackground);

    /// <summary>
    /// The canvas the scanned pages sit on (Leonardo's surfaceAlt). Deliberately one step back from the
    /// chrome, so a white page reads as an object lying on a surface rather than as a hole in the window.
    /// </summary>
    public Color CanvasColor =>
        Pick(LeonardoTokens.Light.SurfaceAlt, LeonardoTokens.Dark.SurfaceAlt, () => SystemColors.ControlBackground);

    /// <summary>
    /// The hairline around a page thumbnail: a third of the way from the subtle stroke to the strong
    /// one. The subtle stroke alone disappears against a white page on the light canvas.
    /// </summary>
    public Color PageBorderColor => HighContrast
        ? SystemColors.ControlText
        : DarkMode
            ? Blend(LeonardoTokens.Dark.StrokeSubtle, LeonardoTokens.Dark.StrokeStrong, 0.33f)
            : Blend(LeonardoTokens.Light.StrokeSubtle, LeonardoTokens.Light.StrokeStrong, 0.33f);

    /// <summary>
    /// The drop shadow under a page thumbnail, layered a few times to fake a blur: depth blue at a few
    /// percent. Weaker in dark mode, where a shadow on a near-black canvas does nothing and the border
    /// carries the separation instead. None in a contrast theme, where the border does all of it.
    /// </summary>
    public Color PageShadowColor => HighContrast
        ? Colors.Transparent
        : new Color(LeonardoTokens.Palette.Depth, DarkMode ? 40 / 255f : 26 / 255f);

    public Color SeparatorColor =>
        Pick(LeonardoTokens.Light.StrokeSubtle, LeonardoTokens.Dark.StrokeSubtle, () => SystemColors.ControlText);

    public Color BorderColor => SeparatorColor;

    public Color CropColor => HighContrast ? SystemColors.Highlight : DarkMode ? AccentColor : Colors.Black;

    /// <summary>
    /// The accent: brand blue, far blue on a dark ground. Deliberately not the user's Windows accent --
    /// Leonardo makes brand blue the app's accent, and as a fixed value its contrast is known (white on
    /// brand 4.9:1, depth on far 7.9:1), where any accent a user can pick in Windows is not.
    /// </summary>
    public Color AccentColor =>
        Pick(LeonardoTokens.Light.Accent, LeonardoTokens.Dark.Accent, () => SystemColors.Highlight);

    /// <summary>Text and glyphs drawn on top of <see cref="AccentColor"/> (Leonardo's onAccent).</summary>
    public Color AccentForegroundColor =>
        Pick(LeonardoTokens.Light.OnAccent, LeonardoTokens.Dark.OnAccent, () => SystemColors.HighlightText);

    /// <summary>
    /// The accent thinned almost into the background: a backplate that tints without competing, e.g.
    /// the disc behind an empty state's glyph.
    /// </summary>
    public Color AccentSubtleBackgroundColor =>
        HighContrast ? SystemColors.ControlBackground : Blend(AccentColor, CanvasColor, 0.88f);

    /// <summary>
    /// Hover state of an accent-filled control. Derived the way WinUI derives AccentFillColorSecondary
    /// (the accent at 90 % opacity over the surface), which is also how Leonardo's own hover token is made.
    /// </summary>
    public Color AccentHoverColor =>
        HighContrast ? SystemColors.Highlight : Blend(AccentColor, BackgroundColor, 0.1f);

    /// <summary>Pressed state of an accent-filled control (AccentFillColorTertiary, 80 % opacity).</summary>
    public Color AccentPressedColor =>
        HighContrast ? SystemColors.Highlight : Blend(AccentColor, BackgroundColor, 0.2f);

    public Color HighlightBorderColor => AccentColor;

    /// <summary>The fill behind a selected thumbnail: Leonardo's selection colour.</summary>
    public Color HighlightBackgroundColor =>
        Pick(LeonardoTokens.Light.Selection, LeonardoTokens.Dark.Selection, () => SystemColors.Highlight);

    /// <summary>
    /// Hover fill for a control that is otherwise transparent, e.g. a toolbar button. Fluent's
    /// SubtleFillColorSecondary is the text colour at a few percent over the surface; mixed from
    /// Leonardo's text and surface it keeps the cool tint. None in a contrast theme, where a fill
    /// would put window text on a colour nobody chose.
    /// </summary>
    public Color SubtleHoverColor => HighContrast
        ? SystemColors.ControlBackground
        : Blend(BackgroundColor, ForegroundColor, DarkMode ? 0.06f : 0.04f);

    /// <summary>Pressed fill for a control that is otherwise transparent (SubtleFillColorTertiary).</summary>
    public Color SubtlePressedColor => HighContrast
        ? SystemColors.ControlBackground
        : Blend(BackgroundColor, ForegroundColor, DarkMode ? 0.04f : 0.025f);

    /// <summary>Warning/error state, e.g. the icon shown when no scanner was found.</summary>
    public Color CautionColor => Pick(CautionLight, CautionDark, () => SystemColors.ControlText);

    public Color NotificationBackgroundColor => BackgroundColor;

    public Color NotificationBorderColor => SeparatorColor;

    /// <summary>The tinted surface of a notification reporting an outcome.</summary>
    public Color GetSeverityBackgroundColor(NotificationSeverity severity) => HighContrast
        ? SystemColors.ControlBackground
        : severity switch
        {
            NotificationSeverity.Success => DarkMode ? SuccessBgDark : SuccessBgLight,
            NotificationSeverity.Warning => DarkMode ? CautionBgDark : CautionBgLight,
            NotificationSeverity.Error => DarkMode ? CriticalBgDark : CriticalBgLight,
            _ => NotificationBackgroundColor
        };

    /// <summary>
    /// The icon colour, and the border of the tinted surface. In a contrast theme the severity is
    /// carried by the icon's shape and the message text alone, as the theme demands.
    /// </summary>
    public Color GetSeverityColor(NotificationSeverity severity) => HighContrast
        ? SystemColors.ControlText
        : severity switch
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
    public Color GetSeverityBorderColor(NotificationSeverity severity) =>
        HighContrast ? SystemColors.ControlText
        : severity == NotificationSeverity.Neutral ? NotificationBorderColor
        : Blend(GetSeverityColor(severity), GetSeverityBackgroundColor(severity), 0.55f);

    public Color LinkColor => Pick(LeonardoTokens.Light.Link, LeonardoTokens.Dark.Link, () => SystemColors.LinkText);

    /// <summary>
    /// A colour part of the way from one to another; what motion uses to turn one state colour into the
    /// next, so the steps in between stay on the line between two scheme colours.
    /// </summary>
    public static Color Mix(Color from, Color to, float amount) => Blend(from, to, Math.Clamp(amount, 0, 1));

    private static Color Blend(Color from, Color to, float amount) => new(
        from.R + (to.R - from.R) * amount,
        from.G + (to.G - from.G) * amount,
        from.B + (to.B - from.B) * amount);

    public event EventHandler? ColorSchemeChanged;
}
