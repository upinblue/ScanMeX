using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Eto.WinForms;
using Eto.WinForms.Forms.Menu;
using NAPS2.EtoForms.Widgets;
using NAPS2.WinForms;
using ContextMenu = Eto.Forms.ContextMenu;

namespace NAPS2.EtoForms.WinForms;

public class WinFormsListView<T> : IListView<T> where T : notnull
{
    private Pen DefaultPen => new(_behavior.ColorScheme.PageBorderColor.ToSD(), 1);
    private const int PageNumberTextPadding = 6;
    private const int PageNumberSelectionPadding = 3;
    /// <summary>How far the drop shadow reaches past the page, before DPI scaling.</summary>
    private const int PageShadowDepth = 3;
    private static readonly StringFormat PageNumberLabelFormat = new()
        { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

    private readonly ListView _view;
    private readonly Eto.Forms.Control _viewEtoControl;
    private readonly ListViewBehavior<T> _behavior;

    private ListSelection<T> _selection = ListSelection.Empty<T>();
    private bool _refreshing;
    private ContextMenu? _contextMenu;
    private IReadOnlyList<ListViewSection> _sections = [];
    /// <summary>Where a drop would insert, drawn by us; see <see cref="DrawDropIndicator"/>.</summary>
    private int _dropIndex = -1;
    private bool _dropAfterItem;
    private float _dpiScale = 1f;
    private Eto.Drawing.Size _imageSize = new(48, 48);
    // Held as the Eto bitmap, not the System.Drawing one: ToSD() hands back the *underlying* image
    // rather than a copy, so disposing the Eto wrapper would invalidate anything kept from it.
    private Eto.Drawing.Bitmap? _emptyStateGlyph;
    private float _emptyStateGlyphScale;

    public WinFormsListView(ListViewBehavior<T> behavior)
    {
        _behavior = behavior;
        _view = behavior.ScrollOnDrag ? new DragScrollListView() : new OverlayPaintListView();
        _view.MultiSelect = behavior.MultiSelect;

        if (_behavior.Checkboxes)
        {
            _view.View = View.List;
            _view.CheckBoxes = true;
            _view.ItemChecked += OnSelectedIndexChanged;
        }
        else
        {
            _view.View = View.LargeIcon;
            _view.LargeImageList = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                TransparentColor = Color.Transparent
            };
            WinFormsHacks.SetControlStyle(_view,
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint,
                true);
            _view.SelectedIndexChanged += OnSelectedIndexChanged;
        }

        if (_behavior.UseCanvasBackground)
        {
            _view.BackColor = _behavior.ColorScheme.CanvasColor.ToSD();
        }

        if (_view is OverlayPaintListView overlayView)
        {
            if (_behavior.EmptyState != null)
            {
                overlayView.OverlayPaint += DrawEmptyState;
            }
            // Section headings and the drop indicator are both drawn over the native control: comctl32
            // draws its own group headings in the light Explorer blue whatever the window's theme, and
            // it stops drawing the insertion mark altogether once groups are switched on.
            overlayView.OverlayPaint += DrawSectionHeaders;
            overlayView.OverlayPaint += DrawDropIndicator;
        }

        _view.AllowDrop = _behavior.AllowDragDrop;
        _view.ItemActivate += OnItemActivate;
        _view.ItemDrag += OnItemDrag;
        _view.DragEnter += OnDragEnter;
        _view.DragDrop += OnDragDrop;
        _view.DragOver += OnDragOver;
        _view.DragLeave += OnDragLeave;
        _view.MouseMove += OnMouseMove;
        _view.MouseLeave += OnMouseLeave;

        _viewEtoControl = Eto.Forms.WinFormsHelpers.ToEto(_view);
        ImageList = UseCustomRendering
            ? new WinFormsImageList<T>.Custom(this, _behavior)
            : !_behavior.Checkboxes
                ? new WinFormsImageList<T>.Native(this, _behavior)
                : new WinFormsImageList<T>.Stub(this, _behavior);
        if (UseCustomRendering)
        {
            _view.OwnerDraw = true;
            _view.DrawItem += CustomRenderItem;
        }

        EtoPlatform.Current.AttachDpiDependency(_viewEtoControl, scale =>
        {
            _dpiScale = scale;
            // Reset internal size based on new scale
            ImageSize = ImageSize;
        });
    }

