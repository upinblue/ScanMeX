using Eto.Forms;
using NAPS2.PostScan;

namespace NAPS2.EtoForms.Desktop;

/// <summary>
/// The three Leonardo motions the document list and the canvas headings play: <b>Arrive</b> (a new
/// document gets a short accent bar for <c>motion.arrive.hold</c>, which then fades), the brief green of
/// a <b>confirmation</b> (a document that has just been filed shows its check mark in the success colour,
/// which then turns neutral), and <b>Leave</b> (a removed document's row fades out instead of vanishing).
/// </summary>
/// <remarks>
/// <para>
/// One instance per window, created with the document panel. It compares each queue state with the
/// last one it saw, so what is already in the queue when the window opens is the baseline, not news:
/// changing the language rebuilds the window, and every document lighting up as "new" afterwards would
/// be the decorative motion at launch that Leonardo rules out.
/// </para>
/// <para>
/// Nothing here draws. The panel and the canvas ask for the current values while they paint, and
/// <see cref="Frame"/> tells them when those values have moved. The timer only runs while something is
/// fading -- during the two-second hold nothing changes, so nothing is repainted.
/// </para>
/// <para>
/// With "Animation effects" off in Windows every transition is the short crossfade
/// (<c>motion.duration.fade</c>); the bar and the green still appear, because they carry meaning, not
/// movement. Any selection the operator makes ends all of it at once (<see cref="SkipToEnd"/>): motion
/// never stands between the operator and the next click.
/// </para>
/// </remarks>
public sealed class DocumentMotion : IDisposable
{
    private readonly Func<bool> _animationsEnabled;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<Guid, DocumentStatus> _known = new();
    private readonly Dictionary<Guid, DateTime> _arrived = new();
    private readonly Dictionary<Guid, DateTime> _confirmed = new();
    private readonly Dictionary<Guid, DateTime> _leaving = new();
    private readonly Func<Action, IDisposable> _startTicker;
    private IDisposable? _ticker;
    private bool _initialized;

    /// <param name="animationsEnabled">Whether the OS wants animations; read at every frame.</param>
    /// <param name="now">The clock; tests pass their own.</param>
    /// <param name="startTicker">
    /// Starts calling the given action once a frame and returns what stops it. A UI timer by default;
    /// tests pass one they drive by hand, since Eto's timer needs a running platform.
    /// </param>
    public DocumentMotion(Func<bool> animationsEnabled, Func<DateTime>? now = null,
        Func<Action, IDisposable>? startTicker = null)
    {
        _animationsEnabled = animationsEnabled;
        _now = now ?? (() => DateTime.UtcNow);
        _startTicker = startTicker ?? StartUiTimer;
    }

    private static IDisposable StartUiTimer(Action tick)
    {
        // 60 frames a second, but only while something is in motion; see Tick.
        var timer = new UITimer { Interval = 1 / 60.0 };
        timer.Elapsed += (_, _) => tick();
        timer.Start();
        return new Stopper(() =>
        {
            timer.Stop();
            timer.Dispose();
        });
    }

    private sealed class Stopper(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }

    /// <summary>Raised on the UI thread whenever a value the panel or the canvas draws has moved.</summary>
    public event EventHandler? Frame;

    /// <summary>Raised for each document that has just appeared, for the screen reader announcement.</summary>
    public event EventHandler<ScannedDocument>? Arrived;

    private int FadeDuration(int animated) =>
        _animationsEnabled() ? animated : LeonardoTokens.Motion.DurationFade;

    /// <summary>
    /// Compares the queue with what was there last time. Call on the UI thread after every queue change.
    /// </summary>
    public void Observe(IReadOnlyList<ScannedDocument> documents)
    {
        var now = _now();
        var present = new HashSet<Guid>();
        foreach (var document in documents)
        {
            present.Add(document.Id);
            var status = document.Status;
            if (!_known.TryGetValue(document.Id, out var previous))
            {
                if (_initialized)
                {
                    _arrived[document.Id] = now;
                    Arrived?.Invoke(this, document);
                    if (status == DocumentStatus.Done) _confirmed[document.Id] = now;
                }
            }
            else if (previous != DocumentStatus.Done && status == DocumentStatus.Done)
            {
                _confirmed[document.Id] = now;
            }
            else if (status != DocumentStatus.Done)
            {
                // Put back in the queue (corrected after filing): the green it may still show is stale.
                _confirmed.Remove(document.Id);
            }
            _known[document.Id] = status;
            _leaving.Remove(document.Id);
        }
        foreach (var id in _known.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _known.Remove(id);
            _arrived.Remove(id);
            _confirmed.Remove(id);
            if (_initialized) _leaving[id] = now;
        }
        _initialized = true;
        EnsureTimer();
    }

