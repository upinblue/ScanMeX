using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms.Widgets;

/// <summary>
/// The Windows 11 progress bar: a hairline track carrying a rounded, accent-coloured indicator, and the
/// two-segment sweep WinUI uses when there is no percentage to report.
/// </summary>
/// <remarks>
/// Drawn rather than themed because the native control cannot be made to look like this. The WinForms
/// ProgressBar renders the Windows 7-era comctl32 bar -- a full-height green or blue block with a
/// gradient and a gloss highlight -- which no longer matches anything else in the app now that the rest
/// of the UI is Fluent. It also animates its own way: it eases towards a new value over a few hundred
/// milliseconds, so a bar that has been told it is at 100% keeps creeping for a moment after the work
/// finished, and a bar set backwards snaps instead. That animation is the reason the shared render path
/// carried a "value += 1; value -= 1" nudge to force it to catch up.
///
/// The geometry is WinUI 3's own: <c>ProgressBarTrackHeight</c> 1, <c>ProgressBarMinHeight</c> 3, with
/// matching corner radii, in device-independent pixels and scaled to the surface. The colours come from
/// <see cref="ColorScheme"/>, so this follows the user's Windows accent and the light/dark theme like
/// everything else.
/// </remarks>
public class FluentProgressBar : Drawable
{
    /// <summary>WinUI's ProgressBarTrackHeight, at 96 dpi.</summary>
    private const float TRACK_HEIGHT = 1f;

    /// <summary>WinUI's ProgressBarMinHeight -- the height of the indicator itself, at 96 dpi.</summary>
    private const float INDICATOR_HEIGHT = 3f;

    /// <summary>
    /// The height the layout should reserve. The bar itself is 3px; the rest is the breathing room a
    /// hairline needs to read as a control rather than as a stray rule.
    /// </summary>
    public const int NATURAL_HEIGHT = 12;

    private const double FRAME_INTERVAL_SECONDS = 1 / 60.0;

    /// <summary>How long the indeterminate sweep takes to cross the track once.</summary>
    private const float SWEEP_SECONDS = 1.6f;

    /// <summary>
    /// How long the indicator takes to travel the whole track when the value jumps. Determinate progress
    /// is eased for the same reason WinUI eases it: an upload that reports in whole percent otherwise
    /// advances in visible steps.
    /// </summary>
    private const float SLIDE_SECONDS = 0.25f;

    private readonly UITimer _timer = new() { Interval = FRAME_INTERVAL_SECONDS };

    private int _value;
    private int _maxValue = 100;
    private bool _indeterminate;
    private float _scale = 1f;

    /// <summary>Where the indicator is drawn, 0..1. Chases <see cref="Fraction"/> rather than jumping.</summary>
    private float _shownFraction;

    /// <summary>Position of the indeterminate sweep, 0..1, wrapping.</summary>
    private float _sweep;

    private DateTime _lastFrame = DateTime.UtcNow;
    private Color? _surface;

    public FluentProgressBar()
    {
        Paint += OnPaint;
        _timer.Elapsed += OnFrame;
        EtoPlatform.Current.AttachDpiDependency(this, scale =>
        {
            _scale = scale;
            Invalidate();
        });
    }

    /// <summary>
    /// The colour behind the bar. Controls paint their own background, so a bar on a tinted surface -- a
    /// notification card -- has to be told what it is sitting on or it draws as a pale box across it.
    /// Left unset it follows the window surface.
    /// </summary>
    /// <remarks>
    /// Deliberately not read in the constructor: <see cref="ColorScheme"/> needs the config, which the
    /// container attaches in a build callback and only when there is an Eto platform, so touching it
    /// while a form's fields are being initialized would make construction order load-bearing.
    /// </remarks>
    public Color SurfaceColor
    {
        set
        {
            _surface = value;
            Invalidate();
        }
    }

