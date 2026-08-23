using System.Globalization;
using System.Xml;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Kapok.View.Avalonia.Helper;

/// <summary>
/// Reads tabular data off the clipboard for the DataGrid's Excel-style paste. Port of
/// Kapok.View.Wpf's ClipboardHelper, with the same three-way format preference (Excel's "XML
/// Spreadsheet" &gt; CSV &gt; plain text).
///
/// The API shape is genuinely different, not a stylistic choice: WPF's <c>Clipboard.GetDataObject()</c>
/// is synchronous and returns an <c>IDataObject</c> with <c>GetFormats()</c>/<c>GetData(format)</c>.
/// Avalonia's clipboard is fully async (<see cref="IClipboard.TryGetDataAsync"/> -&gt; an
/// <see cref="IAsyncDataTransfer"/>) and formats are strongly-typed <see cref="DataFormat"/> values
/// rather than magic strings, so the platform-specific names are declared once below.
/// </summary>
public static class ClipboardHelper
{
    /// <summary>
    /// Excel's own clipboard format. Only present on Windows (and only when Excel put it there) -
    /// on other platforms the CSV/text branches carry the paste, which is why the XML branch is a
    /// preference rather than a requirement.
    /// </summary>
    private static readonly DataFormat<byte[]> XmlSpreadsheetFormat = DataFormat.CreateBytesPlatformFormat("XML Spreadsheet");

    private static readonly DataFormat<string> CsvFormat = DataFormat.CreateStringPlatformFormat("CSV");

    private const string SpreadsheetNamespaceUri = "urn:schemas-microsoft-com:office:spreadsheet";

    /// <summary>
    /// Returns the clipboard content as rows of cell values, or an empty list when the clipboard
    /// holds nothing tabular.
    /// </summary>
    public static async Task<List<object?[]>> ParseClipboardDataAsync(IClipboard? clipboard)
    {
        if (clipboard == null)
            return new List<object?[]>();

        using var dataTransfer = await clipboard.TryGetDataAsync().ConfigureAwait(true);
        if (dataTransfer == null)
            return new List<object?[]>();

        if (dataTransfer.Contains(XmlSpreadsheetFormat))
        {
            var bytes = await dataTransfer.TryGetValueAsync(XmlSpreadsheetFormat).ConfigureAwait(true);
            if (bytes is { Length: > 0 })
                return ParseXmlSpreadsheet(bytes);
        }

        if (dataTransfer.Contains(CsvFormat))
        {
            var csv = await dataTransfer.TryGetValueAsync(CsvFormat).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(csv))
            {
                // WPF split CSV strictly on "\r\n" with a comment admitting it breaks on embedded
                // newlines; ParseText detects the actual line ending instead, which is at least no
                // worse and handles clipboards from non-Windows apps.
                return CsvHelper.ParseText(csv)
                    .Select(row => row.Cast<object?>().ToArray())
                    .ToList();
            }
        }

        var text = await dataTransfer.TryGetTextAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text))
        {
            return CsvHelper.ParseText(text)
                .Select(row => row.Cast<object?>().ToArray())
                .ToList();
        }

        return new List<object?[]>();
    }

    /// <summary>
    /// Parses Excel's "XML Spreadsheet" clipboard payload. Direct port of the WPF version,
    /// including its handling of the <c>ss:Index</c> attributes Excel uses to skip empty
    /// rows/cells, and the per-cell <c>ss:Type</c> (DateTime / Number / String) that gives pasted
    /// values a real CLR type instead of a string.
    /// </summary>
    public static List<object?[]> ParseXmlSpreadsheet(byte[] xmlBytes)
    {
        var clipboardData = new List<object?[]>();

        var xml = new XmlDocument();
        using (var stream = new MemoryStream(xmlBytes))
            xml.Load(stream);

        var tables = xml.GetElementsByTagName("Table");
        if (tables.Count == 0 || tables[0] == null)
            return clipboardData;

        var table = tables[0]!;

        var columnCountString = table.Attributes?.GetNamedItem("ExpandedColumnCount", SpreadsheetNamespaceUri)?.InnerText;
        if (columnCountString == null)
            throw new NotSupportedException(
                "Excel spreadsheet does not give information about the number of columns in tag Table, " +
                $"attribute ExpandedColumnCount in namespace {SpreadsheetNamespaceUri}.");

        var columnCount = int.Parse(columnCountString, CultureInfo.InvariantCulture);

        foreach (XmlNode row in table.ChildNodes)
        {
            if (!string.Equals(row.Name, "row", StringComparison.OrdinalIgnoreCase))
                continue;

            var rowIndexString = row.Attributes?.GetNamedItem("Index", SpreadsheetNamespaceUri)?.InnerText;
            if (rowIndexString != null)
            {
                var index = int.Parse(rowIndexString, CultureInfo.InvariantCulture);
                for (var n = clipboardData.Count; n < index - 1; n++)
                    clipboardData.Add(new object?[columnCount]); // fill in the skipped empty rows
            }

            var lineCells = new object?[columnCount];
            var i = 0;

            foreach (XmlNode cell in row.ChildNodes)
            {
                var cellIndexString = cell.Attributes?.GetNamedItem("Index", SpreadsheetNamespaceUri)?.InnerText;
                if (cellIndexString != null)
                    i = int.Parse(cellIndexString, CultureInfo.InvariantCulture) - 1; // ss:Index is 1-based

                if (cell.ChildNodes.Count == 0)
                {
                    i++; // empty cell
                    continue;
                }

                var typeString = cell.ChildNodes[0]?.Attributes?.GetNamedItem("Type", SpreadsheetNamespaceUri)?.InnerText;

                object cellValue = typeString switch
                {
                    "DateTime" => DateTime.Parse(cell.InnerText, CultureInfo.InvariantCulture),
                    "Number" => decimal.Parse(cell.InnerText, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture),
                    _ => cell.InnerText
                };

                if (i < lineCells.Length)
                    lineCells[i] = cellValue;
                i++;
            }

            clipboardData.Add(lineCells);
        }

        return clipboardData;
    }
}
