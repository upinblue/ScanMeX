using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms.Layout;

/// <summary>
/// The mirror of <see cref="LayoutLeftPanel"/>: a resizable panel on the right of the main content,
/// whose width is remembered.
/// </summary>
/// <remarks>
/// A separate class rather than a flag on the left panel because the two differ in more than which side
/// they sit on. The splitter's fixed panel is the other one, the remembered width is measured from the
/// other end, and the minimum-size assignments swap -- folding that into one class means four
/// conditionals in the middle of layout code that is already hard to follow.
/// </remarks>
public class LayoutRightPanel : LayoutContainer
{
    private readonly LayoutElement _left;
    private readonly LayoutElement _right;
    private readonly LayoutOverlay _overlay;

    private Func<int> _widthGetter = () => 0;
    private Action<int> _widthSetter = _ => { };
    private int? _minWidth;
    private bool _isInitialized;
    private bool _inLayout;
    private float _lastScale;

    public LayoutRightPanel(LayoutElement left, LayoutElement right) : base([left, right])
    {
        _left = left;
        _right = right;
        Splitter = new Splitter
        {
            Orientation = Orientation.Horizontal,
            Panel1 = new Panel(),
            Panel2 = new Panel(),
            FixedPanel = SplitterFixedPanel.Panel2
        };
        _overlay = L.Overlay(Splitter, L.Row(left, right).Spacing(EtoPlatform.Current.IsWinForms ? 3 : 2));
        // Unlike the left panel, this one is nested rather than the root of a window, so without asking
        // for the space it gets its natural width and the whole pair sits in a column at one edge.
        Scale = true;
    }

    public Splitter Splitter { get; }

    public override void Materialize(LayoutContext context) => _overlay.Materialize(context);

    public override void DoLayout(LayoutContext context, RectangleF bounds)
    {
        var w = _minWidth.HasValue ? (int) (_minWidth * context.Scale) : MeasureWidth(context, bounds, _right);
        Splitter.Panel1MinimumSize = (int) (100 * context.Scale);
        Splitter.Panel2MinimumSize = w;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (!_isInitialized || context.Scale != _lastScale)
        {
            _lastScale = context.Scale;
            int initialWidth = Math.Max((int) (_widthGetter() * context.Scale), w);
            _inLayout = true;
            // The splitter position is measured from the left, so the remembered width of a right-hand
            // panel is the distance from the other edge.
            EtoPlatform.Current.SetSplitterPosition(Splitter,
                Math.Max((int) bounds.Width - initialWidth, Splitter.Panel1MinimumSize));
            _inLayout = false;
            _right.Width = initialWidth;
            if (!_isInitialized)
            {
                Splitter.PositionChanged += (_, _) =>
                {
                    if (_inLayout)
                    {
                        return;
                    }
                    var width = Splitter.Width - Splitter.Position;
                    if (width > 0 && _right.Width != width)
                    {
                        _right.Width = width;
                        _widthSetter((int) (width / context.Scale));
                        context.Invalidate();
                    }
                };
                _isInitialized = true;
            }
        }

        _overlay.DoLayout(context, bounds);
    }

    private int MeasureWidth(LayoutContext context, RectangleF bounds, LayoutElement element)
    {
        var w = element.Width;
        element.Width = null;
        var measureContext = context with
        {
            IsLayout = false,
            UseCache = false
        };
        int result = (int) element.GetPreferredSize(measureContext, bounds).Width;
        element.Width = w;
        return result;
    }

    protected override SizeF GetPreferredSizeCore(LayoutContext context, RectangleF parentBounds)
    {
        return _overlay.GetPreferredSize(context, parentBounds);
    }

    public LayoutRightPanel SizeConfig(Func<int> getter, Action<int> setter, int? minWidth = null)
    {
        _widthGetter = getter;
        _widthSetter = setter;
        _minWidth = minWidth;
        return this;
    }
}
