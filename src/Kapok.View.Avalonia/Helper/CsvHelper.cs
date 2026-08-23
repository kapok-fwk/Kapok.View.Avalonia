using System.Text.RegularExpressions;

namespace Kapok.View.Avalonia.Helper;

/// <summary>
/// Splits pasted clipboard text into rows and cells, guessing the separator (tab, semicolon or
/// comma) from the first line. Direct port of Kapok.View.Wpf's CsvHelper - it is pure
/// <see cref="Regex"/> string handling with no WPF API in it at all.
///
/// Kept in this module rather than shared, matching the WPF original: its own header comment notes
/// "CsvHelper is twice implemented: one in Kapok.View.Wpf and once in Kapok.DataPort" - the same
/// duplication exists there, and de-duplicating it would mean changing core packages this port does
/// not own.
/// </summary>
public static class CsvHelper
{
    public enum LineSeparator
    {
        Unknown = 0,
        Tab,
        Semicolon,
        Comma
    }

    private static readonly Dictionary<LineSeparator, Func<string, string[]>> ParserOfSeparator = new()
    {
        [LineSeparator.Unknown] = ParseLineNotSeparated,
        [LineSeparator.Tab] = ParseLineTabSeparated,
        [LineSeparator.Semicolon] = ParseLineSemicolonSeparated,
        [LineSeparator.Comma] = ParseLineCommaSeparated
    };

    /// <summary>
    /// Picks the separator that splits <paramref name="oneLine"/> into the most cells. Returns
    /// <see cref="LineSeparator.Unknown"/> when no separator produces more than one cell.
    /// </summary>
    public static LineSeparator GuessCsvSeparator(string oneLine)
    {
        var candidates = new[]
        {
            (Separator: LineSeparator.Tab, Count: ParseLineTabSeparated(oneLine).Length),
            (Separator: LineSeparator.Semicolon, Count: ParseLineSemicolonSeparated(oneLine).Length),
            (Separator: LineSeparator.Comma, Count: ParseLineCommaSeparated(oneLine).Length)
        };

        var bestBet = candidates.OrderByDescending(c => c.Count).First();

        return bestBet.Count > 1 ? bestBet.Separator : LineSeparator.Unknown;
    }

    // CSV line parsing: from "jgr4" at
    // http://www.kimgentes.com/worshiptech-web-tools-page/2008/10/14/regex-pattern-for-parsing-csv-files-with-embedded-commas-dou.html
    public static string[] ParseLineCommaSeparated(string line)
        => ParseLine(line, @"\s?((?<x>(?=[,]+))|""(?<x>([^""]|"""")+)""|""(?<x>)""|(?<x>[^,]+)),?");

    public static string[] ParseLineTabSeparated(string line)
        => ParseLine(line, @"\s?((?<x>(?=[\t]+))|""(?<x>([^""]|"""")+)""|""(?<x>)""|(?<x>[^\t]+))\t?");

    public static string[] ParseLineSemicolonSeparated(string line)
        => ParseLine(line, @"\s?((?<x>(?=[;]+))|""(?<x>([^""]|"""")+)""|""(?<x>)""|(?<x>[^;]+));?");

    public static string[] ParseLineNotSeparated(string line) => new[] { line };

    private static string[] ParseLine(string line, string pattern)
        => Regex.Matches(line, pattern, RegexOptions.ExplicitCapture)
            .Select(m => m.Groups["x"].Value.Trim().Replace("\"\"", "\""))
            .ToArray();

    /// <summary>
    /// Splits a block of text into lines (detecting CRLF / CR / LF) and then each line into cells.
    /// </summary>
    public static List<string[]> ParseText(string text)
    {
        var carriageReturnDetected = false;
        var lineFeedDetected = false;
        var position = 0;

        while (position < text.Length)
        {
            var currentChar = text[position++];

            if (currentChar == '\n')
            {
                lineFeedDetected = true;
                break;
            }

            if (carriageReturnDetected)
                break;

            if (currentChar == '\r')
                carriageReturnDetected = true;
        }

        string? lineSeparator;
        if (carriageReturnDetected)
            lineSeparator = lineFeedDetected ? "\r\n" : "\r";
        else if (lineFeedDetected)
            lineSeparator = "\n";
        else
            lineSeparator = null; // no line ending at all - the text is a single line

        var lines = lineSeparator != null
            ? text.Split(new[] { lineSeparator }, StringSplitOptions.None)
            : new[] { text };

        return ParseLines(lines);
    }

    public static List<string[]> ParseLines(string[] lines)
    {
        var separator = lines.Length > 0 ? GuessCsvSeparator(lines[0]) : LineSeparator.Unknown;
        var parse = ParserOfSeparator[separator];

        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(parse)
            .ToList();
    }
}
