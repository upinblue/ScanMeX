using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NAPS2.WinForms;

/// <summary>
/// The one thing the managed ListView will not tell us about its groups: where a group's heading is.
/// </summary>
/// <remarks>
/// Needed because the headings are drawn over the control rather than by it -- comctl32 draws its own in
/// the light Explorer blue whatever the window's theme, and no theming call changes that. Kept apart from
/// the list view itself because it reaches through a non-public property to get the group's native id,
/// which is the part most likely to need attention on a future runtime; everything here returns null
/// rather than throwing, and the caller falls back to deriving the band from the items.
/// </remarks>
public static class ListViewGroups
{
    private const int LVM_FIRST = 0x1000;
    private const int LVM_GETGROUPRECT = LVM_FIRST + 98;
    private const int LVGGR_HEADER = 1;

    private static readonly PropertyInfo? IdProperty =
        typeof(ListViewGroup).GetProperty("ID", BindingFlags.NonPublic | BindingFlags.Instance);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    /// <summary>
    /// The heading band of the given group, in the same coordinates as <see cref="ListViewItem.Bounds"/>
    /// -- so it scrolls with the items -- or null if the control would not say.
    /// </summary>
    public static Rectangle? HeaderBounds(ListView view, int groupIndex)
    {
        try
        {
            if (IdProperty == null || groupIndex < 0 || groupIndex >= view.Groups.Count ||
                !view.IsHandleCreated)
            {
                return null;
            }
            if (IdProperty.GetValue(view.Groups[groupIndex]) is not int id)
            {
                return null;
            }
            var rc = new RECT { Top = LVGGR_HEADER };
            if (SendMessage(view.Handle, LVM_GETGROUPRECT, id, ref rc) == IntPtr.Zero)
            {
                return null;
            }
            return new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
        }
        catch (Exception ex)
        {
            Log.ErrorException("Could not read a list view group's bounds", ex);
            return null;
        }
    }
}
