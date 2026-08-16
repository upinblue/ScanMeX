using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Eto.WinForms;
using NAPS2.EtoForms;

namespace NAPS2.WinForms;

/// <summary>
/// Draws toolbars and menus the way Windows 11 does: no gradients, no chrome around the bar itself,
/// and a rounded backplate that only appears under the pointer.
///
/// It derives from <see cref="ToolStripProfessionalRenderer"/> rather than ToolStripRenderer so the
/// parts that are already fine -- arrows, check marks, the overflow button -- keep working; the
/// colour table below covers those, and the overrides replace everything that used to be drawn with
/// a gradient or a hard border.
/// </summary>
public class FluentToolStripRenderer : ToolStripProfessionalRenderer
{
    private const int CORNER_RADIUS = FluentShapes.CONTROL_CORNER_RADIUS;

    /// <summary>Keeps a hovered backplate clear of its neighbours and of the bar's edges.</summary>
    private const int ITEM_INSET = 2;

    private readonly ColorScheme _colorScheme;

    public FluentToolStripRenderer(ColorScheme colorScheme) : base(new FluentColorTable(colorScheme))
    {
        _colorScheme = colorScheme;
        RoundedEdges = false;
    }

    private static float GetScale(ToolStrip? toolStrip) => (toolStrip?.DeviceDpi ?? 96) / 96f;

    private void FillBackplate(Graphics g, Rectangle bounds, Color color, float scale)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentShapes.RoundedRect(bounds, CORNER_RADIUS * scale);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    /// <returns>The hover/pressed fill for an item, or null if it should stay transparent.</returns>
    private Color? GetItemStateColor(ToolStripItem item)
    {
        if (!item.Enabled)
        {
            return null;
        }
        if (item.Pressed)
        {
            return _colorScheme.SubtlePressedColor.ToSD();
        }
        if (item.Selected)
        {
            return _colorScheme.SubtleHoverColor.ToSD();
        }
        // A checked button (e.g. a toggled sidebar) keeps a resting backplate so its state is
        // visible when the pointer is elsewhere.
        if (item is ToolStripButton { Checked: true })
        {
            return _colorScheme.SubtlePressedColor.ToSD();
        }
        return null;
    }

    private void DrawItemBackplate(ToolStripItemRenderEventArgs e)
    {
        var color = GetItemStateColor(e.Item);
        if (color == null)
        {
            return;
        }
        float scale = GetScale(e.ToolStrip);
        int inset = (int) Math.Round(ITEM_INSET * scale);
        var bounds = new Rectangle(inset, inset,
            e.Item.Width - inset * 2, e.Item.Height - inset * 2);
        FillBackplate(e.Graphics, bounds, color.Value, scale);
    }

