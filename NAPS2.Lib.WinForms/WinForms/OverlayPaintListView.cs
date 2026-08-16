using System.Drawing;
using System.Windows.Forms;

namespace NAPS2.WinForms;

/// <summary>
/// A ListView that lets a caller draw on top of it after the native control has painted.
///
/// WinForms does not raise <see cref="Control.Paint"/> for a ListView: the paint event only fires
/// for controls with <see cref="ControlStyles.UserPaint"/>, and a native comctl32 control paints
/// itself. Turning UserPaint on would take over item rendering as well, so the drawing is hooked
/// after the native paint instead.
///
/// This exists so the empty state can be drawn into the list's own background rather than overlaid
/// as a separate control: an overlaid control owns a window, and a window sitting in the middle of
/// the list would swallow the file drops that land there.
/// </summary>
public class OverlayPaintListView : ListView
{
    private const int WM_PAINT = 0x000F;

    /// <summary>
    /// Raised after the native control has drawn itself. Handlers must be cheap -- this runs on
    /// every repaint, including during scrolling.
    /// </summary>
    public event PaintEventHandler? OverlayPaint;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_PAINT || OverlayPaint == null || !IsHandleCreated)
        {
            return;
        }
        // The native control has finished its BeginPaint/EndPaint cycle by now, so this draws
        // straight to the window DC.
        try
        {
            using var graphics = Graphics.FromHwnd(Handle);
            using var args = new PaintEventArgs(graphics, ClientRectangle);
            OverlayPaint(this, args);
        }
        catch (Exception ex)
        {
            // This runs inside the message loop, where a throw takes the window with it. A decorative
            // overlay is never worth that, and a silent skip would be indistinguishable from "the
            // handler drew nothing" -- which is exactly the bug that is hard to find later.
            Log.ErrorException("Error painting the list view overlay", ex);
        }
    }
}
