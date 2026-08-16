using System.Runtime.InteropServices;

namespace NAPS2.Platform.Windows;

/// <summary>
/// Opts the app's windows into the Windows 11 frame: rounded corners and a title bar that follows
/// the app's theme rather than the system's.
///
/// Both are hints. On Windows 10 the attributes don't exist and dwmapi returns a failure HRESULT,
/// which is why every call here ignores the result -- the app targets Windows 10 1809 and must keep
/// running there with the old square frame.
/// </summary>
internal static class DwmWindowStyle
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute,
        int cbAttribute);

    private static void Set(IntPtr handle, int attribute, int value)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }
        try
        {
            DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (Exception ex)
        {
            // dwmapi.dll is present on every supported version, so a throw here means something
            // stranger than an old OS; the window is still perfectly usable unstyled.
            Log.ErrorException("Could not apply the DWM window style", ex);
        }
    }

    /// <summary>
    /// Asks the system to round a top-level window. Standard WinForms windows are usually rounded
    /// by policy already, in which case this changes nothing.
    /// </summary>
    public static void UseRoundedCorners(IntPtr handle) =>
        Set(handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    /// <summary>
    /// Rounds a pop-up window with the smaller radius Windows uses for menus. Pop-ups are not
    /// rounded automatically, so drop-downs need this explicitly.
    /// </summary>
    public static void UseSmallRoundedCorners(IntPtr handle) =>
        Set(handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUNDSMALL);

    /// <summary>
    /// Switches the title bar between the light and dark frame. .NET 9's experimental WinForms dark
    /// mode covers this for most windows, but it is set explicitly so the caption matches the app's
    /// theme even when the user has overridden it against the system setting.
    /// </summary>
    public static void UseDarkTitleBar(IntPtr handle, bool dark) =>
        Set(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
}