    private bool UseCustomRendering => !_behavior.ShowLabels && !_behavior.Checkboxes;

    /// <summary>
    /// The empty state's glyph, tinted for the accent rather than the window foreground the icon
    /// provider applies. Cached because the empty view repaints on every resize and scroll, and
    /// tinting walks every pixel.
    /// </summary>
    private Image? GetEmptyStateGlyph(string iconName)
    {
        if (_emptyStateGlyph == null || _emptyStateGlyphScale != _dpiScale)
        {
            _emptyStateGlyph?.Dispose();
            _emptyStateGlyph = EtoPlatform.Current.IconProvider.GetIcon(iconName, _dpiScale);
            _emptyStateGlyph?.Tint(_behavior.ColorScheme.AccentColor);
            _emptyStateGlyphScale = _dpiScale;
        }
        return _emptyStateGlyph?.ToSD();
    }

    /// <summary>
    /// Draws the empty state into the list's own background, so it costs no window and therefore
    /// doesn't intercept the file drops that land in the middle of the list. It paints only while
    /// the list is empty, so there is nothing to hide once the first page arrives.
    /// </summary>
    private void DrawEmptyState(object? sender, PaintEventArgs e)
    {
        var info = _behavior.EmptyState;
        if (info == null || _view.Items.Count > 0)
        {
            return;
        }

        var scheme = _behavior.ColorScheme;
        var client = _view.ClientRectangle;
        if (client.Width < 120 * _dpiScale || client.Height < 120 * _dpiScale)
        {
            // Too cramped to say anything useful; an elided heading is worse than nothing.
            return;
        }

        var oldSmoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int disc = (int) Math.Round(104 * _dpiScale);
        int icon = (int) Math.Round(48 * _dpiScale);
        int gap = (int) Math.Round(20 * _dpiScale);

        using var titleFont = new Font(_view.Font.FontFamily, _view.Font.Size * 4 / 3, FontStyle.Bold);
        using var hintFont = new Font(_view.Font.FontFamily, _view.Font.Size);
        var titleSize = e.Graphics.MeasureString(info.Title, titleFont);
        var hintSize = e.Graphics.MeasureString(info.Hint, hintFont);

        int blockHeight = disc + gap + (int) Math.Ceiling(titleSize.Height) + (int) Math.Round(6 * _dpiScale) +
                          (int) Math.Ceiling(hintSize.Height);
        int cx = client.Left + client.Width / 2;
        int top = client.Top + (client.Height - blockHeight) / 2;

        using (var discBrush = new SolidBrush(scheme.AccentSubtleBackgroundColor.ToSD()))
        {
            e.Graphics.FillEllipse(discBrush, cx - disc / 2, top, disc, disc);
        }

        var glyph = GetEmptyStateGlyph(info.IconName);
        if (glyph != null)
        {
            // Explicit size: the DrawImage overload without one scales by the image's embedded dpi.
            e.Graphics.DrawImage(glyph, cx - icon / 2, top + (disc - icon) / 2, icon, icon);
        }

        int y = top + disc + gap;
        using (var titleBrush = new SolidBrush(scheme.ForegroundColor.ToSD()))
        {
            e.Graphics.DrawString(info.Title, titleFont, titleBrush, cx - titleSize.Width / 2, y);
        }
        y += (int) Math.Ceiling(titleSize.Height) + (int) Math.Round(6 * _dpiScale);
        using (var hintBrush = new SolidBrush(scheme.SecondaryTextColor.ToSD()))
        {
            e.Graphics.DrawString(info.Hint, hintFont, hintBrush, cx - hintSize.Width / 2, y);
        }

        e.Graphics.SmoothingMode = oldSmoothing;
    }

