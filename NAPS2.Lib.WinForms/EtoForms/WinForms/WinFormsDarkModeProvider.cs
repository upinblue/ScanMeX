using System.Windows.Forms;
using Microsoft.Win32;

namespace NAPS2.EtoForms.WinForms;

public class WinFormsDarkModeProvider : IDarkModeProvider
{
    private bool _darkMode;
    private bool _highContrast;

    /// <remarks>
    /// Both values are read up front rather than on first use, because the change event compares the
    /// system's values against them. With a value still unread, the first preference change of any
    /// kind -- an accessibility client attaching is one -- looked like a theme change, and the window
    /// rebuilt itself for nothing.
    /// </remarks>
    public WinFormsDarkModeProvider()
    {
        _darkMode = ReadDarkMode();
        _highContrast = SystemInformation.HighContrast;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsDarkModeEnabled => _darkMode;

    public bool IsHighContrastEnabled => _highContrast;

    private bool ReadDarkMode()
    {
        try
        {
            using var key =
                Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Equals(key?.GetValue("AppsUseLightTheme"), 0);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public event EventHandler? DarkModeChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var darkMode = ReadDarkMode();
        var highContrast = SystemInformation.HighContrast;
        if (darkMode != _darkMode || highContrast != _highContrast)
        {
            _darkMode = darkMode;
            _highContrast = highContrast;
            Log.Info($"Theme changed: dark mode {darkMode}, contrast theme {highContrast}.");
            DarkModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
