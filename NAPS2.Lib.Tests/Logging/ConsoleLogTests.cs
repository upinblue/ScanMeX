using NAPS2.Logging;
using Xunit;

namespace NAPS2.Lib.Tests.Logging;

public class ConsoleLogTests
{
    [Fact]
    public void ReadFrom_ReturnsOnlyNewLines()
    {
        var (_, start) = ConsoleLog.ReadFrom(0);

        ConsoleLog.Append("first");
        ConsoleLog.Append("second");

        var (lines, cursor) = ConsoleLog.ReadFrom(start);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("first", lines[0]);
        Assert.EndsWith("second", lines[1]);

        // Nothing new since the last read.
        var (none, _) = ConsoleLog.ReadFrom(cursor);
        Assert.Empty(none);
    }

    [Fact]
    public void Append_TimestampsEveryLineOfAMultiLineMessage()
    {
        var (_, start) = ConsoleLog.ReadFrom(0);

        ConsoleLog.Append("outer\r\n  inner");

        var (lines, _) = ConsoleLog.ReadFrom(start);
        Assert.Equal(2, lines.Length);
        // "HH:mm:ss.fff  text"
        Assert.All(lines, line => Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3}  ", line));
    }

    [Fact]
    public void ReadFrom_WithAStaleCursor_ResumesAtTheOldestLineStillHeld()
    {
        // Overfill the buffer so the cursor from before is older than anything still held.
        var (_, stale) = ConsoleLog.ReadFrom(0);
        for (var i = 0; i < ConsoleLog.MaxLines + 10; i++)
        {
            ConsoleLog.Append($"line {i}");
        }

        var (lines, _) = ConsoleLog.ReadFrom(stale);

        // Capped rather than replaying a cursor that no longer exists, and no exception.
        Assert.Equal(ConsoleLog.MaxLines, lines.Length);
        Assert.EndsWith($"line {ConsoleLog.MaxLines + 9}", lines[^1]);
    }
}