    /// <summary>
    /// The accent-tinted backplate behind a selected page. Rounded, because it is a container --
    /// the page image itself keeps square corners, since paper has square corners and rounding it
    /// would clip scanned content at the edges.
    /// </summary>
    private void DrawSelectionBackplate(Graphics g, Rectangle rect)
    {
        var scheme = _behavior.ColorScheme;
        var oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float radius = FluentShapes.CONTROL_CORNER_RADIUS * _dpiScale;
        using (var path = FluentShapes.RoundedRect(rect, radius))
        using (var brush = new SolidBrush(scheme.HighlightBackgroundColor.ToSD()))
        {
            g.FillPath(brush, path);
        }
        using (var path = FluentShapes.RoundedRect(rect, radius))
        using (var pen = new Pen(scheme.HighlightBorderColor.ToSD(), Math.Max(1f, _dpiScale)))
        {
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = oldSmoothing;
    }

    /// <summary>
    /// A soft shadow under a page, so it reads as lying on the canvas rather than being cut out of
    /// it. GDI+ has no blur, so the softness comes from stacking a few outlines that each reach one
    /// pixel further out at a lower alpha.
    /// </summary>
    private void DrawPageShadow(Graphics g, Rectangle page)
    {
        var color = _behavior.ColorScheme.PageShadowColor.ToSD();
        int depth = Math.Max(1, (int) Math.Round(PageShadowDepth * _dpiScale));
        for (int i = 1; i <= depth; i++)
        {
            using var pen = new Pen(Color.FromArgb(Math.Max(1, color.A / i), color));
            g.DrawRectangle(pen, page.X - i, page.Y - i + 1, page.Width + 2 * i - 1, page.Height + 2 * i - 1);
        }
    }

    private void CustomRenderItem(object? sender, DrawListViewItemEventArgs e)
    {
        var image = ImageList.Get(e.Item);
        int imageSizeW = (int) Math.Round(_imageSize.Width * _dpiScale);
        int imageSizeH = (int) Math.Round(_imageSize.Height * _dpiScale);
        if (_behavior.ShowPageNumbers)
        {
            int tp = (int) Math.Round(PageNumberTextPadding * _dpiScale);
            int sp = (int) Math.Round(PageNumberSelectionPadding * _dpiScale);

            // When page numbers are shown, we use a completely different drawing path, as we need to offset the image
            // to have room for the page numbers, and the selection rectangle has a completely different style to
            // encompass the page numbers too.
            // Numbered within its own document where there are sections: which page of this document
            // it is answers the question the operator has, where its place in the whole batch does not.
            var section = _sections.FirstOrDefault(x => x.Contains(e.ItemIndex));
            string label = section != null
                ? $"{e.ItemIndex - section.StartIndex + 1} / {section.Count}"
                : $"{e.ItemIndex + 1} / {_view.Items.Count}";
            SizeF textSize = TextRenderer.MeasureText(label, _view.Font);
            int textOffset = (int) (textSize.Height + tp);

            float scaleHeight = (float) (imageSizeH - textOffset) / image.Height;
            float scaleWidth = (float) imageSizeW / image.Width;

            float scale = Math.Min(scaleWidth, scaleHeight);
            int height = (int) Math.Round(image.Height * scale);
            int width = (int) Math.Round(image.Width * scale);

            var x = e.Bounds.Left + (e.Bounds.Width - width) / 2;
            var y = e.Bounds.Top + (e.Bounds.Height - height - textOffset) / 2;

            // Draw selection rectangle/background
            if (e.Item.Selected)
            {
                Size intTextSize = Size.Ceiling(textSize);

                int selectionWidth = Math.Max(width, intTextSize.Width);
                int selectionHeight = height + tp + intTextSize.Height;

                var selectionX = e.Bounds.Left + (e.Bounds.Width - width) / 2;

                var selectionRect = new Rectangle(selectionX, y, selectionWidth, selectionHeight);
                selectionRect.Inflate(sp, sp);
                DrawSelectionBackplate(e.Graphics, selectionRect);
            }

            var pageRect = new Rectangle(x, y, width, height);
            DrawPageShadow(e.Graphics, pageRect);
            e.Graphics.DrawImage(image, pageRect);
            // The page keeps its hairline whether or not it is selected: it is the edge of the sheet,
            // not a selection cue.
            using (var borderPen = DefaultPen)
            {
                e.Graphics.DrawRectangle(borderPen, x, y, width - 1, height - 1);
            }

            // Draw the text below the image
            using var drawBrush = new SolidBrush(_behavior.ColorScheme.ForegroundColor.ToSD());
            float x1 = x + width / 2f;
            float y1 = y + height + tp;
            RectangleF labelRect = new(x1, y1, 0, textSize.Height);
            float maxLabelWidth = Math.Min(textSize.Width, e.Bounds.Width - 2 * tp);
            labelRect.Inflate(maxLabelWidth / 2, 0);
            labelRect.Width += 2;
            e.Graphics.DrawString(label, _view.Font, drawBrush, labelRect, PageNumberLabelFormat);
        }
        else
        {
            // The basic no-page-numbers drawing path
            int width, height;
            if (image.Width > image.Height)
            {
                width = imageSizeW;
                height = (int) Math.Round(width * (image.Height / (double) image.Width));
            }
            else
            {
                height = imageSizeH;
                width = (int) Math.Round(height * (image.Width / (double) image.Height));
            }
            var x = e.Bounds.Left + (e.Bounds.Width - width) / 2;
            var y = e.Bounds.Top + (e.Bounds.Height - height) / 2;

            var pageRect = new Rectangle(x, y, width, height);
            if (e.Item.Selected)
            {
                var backplate = pageRect;
                backplate.Inflate((int) Math.Round(PageNumberSelectionPadding * _dpiScale),
                    (int) Math.Round(PageNumberSelectionPadding * _dpiScale));
                DrawSelectionBackplate(e.Graphics, backplate);
            }
            DrawPageShadow(e.Graphics, pageRect);
            e.Graphics.DrawImage(image, pageRect);
            using var borderPen = DefaultPen;
            e.Graphics.DrawRectangle(borderPen, x, y, width - 1, height - 1);
        }
    }

    public Eto.Drawing.Size ImageSize
    {
        get => _imageSize;
        set
        {
            _imageSize = value;
            if (_view.LargeImageList != null)
            {
                int w = (int) Math.Round(_imageSize.Width * _dpiScale);
                int h = (int) Math.Round(_imageSize.Height * _dpiScale);
                WinFormsHacks.SetImageSize(_view.LargeImageList!, new Size(w, h));
            }
        }
    }

    // ---------- sections ----------

    /// <summary>The height of a group's heading band, remembered so it can be drawn while scrolled.</summary>
    private int _headerBandHeight;

    public void SetSections(IReadOnlyList<ListViewSection> sections)
    {
        if (_sections.SequenceEqual(sections))
        {
            return;
        }
        _sections = sections.ToList();
        _view.BeginUpdate();
        try
        {
            _view.Groups.Clear();
            _view.ShowGroups = _sections.Count > 0;
            foreach (var section in _sections)
            {
                // The heading text is left blank on purpose: comctl32 draws its own in the light
                // Explorer blue whatever the window's theme is, and no theming call changes that. It
                // still reserves the band, which is what DrawSectionHeaders paints into.
                var group = new ListViewGroup(" ") { HeaderAlignment = HorizontalAlignment.Left };
                _view.Groups.Add(group);
                for (int i = section.StartIndex; i <= section.EndIndex && i < Items.Count; i++)
                {
                    Items[i].Group = group;
                }
            }
        }
        finally
        {
            _view.EndUpdate();
        }
        _view.Invalidate();
    }

    /// <summary>
    /// The band a section's heading goes in, in the same coordinates as the item bounds, or null when it
    /// is not on screen.
    /// </summary>
    private Rectangle? HeaderBandFor(int sectionIndex)
    {
        var band = NativeHeaderBand(sectionIndex);
        if (band == null)
        {
            // Derived instead: comctl32 reserves the band directly above the section's first item.
            var section = _sections[sectionIndex];
            if (section.StartIndex >= Items.Count || _headerBandHeight <= 0)
            {
                return null;
            }
            var top = Items[section.StartIndex].Bounds.Top - _headerBandHeight;
            band = new Rectangle(0, top, _view.ClientRectangle.Width, _headerBandHeight);
        }
        else
        {
            _headerBandHeight = band.Value.Height;
        }
        return band.Value.Bottom < 0 || band.Value.Top > _view.ClientRectangle.Height ? null : band;
    }

    /// <summary>
    /// Asks the control where the heading is, which only the native list view knows.
    /// </summary>
    private Rectangle? NativeHeaderBand(int sectionIndex) =>
        ListViewGroups.HeaderBounds(_view, sectionIndex);

    /// <summary>
    /// The heading of each section: a status dot, the document's name, and a quieter line saying how
    /// many pages it has and where it stands.
    /// </summary>
    private void DrawSectionHeaders(object? sender, PaintEventArgs e)
    {
        if (_sections.Count == 0 || Items.Count == 0)
        {
            return;
        }
        var scheme = _behavior.ColorScheme;
        var oldSmoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var titleFont = new Font(_view.Font, FontStyle.Bold);
        int pad = (int) Math.Round(6 * _dpiScale);
        int dotSize = (int) Math.Round(8 * _dpiScale);
        for (int i = 0; i < _sections.Count; i++)
        {
            var band = HeaderBandFor(i);
            if (band == null)
            {
                continue;
            }
            var section = _sections[i];
            var rect = band.Value;
            using (var background = new SolidBrush(_behavior.ColorScheme.CanvasColor.ToSD()))
            {
                e.Graphics.FillRectangle(background, rect);
            }
            var dot = new Rectangle(rect.Left + pad, rect.Top + (rect.Height - dotSize) / 2, dotSize, dotSize);
            using (var brush = new SolidBrush(section.Color.ToSD()))
            {
                e.Graphics.FillEllipse(brush, dot);
            }
            var titleSize = TextRenderer.MeasureText(section.Title, titleFont);
            int x = dot.Right + pad;
            int y = rect.Top + (rect.Height - titleSize.Height) / 2;
            TextRenderer.DrawText(e.Graphics, section.Title, titleFont, new Point(x, y),
                scheme.ForegroundColor.ToSD());
            TextRenderer.DrawText(e.Graphics, section.Meta, _view.Font,
                new Point(x + titleSize.Width + pad * 2, y), scheme.SecondaryTextColor.ToSD());
            using var rule = new Pen(Color.FromArgb(60, scheme.ForegroundColor.ToSD()));
            e.Graphics.DrawLine(rule, rect.Left + pad, rect.Bottom - 1, rect.Right - pad, rect.Bottom - 1);
        }
        e.Graphics.SmoothingMode = oldSmoothing;
    }

    /// <summary>
    /// Where a drop would put the pages. Drawn here rather than with the control's own insertion mark,
    /// which stops appearing as soon as the items are grouped.
    /// </summary>
    private void DrawDropIndicator(object? sender, PaintEventArgs e)
    {
        if (_dropIndex < 0 || _dropIndex >= Items.Count)
        {
            return;
        }
        var bounds = Items[_dropIndex].Bounds;
        int width = Math.Max(2, (int) Math.Round(3 * _dpiScale));
        int inset = (int) Math.Round(8 * _dpiScale);
        var bar = new Rectangle(
            _dropAfterItem ? bounds.Right - width : bounds.Left,
            bounds.Top + inset, width, Math.Max(1, bounds.Height - 2 * inset));
        var oldSmoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = FluentShapes.RoundedRect(bar, width / 2f))
        using (var brush = new SolidBrush(_behavior.ColorScheme.AccentColor.ToSD()))
        {
            e.Graphics.FillPath(brush, path);
        }
        e.Graphics.SmoothingMode = oldSmoothing;
    }

