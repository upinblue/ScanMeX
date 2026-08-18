using System.Text;
using ZXing;
using ZXing.Common;

namespace NAPS2.Scan;

/// <summary>
/// A single Code 39 value read off a page by <see cref="DamagedCode39Reader"/>.
/// </summary>
/// <param name="Text">The decoded content, without the guard characters.</param>
/// <param name="X">The left edge of the start guard, for putting the value in page reading order.</param>
/// <param name="Y">The scan line it was first read on, for the same reason.</param>
/// <param name="StopGuardDamaged">
/// Whether the symbol ended in a group that is character-shaped but decodes to nothing, which is what a
/// damaged stop guard looks like. False means the symbol was well-formed and ZXing missed it for some
/// other reason -- worth reporting differently, because only the first case says the paper is at fault.
/// </param>
internal record Code39Reading(string Text, int X, int Y, bool StopGuardDamaged);

/// <summary>
/// Reads Code 39 barcodes that ZXing refuses, in particular ones whose stop guard is printed wrong.
/// </summary>
/// <remarks>
/// ZXing classifies each of a character's nine elements as narrow or wide and requires the terminating
/// character to be exactly '*'; a symbol whose stop guard misses that is discarded whole, however clean
/// its data characters are. Measured on a customer's process-order cover sheets: the data and the start
/// guard decode perfectly, while in the stop guard the edge between the fourth and fifth element sits
/// about 1.5 modules too far right -- the space comes out 2.5 modules wide instead of 1 and the bar 1.5
/// instead of 3, so the character reads as a pattern that is in no Code 39 table. The same defect sits on
/// every sheet from that source, at the same character rather than at a fixed place on the page, so it
/// comes from whatever prints them. ZXing.Net, zxing-cpp and ZBar all refuse those sheets.
///
/// This reader exists to let an operator accept such a symbol deliberately, so it is built to be
/// distrusted: it only ever runs when the profile asks for a lower strictness, it requires an intact '*'
/// start guard to anchor on, every data character has to decode cleanly, the terminating group has to be
/// character-shaped and followed by a quiet zone, and the same value has to come off several scan lines.
/// Those last guards are what separate a barcode from print noise. Measured with every guard but the
/// geometry ones switched off, on the customer's eight pages and on a form-shaped noisy page under four
/// noise seeds: a real barcode is fourteen characters agreed on by thirty-five scan lines, while the
/// longest thing the noise ever produces is three characters, on at most five lines. Length is therefore
/// what the levels in <see cref="Code39Tolerance"/> mainly buy and mainly spend.
/// </remarks>
internal static class DamagedCode39Reader
{
    /// <summary>
    /// ZXing's own Code 39 alphabet and encoding table. Note '*' is not in it: the guard character is
    /// held separately, exactly as ZXing does, so that a data character can never decode as a guard.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    private static readonly int[] CharacterEncodings =
    [
        0x034, 0x121, 0x061, 0x160, 0x031, 0x130, 0x070, 0x025, 0x124, 0x064, // 0-9
        0x109, 0x049, 0x148, 0x019, 0x118, 0x058, 0x00D, 0x10C, 0x04C, 0x01C, // A-J
        0x103, 0x043, 0x142, 0x013, 0x112, 0x052, 0x007, 0x106, 0x046, 0x016, // K-T
        0x181, 0x0C1, 0x1C0, 0x091, 0x190, 0x0D0, 0x085, 0x184, 0x0C4, 0x0A8, // U-Z - . space $
        0x0A2, 0x08A, 0x02A // / + %
    ];

    private const int AsteriskEncoding = 0x094;

    /// <summary>The nine elements of a character, plus the narrow gap that follows it.</summary>
    private const int ElementsPerCharacter = 9;

    private const int GroupStride = ElementsPerCharacter + 1;

    /// <summary>A character spans six narrow and three wide elements, so fifteen modules.</summary>
    private const int ModulesPerCharacter = 15;

