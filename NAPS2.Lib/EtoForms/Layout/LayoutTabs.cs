using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms.Layout;

/// <summary>
/// A tab strip whose pages are laid out by the layout system.
/// </summary>
/// <remarks>
/// Follows <see cref="LayoutScrollable"/>: each page owns a platform container, and the child element is
/// laid out into that container rather than into the window's. Every page is laid out, not only the
/// selected one -- switching tabs does not go through the layout system, so a page sized only when it
/// became visible would come up empty the first time it is opened.
/// </remarks>
public class LayoutTabs : LayoutContainer
{
    /// <summary>
    /// Vertical space the tab strip takes off the top of a page, when the platform hasn't reported the
    /// page's own size yet. Only used for the first layout pass.
    /// </summary>
    private const int TAB_STRIP_ALLOWANCE = 40;

    private readonly TabControl _tabControl = new();
    private readonly List<(TabPage Page, Control Container, LayoutElement Content)> _tabs = [];
    private readonly LayoutControl _tabsControl;

    public LayoutTabs(params (string Title, LayoutElement Content)[] tabs) : base([])
    {
        Scale = true;
        foreach (var (title, content) in tabs)
        {
            var container = EtoPlatform.Current.CreateContainer();
            var page = new TabPage { Text = title, Content = container };
            _tabControl.Pages.Add(page);
            _tabs.Add((page, container, content));
        }
        _tabsControl = new LayoutControl(_tabControl, scale: true);
        Children.Add(_tabsControl);
        foreach (var tab in _tabs)
        {
            Children.Add(tab.Content);
        }
    }

    public TabControl TabControl => _tabControl;

    public override void Materialize(LayoutContext context)
    {
        _tabsControl.Materialize(context);
        foreach (var tab in _tabs)
        {
            tab.Content.Materialize(GetPageContext(context, tab.Container));
        }
    }

    public override void DoLayout(LayoutContext context, RectangleF bounds)
    {
        _tabsControl.DoLayout(context, bounds);

        foreach (var tab in _tabs)
        {
            var pageContext = GetPageContext(context, tab.Container);
            // The page reports its own client size once the platform has realized it; before that, the
            // parent bounds less the strip is the best guess available.
            var size = tab.Page.ClientSize;
            var width = size.Width > 0 ? size.Width : (int) bounds.Width;
            var height = size.Height > 0
                ? size.Height
                : (int) (bounds.Height - TAB_STRIP_ALLOWANCE * context.Scale);
            var pageBounds = new RectangleF(0, 0, Math.Max(0, width), Math.Max(0, height));

            tab.Content.DoLayout(pageContext, pageBounds);
            EtoPlatform.Current.SetContainerSize(
                context.Window!, tab.Container, Size.Ceiling(pageBounds.Size), 0);
        }
    }

    protected override SizeF GetPreferredSizeCore(LayoutContext context, RectangleF parentBounds)
    {
        // The strip is as wide as the widest page needs and as tall as the tallest, because switching
        // tabs must not resize the window.
        var width = 0f;
        var height = 0f;
        foreach (var tab in _tabs)
        {
            var preferred = tab.Content.GetPreferredSize(GetPageContext(context, tab.Container), parentBounds);
            width = Math.Max(width, preferred.Width);
            height = Math.Max(height, preferred.Height);
        }
        return new SizeF(width, height + TAB_STRIP_ALLOWANCE * context.Scale);
    }

    private static LayoutContext GetPageContext(LayoutContext context, Control container)
    {
        return context with
        {
            Container = container,
            Depth = context.Depth + 1
        };
    }
}
