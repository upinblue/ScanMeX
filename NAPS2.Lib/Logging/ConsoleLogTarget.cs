using NLog;
using NLog.Targets;

namespace NAPS2.Logging;

/// <summary>
/// Mirrors the application's log into <see cref="ConsoleLog"/>. Registering this as an NLog target means
/// every existing log call shows up in the console window without the call site knowing about it.
/// </summary>
[Target("ScanMeConsole")]
public class ConsoleLogTarget : TargetWithLayout
{
    protected override void Write(LogEventInfo logEvent)
    {
        ConsoleLog.Append(RenderLogEvent(Layout, logEvent));
    }
}