    /// <summary>
    /// Roughly how many scan lines to take off a page. Fixed as a count rather than a pixel step so that
    /// the "read on at least N lines" guard means the same thing whatever the resolution, and so that a
    /// short barcode still crosses enough of them to clear it.
    /// </summary>
    private const int TargetScanLines = 600;

    /// <summary>The white run a symbol has to end in, in modules. The standard asks for ten.</summary>
    private const double MinTrailingQuietZoneModules = 3.0;

    public static List<Code39Reading> Read(LuminanceSource source, Code39Tolerance tolerance)
    {
        BitMatrix matrix;
        try
        {
            matrix = new HybridBinarizer(source).BlackMatrix;
        }
        catch (Exception)
        {
            // The same page still went through the strict passes, so a binarizer that can't handle it
            // costs nothing here.
            return [];
        }
        if (matrix == null)
        {
            return [];
        }

        // text -> (how many scan lines produced it, where it was first seen, whether the stop was damaged)
        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var step = Math.Max(1, matrix.Height / TargetScanLines);
        BitArray? row = null;
        for (var y = 0; y < matrix.Height; y += step)
        {
            row = matrix.getRow(y, row);
            foreach (var reading in ReadRow(row, y, tolerance))
            {
                if (candidates.TryGetValue(reading.Text, out var existing))
                {
                    // A value that comes off some lines with a damaged stop and off others cleanly is
                    // reported as clean: the damage claim is about the paper, so only make it when no
                    // scan line managed to read the guard.
                    candidates[reading.Text] = existing with
                    {
                        ScanLines = existing.ScanLines + 1,
                        StopGuardDamaged = existing.StopGuardDamaged && reading.StopGuardDamaged
                    };
                }
                else
                {
                    candidates[reading.Text] = new Candidate(reading.X, reading.Y, reading.StopGuardDamaged, 1);
                }
            }
        }

        return candidates
            .Where(x => x.Key.Length >= tolerance.MinCharacters && x.Value.ScanLines >= tolerance.MinScanLines)
            .Select(x => new Code39Reading(x.Key, x.Value.X, x.Value.Y, x.Value.StopGuardDamaged))
            .ToList();
    }

    private record Candidate(int X, int Y, bool StopGuardDamaged, int ScanLines);

    /// <summary>
    /// Reads one scan line. A value is reported once per line however many times it appears on it, so
    /// that the "read on at least N scan lines" guard counts lines rather than overlapping attempts at
    /// the same symbol.
    /// </summary>
    private static List<Code39Reading> ReadRow(BitArray row, int y, Code39Tolerance tolerance)
    {
        var found = new List<Code39Reading>();
        var runs = BuildRuns(row);
        for (var start = 0; start + ElementsPerCharacter <= runs.Count; start++)
        {
            if (!runs[start].IsBlack || ToNarrowWidePattern(runs, start) != AsteriskEncoding)
            {
                continue;
            }
            var reading = DecodeFrom(runs, start, y, tolerance);
            if (reading != null && !found.Any(x => x.Text == reading.Text))
            {
                found.Add(reading);
            }
        }
        return found;
    }

    /// <summary>
    /// Decodes characters after a start guard until one of them doesn't decode. That failing group is the
    /// symbol's terminator: accepted when it is character-shaped and followed by a quiet zone, which is
    /// what a damaged stop guard looks like, and rejected when it is neither, which is what running off
    /// the barcode into the rest of the page looks like.
    /// </summary>
    private static Code39Reading? DecodeFrom(
        IReadOnlyList<Run> runs, int start, int y, Code39Tolerance tolerance)
    {
        var text = new StringBuilder();
        var widthTotal = 0;
        var position = start + GroupStride;
        while (position + ElementsPerCharacter <= runs.Count)
        {
            var pattern = ToNarrowWidePattern(runs, position);
            var groupWidth = GroupWidth(runs, position);
            if (pattern == AsteriskEncoding)
            {
                return text.Length == 0
                    ? null
                    : new Code39Reading(text.ToString(), runs[start].Start, y, false);
            }
            var character = PatternToChar(pattern);
            if (character == null)
            {
                return text.Length > 0 &&
                       IsDamagedStopGuard(runs, position, widthTotal / text.Length, tolerance)
                    ? new Code39Reading(text.ToString(), runs[start].Start, y, true)
                    : null;
            }
            text.Append(character.Value);
            widthTotal += groupWidth;
            position += GroupStride;
        }
        // The symbol ran to the end of the row without a terminator of any kind. That is a crop through
        // the middle of a barcode, not a barcode, so there is no value here worth reporting.
        return null;
    }

