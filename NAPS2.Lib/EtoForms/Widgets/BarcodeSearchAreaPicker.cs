using Eto.Drawing;
using Eto.Forms;
using NAPS2.Scan;

namespace NAPS2.EtoForms.Widgets;

/// <summary>
/// A sheet of paper to draw a rectangle on: the part of the page barcode detection is restricted to.
/// </summary>
/// <remarks>
/// The setting is four numbers between 0 and 1, and four numbers is exactly the form in which nobody can
/// tell whether the area covers the barcode on the paperwork in front of them. So the control is the
/// paper: a page in the profile's proportions, with the area drawn on it and the numbers written
/// underneath for the operator who does want to check them.
///
/// The area is kept in page fractions rather than in pixels of this control, so the same profile holds at
/// any resolution, any paper size, and any DPI this dialog happens to be shown at.
/// </remarks>
public class BarcodeSearchAreaPicker : Drawable
{
    /// <summary>A4's ratio, which is what the paperwork this exists for is printed on.</summary>
    private const float PAPER_ASPECT = 1.4142f;

    /// <summary>Space between the control's edge and the paper, at 96 dpi.</summary>
    private const float MARGIN = 6f;

    /// <summary>How close to an edge counts as grabbing it rather than the area itself, at 96 dpi.</summary>
    private const float GRAB_TOLERANCE = 7f;

    /// <summary>The side of a corner handle, at 96 dpi.</summary>
    private const float HANDLE_SIZE = 7f;

    /// <summary>The size the layout should reserve, at 96 dpi. The paper is fitted inside it.</summary>
    public const int NATURAL_WIDTH = 190;

    public const int NATURAL_HEIGHT = 250;

    [Flags]
    private enum Edges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    private enum DragKind
    {
        None,

        /// <summary>Dragging on bare paper draws a new area from where the drag started.</summary>
        Draw,

        /// <summary>Dragging inside the area moves it, keeping its size.</summary>
        Move,

        /// <summary>Dragging an edge or a corner moves those edges only.</summary>
        Resize
    }

    private BarcodeSearchArea _area = BarcodeSearchArea.WholePage;
    private float _scale = 1f;

    private DragKind _dragKind;
    private Edges _dragEdges;
    private PointF _dragAnchor;
    private BarcodeSearchArea _dragStartArea = BarcodeSearchArea.WholePage;

    public BarcodeSearchAreaPicker()
    {
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        // Subscribes, so it belongs here and nowhere that runs again -- each call adds a handler that
        // lives until the control's handle is destroyed.
        EtoPlatform.Current.AttachDpiDependency(this, scale =>
        {
            _scale = scale;
            Invalidate();
        });
    }

    /// <summary>
    /// The area, in fractions of the page. Setting it does not raise <see cref="AreaChanged"/>: that
    /// event means "the operator changed this", which is what the dialog reacts to.
    /// </summary>
    public BarcodeSearchArea Area
    {
        get => _area;
        set
        {
            _area = (value ?? BarcodeSearchArea.WholePage).Normalized();
            Invalidate();
        }
    }

    /// <summary>
    /// Raised while the area is being dragged as well as when the drag ends, so a readout of the
    /// coordinates follows the rectangle instead of catching up with it afterwards.
    /// </summary>
    public event EventHandler? AreaChanged;

