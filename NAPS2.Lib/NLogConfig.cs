using System.Text;
// Explicit rather than relying on the implicit using, because NAPS2.App.Worker compiles this file in
// and does not import LibUsers.targets.
using NAPS2.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Filters;
using NLog.LayoutRenderers;
using NLog.Targets;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace NAPS2;

public static class NLogConfig
{
    public static ILogger CreateLogger(Func<bool> enableDebugLogging)
    {
        LogManager.Setup().SetupExtensions(ext =>
        {
            ext.RegisterLayoutRenderer<CustomExceptionLayoutRenderer>("exception");
        });
        var config = new LoggingConfiguration();
        var target = new FileTarget
        {
            FileName = Path.Combine(Paths.AppData, "errorlog.txt"),
            Layout = "${longdate} ${processid} ${message} ${exception:format=tostring}",
            ArchiveAboveSize = 100000,
            MaxArchiveFiles = 1,
            ConcurrentWrites = true
        };
        var debugTarget = new FileTarget
        {
            FileName = Path.Combine(Paths.AppData, "debuglog.txt"),
            Layout = "${longdate} ${processid} ${message} ${exception:format=tostring}",
            ArchiveAboveSize = 100000,
            MaxArchiveFiles = 1,
            ConcurrentWrites = true
        };
        // Feeds the console window. Unlike the debug log file this is never filtered by the debug logging
        // setting -- the console is opened on demand, so it has to be able to show what already happened.
        var consoleTarget = new ConsoleLogTarget
        {
            Layout = "${level:uppercase=true} ${message} ${exception:format=tostring}"
        };
        config.AddTarget("errorlogfile", target);
        config.AddTarget("debuglogfile", debugTarget);
        config.AddTarget("scanmeconsole", consoleTarget);
        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, target));
        var debugRule = new LoggingRule("*", LogLevel.Trace, debugTarget);
        debugRule.Filters.Add(new WhenMethodFilter(_ => enableDebugLogging() ? FilterResult.Log : FilterResult.Ignore));
        config.LoggingRules.Add(debugRule);
        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, consoleTarget));
        LogManager.Configuration = config;
        return new NLogLoggerFactory().CreateLogger("ScanMe");
    }

    /// <summary>
    /// The debug logging flag as stored in an environment variable. This is used by worker processes to propagate from
    /// the parent process without needing to access the config directly.
    /// </summary>
    public static bool EnvDebugLogging
    {
        get => Environment.GetEnvironmentVariable("NAPS2_DEBUG_LOGGING") == "1";
        set => Environment.SetEnvironmentVariable("NAPS2_DEBUG_LOGGING", value ? "1" : "0");
    }

    private class CustomExceptionLayoutRenderer : ExceptionLayoutRenderer
    {
        protected override void AppendToString(StringBuilder sb, Exception ex)
        {
            // Note we don't want to use the AppendDemystified() helper
            // https://github.com/benaadams/Ben.Demystifier/issues/85
            sb.Append(ex.Demystify());
        }
    }
}