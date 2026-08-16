using Microsoft.Win32;
using EtoColor = Eto.Drawing.Color;

namespace NAPS2.EtoForms.WinForms;

public class WinFormsDarkModeProvider : IDarkModeProvider
{
    private bool? _value;
    private EtoColor? _accent;
    private bool _accentRead;

    public WinFormsDarkModeProvider()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsDarkModeEnabled => _value ??= ReadDarkMode();

    public EtoColor? AccentColor
    {
        get
        {
            if (!_accentRead)
            {
                _accent = ReadAccentColor();
                _accentRead = true;
            }
            return _accent;
        }
    }

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

    /// <summary>
    /// Reads the accent colour the user picked in Settings > Personalization. DWM stores it as a
    /// single DWORD in 0xAABBGGRR order, i.e. the byte order is reversed relative to the usual
    /// ARGB, which is why the red and blue components are swapped back here.
    /// </summary>
    private EtoColor? ReadAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int abgr)
            {
                return null;
            }
            return EtoColor.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public event EventHandler? DarkModeChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var newValue = ReadDarkMode();
        var newAccent = ReadAccentColor();
        if (newValue != _value || newAccent != _accent)
        {
            _value = newValue;
            _accent = newAccent;
            _accentRead = true;
            DarkModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
