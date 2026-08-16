using System.Drawing;
using System.Drawing.Drawing2D;

namespace NAPS2.WinForms;

/// <summary>
/// The rounded rectangle Fluent uses everywhere. Shared so the toolbar renderer and the accent
/// button agree on the geometry rather than each rounding corners their own way.
/// </summary>
internal static class FluentShapes
{
    /// <summary>Fluent: 4px for in-page elements such as buttons and list backplates.</summary>
    public const int CONTROL_CORNER_RADIUS = 4;

    public static GraphicsPath RoundedRect(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();
        // Clamp so a short or narrow control degrades to a plain rectangle instead of drawing arcs
        // that overlap and produce a bowtie.
        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        float d = radius * 2;
        if (d < 2 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