    private void SetAreaFromUser(BarcodeSearchArea area)
    {
        var normalized = area.Normalized();
        if (normalized == _area)
        {
            return;
        }
        _area = normalized;
        Invalidate();
        AreaChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The paper, centred in the control at the proportions of a portrait page. Empty when the control
    /// has not been laid out yet, which every caller has to cope with -- a paint can arrive first.
    /// </summary>
    private RectangleF GetPaperRect()
    {
        var margin = MARGIN * _scale;
        var availableWidth = Width - 2 * margin;
        var availableHeight = Height - 2 * margin;
        if (availableWidth <= 1 || availableHeight <= 1)
        {
            return RectangleF.Empty;
        }
        var paperWidth = Math.Min(availableWidth, availableHeight / PAPER_ASPECT);
        var paperHeight = paperWidth * PAPER_ASPECT;
        return new RectangleF((Width - paperWidth) / 2f, (Height - paperHeight) / 2f, paperWidth,
            paperHeight);
    }

    private RectangleF GetAreaRect(RectangleF paper) => new(
        paper.X + (float) _area.X * paper.Width,
        paper.Y + (float) _area.Y * paper.Height,
        (float) _area.Width * paper.Width,
        (float) _area.Height * paper.Height);

    /// <summary>
    /// Where a point on the control sits on the page, 0..1. Clamped, so a drag that leaves the control --
    /// which is easy on a rectangle that reaches the paper's edge -- pins the edge to the paper instead
    /// of producing an area that is partly off the page.
    /// </summary>
    private PointF ToPageFraction(PointF location, RectangleF paper) => new(
        Math.Clamp((location.X - paper.X) / paper.Width, 0f, 1f),
        Math.Clamp((location.Y - paper.Y) / paper.Height, 0f, 1f));

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var colorScheme = EtoPlatform.Current.ColorScheme;
        var g = e.Graphics;
        // Controls paint their own background, so the surface behind the paper is drawn rather than
        // inherited -- otherwise the control shows as a pale box on the tab.
        g.FillRectangle(colorScheme.BackgroundColor, e.ClipRectangle);

        var paper = GetPaperRect();
        if (paper.IsEmpty)
        {
            return;
        }

        g.AntiAlias = false;
        // A white sheet at full brightness is a glaring block in dark mode, so it is taken towards the
        // canvas colour there. It still has to read as paper: this is the thing the rectangle is on.
        var paperColor = colorScheme.DarkMode
            ? Color.Blend(Colors.White, colorScheme.CanvasColor, 0.3f)
            : Colors.White;
        var dimmed = !Enabled;
        if (dimmed)
        {
            paperColor = Color.Blend(paperColor, colorScheme.BackgroundColor, 0.55f);
        }
        g.FillRectangle(paperColor, paper);
        g.DrawRectangle(colorScheme.PageBorderColor, paper.X, paper.Y, paper.Width - 1, paper.Height - 1);

        // A few ruled lines so the sheet reads as a page of paperwork rather than as an empty panel, and
        // so the area drawn on it is visibly *on* something.
        var ruleColor = Color.Blend(paperColor, colorScheme.SecondaryTextColor, dimmed ? 0.12f : 0.22f);
        for (var i = 1; i <= 8; i++)
        {
            var y = paper.Y + paper.Height * i / 9f;
            g.DrawLine(ruleColor, paper.X + paper.Width * 0.12f, y, paper.X + paper.Width * 0.88f, y);
        }

        var area = GetAreaRect(paper);
        var accent = dimmed
            ? Color.Blend(colorScheme.AccentColor, colorScheme.BackgroundColor, 0.5f)
            : colorScheme.AccentColor;
        // Semi-transparent, because what is underneath -- how much of the page is left out -- is the
        // whole question the control answers.
        g.FillRectangle(new Color(accent, 0.22f), area);
        g.DrawRectangle(new Pen(accent, Math.Max(1f, _scale)), area.X, area.Y, area.Width - 1,
            area.Height - 1);

        if (!dimmed)
        {
            var handle = HANDLE_SIZE * _scale;
            foreach (var corner in new[]
                     {
                         new PointF(area.Left, area.Top), new PointF(area.Right, area.Top),
                         new PointF(area.Left, area.Bottom), new PointF(area.Right, area.Bottom)
                     })
            {
                var rect = new RectangleF(corner.X - handle / 2f, corner.Y - handle / 2f, handle, handle);
                g.FillRectangle(accent, rect);
                g.DrawRectangle(paperColor, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        var paper = GetPaperRect();
        if (!Enabled || paper.IsEmpty || e.Buttons != MouseButtons.Primary)
        {
            return;
        }
        var point = ToPageFraction(e.Location, paper);
        _dragStartArea = _area;
        _dragAnchor = point;
        _dragEdges = GetEdgesUnderMouse(e.Location, paper);
        if (_dragEdges != Edges.None)
        {
            _dragKind = DragKind.Resize;
        }
        else if (GetAreaRect(paper).Contains(e.Location))
        {
            _dragKind = DragKind.Move;
        }
        else
        {
            // Starting on bare paper draws a new area rather than nudging the old one, which is the
            // quickest way to say "the barcode is over here" on a page that is mostly not it.
            _dragKind = DragKind.Draw;
            SetAreaFromUser(FromCorners(point, point));
        }
        UpdateCursor(e.Location, paper);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        var paper = GetPaperRect();
        if (!Enabled || paper.IsEmpty)
        {
            return;
        }
        UpdateCursor(e.Location, paper);
        if (_dragKind == DragKind.None)
        {
            return;
        }
        var point = ToPageFraction(e.Location, paper);
        SetAreaFromUser(_dragKind switch
        {
            DragKind.Draw => FromCorners(_dragAnchor, point),
            DragKind.Move => MoveTo(point),
            _ => ResizeTo(point)
        });
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (_dragKind == DragKind.None)
        {
            return;
        }
        _dragKind = DragKind.None;
        _dragEdges = Edges.None;
        // The area is already at its final value; this is the event a caller that only wants the
        // finished rectangle -- not every frame of the drag -- would listen for.
        AreaChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// An area from two opposite corners, in either order, so a drag up and to the left works like one
    /// down and to the right. The minimum size is applied by <see cref="BarcodeSearchArea.Normalized"/>,
    /// which is also what keeps a single click from producing an area of nothing.
    /// </summary>
    private static BarcodeSearchArea FromCorners(PointF a, PointF b) => new()
    {
        X = Math.Min(a.X, b.X),
        Y = Math.Min(a.Y, b.Y),
        Width = Math.Abs(b.X - a.X),
        Height = Math.Abs(b.Y - a.Y)
    };

    private BarcodeSearchArea MoveTo(PointF point)
    {
        // The area keeps its size and stays on the page, so dragging it into a corner parks it there
        // rather than shrinking it.
        var x = _dragStartArea.X + (point.X - _dragAnchor.X);
        var y = _dragStartArea.Y + (point.Y - _dragAnchor.Y);
        return _dragStartArea with
        {
            X = Math.Clamp(x, 0, 1 - _dragStartArea.Width),
            Y = Math.Clamp(y, 0, 1 - _dragStartArea.Height)
        };
    }

    private BarcodeSearchArea ResizeTo(PointF point)
    {
        double left = _dragStartArea.X, top = _dragStartArea.Y;
        double right = _dragStartArea.X + _dragStartArea.Width;
        double bottom = _dragStartArea.Y + _dragStartArea.Height;
        var min = BarcodeSearchArea.MIN_SIZE;
        if (_dragEdges.HasFlag(Edges.Left)) left = Math.Clamp(point.X, 0, right - min);
        if (_dragEdges.HasFlag(Edges.Right)) right = Math.Clamp(point.X, left + min, 1);
        if (_dragEdges.HasFlag(Edges.Top)) top = Math.Clamp(point.Y, 0, bottom - min);
        if (_dragEdges.HasFlag(Edges.Bottom)) bottom = Math.Clamp(point.Y, top + min, 1);
        return new BarcodeSearchArea
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top
        };
    }

    /// <summary>
    /// Which edges the pointer is close enough to to grab. The tolerance is in control pixels rather than
    /// in page fractions, so grabbing an edge feels the same however small the paper is drawn.
    /// </summary>
    private Edges GetEdgesUnderMouse(PointF location, RectangleF paper)
    {
        var area = GetAreaRect(paper);
        var tolerance = GRAB_TOLERANCE * _scale;
        // Only along the edge itself, so the tolerance box around a corner doesn't reach out past it.
        var withinRows = location.Y >= area.Top - tolerance && location.Y <= area.Bottom + tolerance;
        var withinColumns = location.X >= area.Left - tolerance && location.X <= area.Right + tolerance;
        var edges = Edges.None;
        if (withinRows && Math.Abs(location.X - area.Left) <= tolerance) edges |= Edges.Left;
        if (withinRows && Math.Abs(location.X - area.Right) <= tolerance) edges |= Edges.Right;
        if (withinColumns && Math.Abs(location.Y - area.Top) <= tolerance) edges |= Edges.Top;
        if (withinColumns && Math.Abs(location.Y - area.Bottom) <= tolerance) edges |= Edges.Bottom;
        // An area at its minimum size has both edges under the same pointer; the one that grows the area
        // is the one that can still be dragged anywhere.
        if (edges.HasFlag(Edges.Left) && edges.HasFlag(Edges.Right)) edges &= ~Edges.Left;
        if (edges.HasFlag(Edges.Top) && edges.HasFlag(Edges.Bottom)) edges &= ~Edges.Top;
        return edges;
    }

    private void UpdateCursor(PointF location, RectangleF paper)
    {
        var edges = _dragKind == DragKind.Resize ? _dragEdges : GetEdgesUnderMouse(location, paper);
        if (edges != Edges.None)
        {
            // Only the two split cursors are used for edges: they are the ones every Eto platform this
            // app runs on has, and a corner is a crosshair anyway.
            var horizontal = edges.HasFlag(Edges.Left) || edges.HasFlag(Edges.Right);
            var vertical = edges.HasFlag(Edges.Top) || edges.HasFlag(Edges.Bottom);
            Cursor = horizontal && vertical ? Cursors.Crosshair :
                horizontal ? Cursors.VerticalSplit : Cursors.HorizontalSplit;
        }
        else if (_dragKind == DragKind.Move || GetAreaRect(paper).Contains(location))
        {
            Cursor = Cursors.Move;
        }
        else
        {
            Cursor = Cursors.Crosshair;
        }
    }
}
