using Eto.Drawing;
using Eto.Forms;
using NAPS2.EtoForms.Layout;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// The diagnostic console: a single read-only text box that every scan, barcode, separation and upload
/// event is appended to. Opened from the toolbar, and only ever one at a time.
/// </summary>
public class ConsoleForm : EtoFormBase
{
    /// <summary>
    /// How often new lines are pulled out of <see cref="ConsoleLog"/>. Polling rather than subscribing
    /// keeps the logging threads free of any dependency on the UI thread, and batches bursts during a
    /// fast scan into one update.
    /// </summary>
    private const double PollIntervalSeconds = 0.25;

    private readonly TextArea _text = new()
    {
        ReadOnly = true,
        // Wrapped rather than horizontally scrolled: log lines carrying a full path or an HTTP error are
        // long, and a line the operator has to scroll sideways for is a line they won't read.
        Wrap = true,
        Font = Fonts.Monospace(9)
    };

    private readonly UITimer _timer = new() { Interval = PollIntervalSeconds };

    private long _cursor;

    public ConsoleForm(Naps2Config config) : base(config)
    {
        Title = UiStrings.Console;
        IconName = "console";
        _timer.Elapsed += (_, _) => DrainNewLines();
    }

    protected override void BuildLayout()
    {
        FormStateController.DefaultExtraLayoutSize = new Size(500, 300);
        LayoutController.Content = L.Column(
            _text.NaturalSize(600, 400).Scale()
        );
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Pick up everything logged before the window was opened, then keep following.
        DrainNewLines();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void DrainNewLines()
    {
        var (lines, nextCursor) = ConsoleLog.ReadFrom(_cursor);
        _cursor = nextCursor;
        if (lines.Length == 0)
        {
            return;
        }
        // Only follow the tail if the user hasn't scrolled up to read something.
        var wasAtEnd = _text.CaretIndex >= _text.Text.Length;
        _text.Append(string.Join(Environment.NewLine, lines) + Environment.NewLine, wasAtEnd);
    }
}