    /// <summary>
    /// The value the bar is at. Clamped rather than validated: the native control throws when the value
    /// is outside the range, and an operation that reports 101% of an estimate is not a reason to take
    /// down the window it is reporting into.
    /// </summary>
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(_maxValue, 0));
            if (clamped == _value)
            {
                return;
            }
            _value = clamped;
            OnStateChanged();
        }
    }

    public int MaxValue
    {
        get => _maxValue;
        set
        {
            var max = Math.Max(value, 0);
            if (max == _maxValue)
            {
                return;
            }
            _maxValue = max;
            _value = Math.Clamp(_value, 0, max);
            OnStateChanged();
        }
    }

    /// <summary>
    /// Whether there is a percentage to show at all. An indeterminate bar sweeps; it does not sit at
    /// zero, which is what "working, but I can't tell you how far" used to look like.
    /// </summary>
    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value)
            {
                return;
            }
            _indeterminate = value;
            OnStateChanged();
        }
    }

    private float Fraction => _maxValue <= 0 ? 0f : Math.Clamp(_value / (float) _maxValue, 0f, 1f);

    private void OnStateChanged()
    {
        UpdateTimer();
        Invalidate();
    }

    /// <summary>
    /// The timer runs only while something is actually moving: the sweep, or an indicator still catching
    /// up with its value. A repaint every frame for a bar that has settled is work nobody can see.
    /// </summary>
    private void UpdateTimer()
    {
        var needed = _indeterminate || Math.Abs(_shownFraction - Fraction) > 0.0005f;
        if (needed == _timer.Started)
        {
            return;
        }
        if (needed)
        {
            _lastFrame = DateTime.UtcNow;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        // Measured rather than assumed to be the interval: a UI thread busy with a scan delivers ticks
        // late, and an animation stepped by tick count then runs slow exactly when the app is busiest.
        var elapsed = (float) (now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        // A tick that arrives after the thread was blocked would otherwise jump the sweep across the bar.
        elapsed = Math.Clamp(elapsed, 0f, 0.1f);

        if (_indeterminate)
        {
            _sweep = (_sweep + elapsed / SWEEP_SECONDS) % 1f;
        }

        var target = Fraction;
        var delta = target - _shownFraction;
        var step = elapsed / SLIDE_SECONDS;
        _shownFraction = Math.Abs(delta) <= step ? target : _shownFraction + Math.Sign(delta) * step;

        UpdateTimer();
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var width = Width;
        var height = Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var g = e.Graphics;
        var colorScheme = EtoPlatform.Current.ColorScheme;
        g.FillRectangle(_surface ?? colorScheme.BackgroundColor, e.ClipRectangle);
        var trackHeight = Math.Max(1f, TRACK_HEIGHT * _scale);
        var indicatorHeight = Math.Max(2f, INDICATOR_HEIGHT * _scale);
        var centre = height / 2f;

        g.AntiAlias = true;
        // ControlStrongFillColorDefault: the track is a hairline the indicator runs along, not a second
        // filled bar competing with it.
        var trackColor = Color.Blend(colorScheme.BackgroundColor, colorScheme.ForegroundColor, 0.45f);
        FillPill(g, trackColor, 0, centre - trackHeight / 2f, width, trackHeight);

        var accent = colorScheme.AccentColor;
        if (_indeterminate)
        {
            // WinUI runs two segments at different phases, which is what stops a slow sweep from reading
            // as a stalled one.
            DrawSweepSegment(g, accent, _sweep, 0.35f, indicatorHeight, centre, width);
            DrawSweepSegment(g, accent, (_sweep + 0.55f) % 1f, 0.12f, indicatorHeight, centre, width);
            return;
        }

        var indicatorWidth = width * _shownFraction;
        if (indicatorWidth > 0)
        {
            FillPill(g, accent, 0, centre - indicatorHeight / 2f, indicatorWidth, indicatorHeight);
        }
    }

    private static void DrawSweepSegment(Graphics g, Color color, float phase, float widthFraction,
        float height, float centre, float trackWidth)
    {
        var segmentWidth = trackWidth * widthFraction;
        // Eased so the segment accelerates in and decelerates out, rather than tracking at a constant
        // speed and stopping dead at the edge.
        var eased = phase * phase * (3f - 2f * phase);
        var x = -segmentWidth + (trackWidth + segmentWidth) * eased;
        FillPill(g, color, x, centre - height / 2f, segmentWidth, height);
    }

    /// <summary>
    /// A rounded-end bar. Degrades to a rectangle when it is too short to round, so a bar at 1% doesn't
    /// draw as a circle wider than the progress it represents.
    /// </summary>
    private static void FillPill(Graphics g, Color color, float x, float y, float width, float height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        var radius = Math.Min(height / 2f, width / 2f);
        if (radius < 0.5f)
        {
            g.FillRectangle(color, x, y, width, height);
            return;
        }
        var path = GraphicsPath.GetRoundRect(new RectangleF(x, y, width, height), radius);
        g.FillPath(color, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
