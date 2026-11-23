namespace NAPS2.EtoForms;

public interface IDarkModeProvider
{
    bool IsDarkModeEnabled { get; }

    /// <summary>
    /// True while the OS runs a contrast theme (Windows: Settings > Accessibility > Contrast themes).
    /// ColorScheme then hands out the system colours instead of its own, because a contrast theme is
    /// an accessibility setting the user chose and the app's palette must not override it. A
    /// platform that has no such mode can leave this alone.
    /// </summary>
    bool IsHighContrastEnabled => false;

    /// <summary>Raised when dark mode or the contrast theme changes.</summary>
    event EventHandler? DarkModeChanged;
}