    private static bool IsDamagedStopGuard(
        IReadOnlyList<Run> runs, int position, int meanCharacterWidth, Code39Tolerance tolerance)
    {
        if (meanCharacterWidth <= 0)
        {
            return false;
        }
        var width = GroupWidth(runs, position);
        if (Math.Abs(width - meanCharacterWidth) > meanCharacterWidth * tolerance.MaxTerminatorWidthDeviation)
        {
            return false;
        }
        var module = meanCharacterWidth / (double) ModulesPerCharacter;
        var after = position + ElementsPerCharacter;
        if (after >= runs.Count)
        {
            // The row ends right after the guard, so the quiet zone is off the edge of the image rather
            // than absent.
            return true;
        }
        return !runs[after].IsBlack && runs[after].Length >= module * MinTrailingQuietZoneModules;
    }

    private static char? PatternToChar(int pattern)
    {
        if (pattern < 0)
        {
            return null;
        }
        var index = Array.IndexOf(CharacterEncodings, pattern);
        return index >= 0 ? Alphabet[index] : null;
    }

    private static int GroupWidth(IReadOnlyList<Run> runs, int position)
    {
        var total = 0;
        for (var i = 0; i < ElementsPerCharacter; i++)
        {
            total += runs[position + i].Length;
        }
        return total;
    }

    /// <summary>
    /// ZXing's own narrow/wide classification, reproduced so that a character this reader accepts is a
    /// character the strict path would have accepted too. It raises the narrow threshold through the
    /// element widths until exactly three come out wide, then rejects the group if one of those three is
    /// half again the average -- which is what stops a run of unrelated print from being read as a
    /// character.
    /// </summary>
    private static int ToNarrowWidePattern(IReadOnlyList<Run> runs, int position)
    {
        var maxNarrow = 0;
        while (true)
        {
            var minAboveNarrow = int.MaxValue;
            for (var i = 0; i < ElementsPerCharacter; i++)
            {
                var length = runs[position + i].Length;
                if (length > maxNarrow && length < minAboveNarrow)
                {
                    minAboveNarrow = length;
                }
            }
            if (minAboveNarrow == int.MaxValue)
            {
                return -1;
            }
            maxNarrow = minAboveNarrow;

            var pattern = 0;
            var wideCount = 0;
            var wideTotal = 0;
            for (var i = 0; i < ElementsPerCharacter; i++)
            {
                var length = runs[position + i].Length;
                if (length > maxNarrow)
                {
                    pattern |= 1 << (ElementsPerCharacter - 1 - i);
                    wideCount++;
                    wideTotal += length;
                }
            }
            if (wideCount == 3)
            {
                for (var i = 0; i < ElementsPerCharacter; i++)
                {
                    var length = runs[position + i].Length;
                    if (length > maxNarrow && length * 2 >= wideTotal)
                    {
                        return -1;
                    }
                }
                return pattern;
            }
            if (wideCount < 3)
            {
                return -1;
            }
        }
    }

    private readonly record struct Run(bool IsBlack, int Start, int Length);

    private static List<Run> BuildRuns(BitArray row)
    {
        var runs = new List<Run>();
        var size = row.Size;
        if (size == 0)
        {
            return runs;
        }
        var current = row[0];
        var start = 0;
        for (var x = 1; x <= size; x++)
        {
            var value = x < size && row[x];
            if (x == size || value != current)
            {
                runs.Add(new Run(current, start, x - start));
                current = value;
                start = x;
            }
        }
        return runs;
    }
}