    /// <summary>
    /// The arrive marker's opacity: fully visible for the hold, then fading out the way an element
    /// leaves (<c>motion.duration.short</c>), linear because only opacity changes.
    /// </summary>
    public float ArriveBar(Guid id) =>
        _arrived.TryGetValue(id, out var start)
            ? 1 - Progress(start, LeonardoTokens.Motion.ArriveHold, FadeDuration(LeonardoTokens.Motion.DurationShort),
                LeonardoTokens.Motion.EaseFade)
            : 0;

    /// <summary>
    /// How much of the success colour the status icon carries: all of it for the hold, then turning into
    /// the neutral colour -- a transform ("this becomes that"), so the move curve and the normal duration.
    /// </summary>
    public float Confirmation(Guid id) =>
        _confirmed.TryGetValue(id, out var start)
            ? 1 - Progress(start, LeonardoTokens.Motion.ArriveHold, FadeDuration(LeonardoTokens.Motion.DurationNormal),
                LeonardoTokens.Motion.EaseMove)
            : 0;

    /// <summary>
    /// A removed document's opacity while its row fades out, or null when it is not leaving. Leave is the
    /// short duration -- leaving is always quicker than arriving -- and, being opacity, linear.
    /// </summary>
    public float? Leaving(Guid id) =>
        _leaving.TryGetValue(id, out var start)
            ? 1 - Progress(start, 0, FadeDuration(LeonardoTokens.Motion.DurationShort), LeonardoTokens.Motion.EaseFade)
            : null;

    public bool IsLeaving(Guid id) => _leaving.ContainsKey(id);

    /// <summary>Jumps every running motion to its end state.</summary>
    public void SkipToEnd()
    {
        if (_arrived.Count == 0 && _confirmed.Count == 0 && _leaving.Count == 0)
        {
            return;
        }
        _arrived.Clear();
        _confirmed.Clear();
        _leaving.Clear();
        StopTimer();
        Frame?.Invoke(this, EventArgs.Empty);
    }

    private float Progress(DateTime start, int hold, int duration, (float, float, float, float) curve)
    {
        var elapsed = (_now() - start).TotalMilliseconds - hold;
        if (elapsed <= 0) return 0;
        if (elapsed >= duration) return 1;
        return CubicBezier.Evaluate(curve, (float) (elapsed / duration));
    }

    private bool IsOver(DateTime start, int hold, int duration) =>
        (_now() - start).TotalMilliseconds >= hold + duration;

    private bool IsMoving(DateTime start, int hold) => (_now() - start).TotalMilliseconds >= hold;

    private void EnsureTimer()
    {
        if (_arrived.Count + _confirmed.Count + _leaving.Count == 0 || _ticker != null)
        {
            return;
        }
        _ticker = _startTicker(Tick);
    }

    /// <summary>Whether a frame timer is running; it only does while something is in motion.</summary>
    internal bool IsTicking => _ticker != null;

    private void StopTimer()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    internal void Tick()
    {
        int shortFade = FadeDuration(LeonardoTokens.Motion.DurationShort);
        int normalFade = FadeDuration(LeonardoTokens.Motion.DurationNormal);
        int hold = LeonardoTokens.Motion.ArriveHold;
        // Something is visibly changing if a transition is under way, or one ended since the last tick
        // and its final frame has yet to be drawn.
        bool moving = _arrived.Values.Any(x => IsMoving(x, hold))
                      || _confirmed.Values.Any(x => IsMoving(x, hold))
                      || _leaving.Count > 0;
        Remove(_arrived, x => IsOver(x, hold, shortFade));
        Remove(_confirmed, x => IsOver(x, hold, normalFade));
        Remove(_leaving, x => IsOver(x, 0, shortFade));
        if (_arrived.Count + _confirmed.Count + _leaving.Count == 0)
        {
            StopTimer();
        }
        if (moving)
        {
            Frame?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void Remove(Dictionary<Guid, DateTime> map, Func<DateTime, bool> over)
    {
        foreach (var id in map.Where(x => over(x.Value)).Select(x => x.Key).ToList())
        {
            map.Remove(id);
        }
    }

    public void Dispose() => StopTimer();
}

/// <summary>A cubic-bezier easing curve, as CSS and the Leonardo tokens define it.</summary>
public static class CubicBezier
{
    /// <summary>The curve's y for a given x (the share of the duration that has passed).</summary>
    public static float Evaluate((float X1, float Y1, float X2, float Y2) curve, float x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var (x1, y1, x2, y2) = curve;
        // Solve x(t) = x by bisection -- monotonic for every curve whose x control points lie in [0, 1],
        // which CSS requires, and exact enough after 30 steps for any screen.
        float lo = 0, hi = 1, t = x;
        for (int i = 0; i < 30; i++)
        {
            t = (lo + hi) / 2;
            if (Bezier(x1, x2, t) < x) lo = t;
            else hi = t;
        }
        return Bezier(y1, y2, t);
    }

    private static float Bezier(float p1, float p2, float t)
    {
        float u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }
}
