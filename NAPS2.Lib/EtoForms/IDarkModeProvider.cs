using Eto.Drawing;

namespace NAPS2.EtoForms;

public interface IDarkModeProvider
{
    bool IsDarkModeEnabled { get; }

    /// <summary>
    /// The accent colour the user picked in their OS settings, or null if the platform doesn't
    /// expose one. ColorScheme adjusts it for contrast and falls back to the Fluent default blue,
    /// so a platform that has nothing to report can leave this alone.
    /// </summary>
    Color? AccentColor => null;

    event EventHandler? DarkModeChanged;
}
