using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms.Layout;

public class LayoutScrollable : LayoutContainer
{
    private readonly Scrollable _scrollable = new() { Border = BorderType.None };
    private readonly Control _contentContainer = EtoPlatform.Current.CreateContainer();
    private readonly LayoutControl _scrollableControl;
    private readonly LayoutElement _content;

    public LayoutScrollable(LayoutElement content) : base([])
    {
        _content = content;
        Scale = true;
        _scrollable.Content = _contentContainer;
        _scrollableControl = new LayoutControl(_scrollable, scale: true);
        Children.Add(_scrollableControl);
        Children.Add(_content);
    }

    public override void Materialize(LayoutContext context)
    {
        _scrollableControl.Materialize(context);
        _content.Materialize(GetContentContext(context));
    }

    public override void DoLayout(LayoutContext context, RectangleF bounds)
    {
        _scrollableControl.DoLayout(context, bounds);

        var contentContext = GetContentContext(context);
        var contentWidth = Math.Max(0, bounds.Width - 20 * context.Scale);
        var preferred = _content.GetPreferredSize(
            contentContext,
            new RectangleF(0, 0, contentWidth, LayoutController.MAX_SIZE));
        var contentHeight = Math.Max(preferred.Height, bounds.Height);
        var contentBounds = new RectangleF(0, 0, contentWidth, contentHeight);

        _content.DoLayout(contentContext, contentBounds);
        EtoPlatform.Current.SetContainerSize(
            context.Window!,
            _contentContainer,
            Size.Ceiling(new SizeF(contentWidth, contentHeight)),
            0);
    }

    protected override SizeF GetPreferredSizeCore(LayoutContext context, RectangleF parentBounds)
    {
        var contentContext = GetContentContext(context);
        var preferred = _content.GetPreferredSize(contentContext, parentBounds);
        return new SizeF(preferred.Width, Math.Min(preferred.Height, 650 * context.Scale));
    }

    private LayoutContext GetContentContext(LayoutContext context)
    {
        return context with
        {
            Container = _contentContainer,
            Depth = context.Depth + 1
        };
    }
}