    private void SetDropIndicator(int index, bool afterItem)
    {
        if (_dropIndex == index && _dropAfterItem == afterItem)
        {
            return;
        }
        _dropIndex = index;
        _dropAfterItem = afterItem;
        _view.Invalidate();
    }

    /// <summary>The section the given y coordinate falls in, heading band included.</summary>
    private ListViewSection? SectionAt(int y)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            var section = _sections[i];
            if (section.StartIndex >= Items.Count)
            {
                continue;
            }
            var top = HeaderBandFor(i)?.Top ?? Items[section.StartIndex].Bounds.Top;
            var last = Math.Min(section.EndIndex, Items.Count - 1);
            if (y >= top && y <= Items[last].Bounds.Bottom)
            {
                return section;
            }
        }
        return null;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var data = e.Data.ToEto();
        // TODO: Figure out why .Contains is not working correctly on net9
        try
        {
            if (data.Contains(_behavior.CustomDragDataType) && _behavior.AllowDragDrop)
            {
                e.Effect = _behavior.GetCustomDragEffect(data.GetData(_behavior.CustomDragDataType)).ToSwf();
                return;
            }
        }
        catch (COMException)
        {
        }
        if (data.Contains("FileDrop") && _behavior.AllowFileDrop)
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    public Eto.Forms.Control Control => _viewEtoControl;

    public ContextMenu? ContextMenu
    {
        get => _contextMenu;
        set
        {
            _contextMenu = value;
            _view.ContextMenuStrip = (_contextMenu?.Handler as ContextMenuHandler)?.Control;
        }
    }

    public ListView NativeControl => _view;

    public event EventHandler? SelectionChanged;

    public event EventHandler? ItemClicked;

    public event EventHandler<DropEventArgs>? Drop;

    private ListView.ListViewItemCollection Items => _view.Items;

    private WinFormsImageList<T> ImageList { get; }

    public void SetItems(IEnumerable<T> items)
    {
        if (_refreshing)
        {
            throw new InvalidOperationException();
        }
        _refreshing = true;
        Items.Clear();
        ImageList.Clear();
        foreach (var item in items)
        {
            var listViewItem = Items.Add(GetLabel(item));
            listViewItem.Tag = item;
            ImageList.Append(item, listViewItem);
        }
        SetSelectedItems();
        _refreshing = false;
    }

    private void SetSelectedItems()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (_behavior.Checkboxes)
            {
                Items[i].Checked = Selection.Contains((T) Items[i].Tag!);
            }
            else
            {
                Items[i].Selected = Selection.Contains((T) Items[i].Tag!);
            }
        }
    }

    public void RegenerateImages()
    {
        if (_refreshing || Items.Count == 0)
        {
            return;
        }
        if (!UseCustomRendering)
        {
            // TODO: Not sure why but this gets glitchy unless we reset the items too
            Invoker.Current.InvokeDispatch(() =>
                SetItems(_view.Items.Cast<ListViewItem>().Select(x => (T) x.Tag!).ToList()));
            return;
        }
        _refreshing = true;
        _view.BeginUpdate();
        ImageList.Clear();

        var images = new List<Image>();
        foreach (ListViewItem listViewItem in Items)
        {
            var item = (T) listViewItem.Tag!;
            images.Add(ImageList.PartialAppend(item));
        }
        ImageList.FinishPartialAppends(images);

        _view.EndUpdate();
        _refreshing = false;
    }

    public void ApplyDiffs(ListViewDiffs<T> diffs)
    {
        if (_refreshing)
        {
            throw new InvalidOperationException();
        }
        _refreshing = true;
        _view.BeginUpdate();

        // TODO: We might want to make the differ even smarter. e.g. maybe it can generate an arbitrary order of operations that minimizes update cost
        // example: clear then append 1 instead of delete all but 1
        var originalItemsList = Items.OfType<ListViewItem>().Select(x => (T) x.Tag!).ToList();
        var originalItemsSet = new HashSet<T>(originalItemsList);
        if (!diffs.AppendOperations.Any() && !diffs.ReplaceOperations.Any() &&
            diffs.TrimOperations.Any(x => x.Count == Items.Count))
        {
            ImageList.Clear();
            Items.Clear();
        }
        else
        {
            foreach (var append in diffs.AppendOperations)
            {
                // TODO: We want to use the thumbnail bitmap from the ImageRenderState, though we need to consider lifetime/disposal
                // TODO: Use AddRange instead?
                // TODO: Add this to the new ImageListViewBehavior
                //  _thumbnailProvider.GetThumbnail(append.Image.Source, ThumbnailSize)
                var listViewItem = Items.Add(GetLabel(append.Item));
                listViewItem.Tag = append.Item;
                ImageList.Append(append.Item, listViewItem);
            }
            foreach (var replace in diffs.ReplaceOperations)
            {
                // TODO: This seems to have some race condition (errors when changing languages while thumbnails render)
                Items[replace.Index].Tag = replace.Item;
                ImageList.Replace(replace.Item, replace.Index);
            }
            foreach (var trim in diffs.TrimOperations)
            {
                for (int i = 0; i < trim.Count; i++)
                {
                    Items.RemoveAt(Items.Count - 1);
                    ImageList.DeleteFromEnd();
                }
            }
        }
        SetSelectedItems();
        var newItemsList = Items.OfType<ListViewItem>().Select(x => (T) x.Tag!).ToList();
        var newItemsSet = new HashSet<T>(newItemsList);
        if (originalItemsSet.SetEquals(newItemsSet) && !originalItemsList.SequenceEqual(newItemsList))
        {
            ScrollToSelection();
        }
        _view.EndUpdate();
        _view.Invalidate();
        _refreshing = false;
    }

    private void ScrollToSelection()
    {
        // If selection is empty (e.g. after interleave), this scrolls to top
        _view.EnsureVisible(_view.SelectedIndices.OfType<int>().LastOrDefault());
        _view.EnsureVisible(_view.SelectedIndices.OfType<int>().FirstOrDefault());
    }

    public ListSelection<T> Selection
    {
        get => _selection;
        set
        {
            if (_selection == value)
            {
                return;
            }
            _selection = value ?? throw new ArgumentNullException(nameof(value));
            if (!_refreshing)
            {
                _refreshing = true;
                SetSelectedItems();
                _refreshing = false;
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string GetLabel(T item) => _behavior.ShowLabels ? _behavior.GetLabel(item) : "";

    private void OnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!_refreshing)
        {
            _refreshing = true;
            var items = _behavior.Checkboxes
                ? _view.CheckedItems.Cast<ListViewItem>()
                : _view.SelectedItems.Cast<ListViewItem>();
            Selection = ListSelection.From(items.Select(x => (T) x.Tag!));
            _refreshing = false;
        }
    }

    private void OnItemActivate(object? sender, EventArgs e)
    {
        ItemClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (!_behavior.AllowDragDrop)
        {
            return;
        }
        // Provide drag data
        if (Selection.Count > 0)
        {
            var dataObject = new DataObject();
            dataObject.SetData(_behavior.CustomDragDataType, _behavior.SerializeCustomDragData(Selection.ToArray()));
            _view.DoDragDrop(dataObject, DragDropEffects.Move | DragDropEffects.Copy);
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var index = GetDragIndex(e);
        SetDropIndicator(-1, false);
        if (index != -1)
        {
            var data = e.Data.ToEto();
            // TODO: Figure out why .Contains is not working correctly on net9
            try
            {
                if (data.Contains(_behavior.CustomDragDataType))
                {
                    Drop?.Invoke(this, new DropEventArgs(index, data.GetData(_behavior.CustomDragDataType)));
                }
            }
            catch (COMException)
            {
            }
            try
            {
                if (data.Contains("FileDrop"))
                {
                    var filePaths = (string[]) e.Data!.GetData(DataFormats.FileDrop)!;
                    Drop?.Invoke(this, new DropEventArgs(index, filePaths));
                }
            }
            catch (COMException)
            {
            }
        }
    }

    private void OnDragLeave(object? sender, EventArgs e)
    {
        SetDropIndicator(-1, false);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Effect == DragDropEffects.Move && Items.Count > 0)
        {
            var index = GetDragIndex(e);
            if (index == Items.Count)
            {
                SetDropIndicator(index - 1, true);
            }
            else
            {
                SetDropIndicator(index, false);
            }
        }
    }

    private int GetDragIndex(DragEventArgs e)
    {
        if (Items.Count == 0)
        {
            return 0;
        }
        Point cp = _view.PointToClient(new Point(e.X, e.Y));
        ListViewItem? dragToItem = _view.GetItemAt(cp.X, cp.Y);
        if (dragToItem == null)
        {
            var items = Items.Cast<ListViewItem>().ToList();
            var minY = items.Select(x => x.Bounds.Top).Min();
            var maxY = items.Select(x => x.Bounds.Bottom).Max();
            if (cp.Y < minY)
            {
                cp.Y = minY;
            }
            if (cp.Y > maxY)
            {
                cp.Y = maxY;
            }
            var row = items.Where(x => x.Bounds.Top <= cp.Y && x.Bounds.Bottom >= cp.Y).OrderBy(x => x.Bounds.X)
                .ToList();
            dragToItem = row.FirstOrDefault(x => x.Bounds.Right >= cp.X) ?? row.LastOrDefault();
        }
        if (dragToItem == null)
        {
            // With sections there are bands between the rows that hold no item -- the headings. A drop
            // on one of those means the section it belongs to, not nowhere.
            return SectionAt(cp.Y)?.StartIndex ?? -1;
        }
        int dragToIndex = dragToItem.Index;
        if (cp.X > (dragToItem.Bounds.X + dragToItem.Bounds.Width / 2))
        {
            dragToIndex++;
        }
        return dragToIndex;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_behavior.UseHandCursor)
        {
            _view.Cursor = _view.GetItemAt(e.X, e.Y) == null ? Cursors.Default : Cursors.Hand;
        }
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (_behavior.UseHandCursor)
        {
            _view.Cursor = Cursors.Default;
        }
    }
}