    // --- the bar itself -------------------------------------------------------------------------

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        // A drop-down is a floating surface and gets the card colour; a docked bar continues the
        // window and gets the window colour.
        var color = e.ToolStrip is ToolStripDropDown
            ? _colorScheme.NotificationBackgroundColor.ToSD()
            : _colorScheme.BackgroundColor.ToSD();
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is not ToolStripDropDown)
        {
            // Fluent toolbars sit flush against the content; the line below the toolbar is drawn by
            // WinFormsDesktopForm.DrawContentBorders instead.
            return;
        }
        using var pen = new Pen(_colorScheme.NotificationBorderColor.ToSD());
        var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
        e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
    {
        using var brush = new SolidBrush(_colorScheme.BackgroundColor.ToSD());
        e.Graphics.FillRectangle(brush, e.ToolStripPanel.ClientRectangle);
        e.Handled = true;
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Windows 11 menus have no separate grey gutter behind the icons.
        using var brush = new SolidBrush(_colorScheme.NotificationBackgroundColor.ToSD());
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
    {
        // The toolbars are dockable and the position is persisted, so the drag affordance has to
        // stay -- but as one faint line rather than the classic double row of dots.
        if (e.GripStyle != ToolStripGripStyle.Visible)
        {
            return;
        }
        float scale = GetScale(e.ToolStrip);
        var b = e.GripBounds;
        using var brush = new SolidBrush(_colorScheme.SeparatorColor.ToSD());
        if (e.GripDisplayStyle == ToolStripGripDisplayStyle.Vertical)
        {
            int w = Math.Max(1, (int) Math.Round(scale));
            e.Graphics.FillRectangle(brush, b.X + b.Width / 2, b.Y + (int) (4 * scale), w,
                Math.Max(0, b.Height - (int) (8 * scale)));
        }
        else
        {
            int h = Math.Max(1, (int) Math.Round(scale));
            e.Graphics.FillRectangle(brush, b.X + (int) (4 * scale), b.Y + b.Height / 2,
                Math.Max(0, b.Width - (int) (8 * scale)), h);
        }
    }

    // --- items ----------------------------------------------------------------------------------

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => DrawItemBackplate(e);

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) =>
        DrawItemBackplate(e);

    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
        DrawItemBackplate(e);
        if (e.Item is not ToolStripSplitButton splitButton)
        {
            return;
        }
        // The professional renderer draws the split button's arrow as part of its background pass,
        // so replacing that pass means drawing the arrow here too, or it disappears.
        float scale = GetScale(e.ToolStrip);
        var arrowBounds = splitButton.DropDownButtonBounds;
        if (splitButton.Enabled)
        {
            using var pen = new Pen(_colorScheme.SeparatorColor.ToSD());
            int inset = (int) Math.Round(6 * scale);
            e.Graphics.DrawLine(pen, arrowBounds.Left, arrowBounds.Top + inset,
                arrowBounds.Left, arrowBounds.Bottom - inset);
        }
        DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, e.Item, arrowBounds,
            GetTextColor(e.Item), ArrowDirection.Down));
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var color = GetItemStateColor(e.Item);
        if (color == null)
        {
            return;
        }
        float scale = GetScale(e.ToolStrip);
        int inset = (int) Math.Round(ITEM_INSET * scale);
        var bounds = new Rectangle(inset, inset, e.Item.Width - inset * 2, e.Item.Height - inset * 2);
        FillBackplate(e.Graphics, bounds, color.Value, scale);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float scale = GetScale(e.ToolStrip);
        int inset = (int) Math.Round(4 * scale);
        using var pen = new Pen(_colorScheme.SeparatorColor.ToSD());
        var b = e.Item.Bounds;
        if (e.Vertical)
        {
            int x = b.Width / 2;
            e.Graphics.DrawLine(pen, x, inset, x, Math.Max(inset, b.Height - inset));
        }
        else
        {
            int y = b.Height / 2;
            e.Graphics.DrawLine(pen, inset, y, Math.Max(inset, b.Width - inset), y);
        }
    }

    private Color GetTextColor(ToolStripItem item) => item.Enabled
        ? _colorScheme.ForegroundColor.ToSD()
        // Fluent dims disabled text rather than greying it to a fixed colour.
        : Blend(_colorScheme.ForegroundColor.ToSD(), _colorScheme.BackgroundColor.ToSD(), 0.6f);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = GetTextColor(e.Item);
        base.OnRenderItemText(e);
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        from.A,
        (int) (from.R + (to.R - from.R) * amount),
        (int) (from.G + (to.G - from.G) * amount),
        (int) (from.B + (to.B - from.B) * amount));

    /// <summary>
    /// Covers the parts still drawn by the base renderer, so nothing falls back to the Office-style
    /// blues when a state this class doesn't override comes up.
    /// </summary>
    private class FluentColorTable(ColorScheme colorScheme) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => colorScheme.NotificationBackgroundColor.ToSD();
        public override Color ImageMarginGradientBegin => colorScheme.NotificationBackgroundColor.ToSD();
        public override Color ImageMarginGradientMiddle => colorScheme.NotificationBackgroundColor.ToSD();
        public override Color ImageMarginGradientEnd => colorScheme.NotificationBackgroundColor.ToSD();
        public override Color MenuBorder => colorScheme.NotificationBorderColor.ToSD();
        public override Color MenuItemBorder => colorScheme.SubtleHoverColor.ToSD();
        public override Color MenuItemSelected => colorScheme.SubtleHoverColor.ToSD();
        public override Color MenuItemSelectedGradientBegin => colorScheme.SubtleHoverColor.ToSD();
        public override Color MenuItemSelectedGradientEnd => colorScheme.SubtleHoverColor.ToSD();
        public override Color MenuItemPressedGradientBegin => colorScheme.SubtlePressedColor.ToSD();
        public override Color MenuItemPressedGradientMiddle => colorScheme.SubtlePressedColor.ToSD();
        public override Color MenuItemPressedGradientEnd => colorScheme.SubtlePressedColor.ToSD();
        public override Color ButtonSelectedHighlight => colorScheme.SubtleHoverColor.ToSD();
        public override Color ButtonSelectedHighlightBorder => colorScheme.SubtleHoverColor.ToSD();
        public override Color ButtonPressedHighlight => colorScheme.SubtlePressedColor.ToSD();
        public override Color ButtonPressedHighlightBorder => colorScheme.SubtlePressedColor.ToSD();
        public override Color ButtonCheckedHighlight => colorScheme.SubtlePressedColor.ToSD();
        public override Color CheckBackground => colorScheme.SubtlePressedColor.ToSD();
        public override Color CheckSelectedBackground => colorScheme.SubtlePressedColor.ToSD();
        public override Color SeparatorDark => colorScheme.SeparatorColor.ToSD();
        public override Color SeparatorLight => colorScheme.SeparatorColor.ToSD();
        public override Color ToolStripBorder => colorScheme.BackgroundColor.ToSD();
        public override Color ToolStripGradientBegin => colorScheme.BackgroundColor.ToSD();
        public override Color ToolStripGradientMiddle => colorScheme.BackgroundColor.ToSD();
        public override Color ToolStripGradientEnd => colorScheme.BackgroundColor.ToSD();
        public override Color ToolStripPanelGradientBegin => colorScheme.BackgroundColor.ToSD();
        public override Color ToolStripPanelGradientEnd => colorScheme.BackgroundColor.ToSD();
        public override Color OverflowButtonGradientBegin => colorScheme.BackgroundColor.ToSD();
        public override Color OverflowButtonGradientMiddle => colorScheme.BackgroundColor.ToSD();
        public override Color OverflowButtonGradientEnd => colorScheme.BackgroundColor.ToSD();
    }
}
