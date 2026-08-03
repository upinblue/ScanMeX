namespace NAPS2.Logging;

/// <summary>
/// The in-memory backlog behind the diagnostic console window. Everything the application logs is
/// mirrored here, so the console can be opened after something went wrong and still show what happened.
/// Nothing is written to disk from here -- the file log is a separate NLog target.
/// </summary>
public static class ConsoleLog
{
    /// <summary>
    /// How many lines are kept. A long duplex scan with barcode separation produces a few lines per page,
    /// so this covers a full working session without letting a runaway loop exhaust memory.
    /// </summary>
    public const int MaxLines = 10000;

    private static readonly object Lock = new();
    private static readonly List<string> Lines = [];

    /// <summary>
    /// The absolute index of Lines[0]. Grows as old lines are dropped, which lets readers hold a stable
    /// cursor across trimming.
    /// </summary>
    private static long _firstIndex;

    /// <summary>
    /// Appends one entry. Multi-line messages (stack traces) become one console line each, so the
    /// console never contains a line without a timestamp.
    /// </summary>
    public static void Append(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        lock (Lock)
        {
            foreach (var part in (message ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                Lines.Add($"{timestamp}  {part}");
            }
            if (Lines.Count > MaxLines)
            {
                var excess = Lines.Count - MaxLines;
                Lines.RemoveRange(0, excess);
                _firstIndex += excess;
            }
        }
    }

    /// <summary>
    /// Reads every line added since the given cursor. Readers poll this rather than subscribing to an
    /// event, which keeps logging threads from ever blocking on the UI thread.
    /// </summary>
    /// <param name="cursor">Zero for the whole backlog, otherwise the cursor from the previous call.</param>
    /// <returns>The new lines and the cursor to pass next time.</returns>
    public static (string[] Lines, long NextCursor) ReadFrom(long cursor)
    {
        lock (Lock)
        {
            var end = _firstIndex + Lines.Count;
            // A cursor older than the backlog means lines were dropped in between; resume at the oldest
            // line we still have rather than replaying nothing.
            var offset = (int) (Math.Max(cursor, _firstIndex) - _firstIndex);
            return (Lines.Skip(offset).ToArray(), end);
        }
    }
}
