using System.Text;
using Kapok.View.Avalonia.Helper;
using Xunit;

namespace Kapok.View.Avalonia.UnitTest;

/// <summary>
/// Tests <see cref="ClipboardHelper.ParseXmlSpreadsheet"/> against real Excel "XML Spreadsheet"
/// clipboard payloads (the format Excel puts on the clipboard alongside CSV/plain text) - built by
/// hand rather than copied from a real Excel export, but matching the shape Excel produces: a
/// default namespace and an "ss" prefix both bound to the same
/// urn:schemas-microsoft-com:office:spreadsheet URI (real Excel documents declare both), which is
/// what lets element names (Row/Cell/Data) resolve unprefixed while ss:Index/ss:Type/
/// ss:ExpandedColumnCount still resolve through the same namespace via the "ss" prefix.
/// </summary>
public class ClipboardHelperTests
{
    private const string NamespaceDeclarations =
        "xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" " +
        "xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"";

    private static byte[] Xml(string body) => Encoding.UTF8.GetBytes(
        $"<?xml version=\"1.0\"?><Workbook {NamespaceDeclarations}>{body}</Workbook>");

    [Fact]
    public void ParseXmlSpreadsheet_NoTableElement_ReturnsEmptyList()
    {
        var result = ClipboardHelper.ParseXmlSpreadsheet(Xml("<Worksheet ss:Name=\"Sheet1\"></Worksheet>"));

        Assert.Empty(result);
    }

    [Fact]
    public void ParseXmlSpreadsheet_ReadsPlainStringAndNumberCells()
    {
        var xml = Xml("""
            <Worksheet><Table ss:ExpandedColumnCount="2">
              <Row>
                <Cell><Data ss:Type="String">Buy milk</Data></Cell>
                <Cell><Data ss:Type="Number">1.25</Data></Cell>
              </Row>
            </Table></Worksheet>
            """);

        var rows = ClipboardHelper.ParseXmlSpreadsheet(xml);

        var row = Assert.Single(rows);
        Assert.Equal("Buy milk", row[0]);
        Assert.Equal(1.25m, row[1]);
    }

    [Fact]
    public void ParseXmlSpreadsheet_ParsesDateTimeTypedCell()
    {
        var xml = Xml("""
            <Worksheet><Table ss:ExpandedColumnCount="1">
              <Row>
                <Cell><Data ss:Type="DateTime">2024-01-15T00:00:00.000</Data></Cell>
              </Row>
            </Table></Worksheet>
            """);

        var rows = ClipboardHelper.ParseXmlSpreadsheet(xml);

        var row = Assert.Single(rows);
        Assert.Equal(new DateTime(2024, 1, 15), row[0]);
    }

    [Fact]
    public void ParseXmlSpreadsheet_CellWithoutTypeAttribute_ReadsAsPlainString()
    {
        var xml = Xml("""
            <Worksheet><Table ss:ExpandedColumnCount="1">
              <Row>
                <Cell><Data>plain text</Data></Cell>
              </Row>
            </Table></Worksheet>
            """);

        var row = Assert.Single(ClipboardHelper.ParseXmlSpreadsheet(xml));
        Assert.Equal("plain text", row[0]);
    }

    [Fact]
    public void ParseXmlSpreadsheet_EmptyCellElement_LeavesCellNull()
    {
        // Excel omits the Data child entirely for a genuinely empty cell rather than emitting a
        // Cell with empty content - cell.ChildNodes.Count == 0 is exactly that case.
        var xml = Xml("""
            <Worksheet><Table ss:ExpandedColumnCount="2">
              <Row>
                <Cell/>
                <Cell><Data ss:Type="String">second</Data></Cell>
              </Row>
            </Table></Worksheet>
            """);

        var row = Assert.Single(ClipboardHelper.ParseXmlSpreadsheet(xml));
        Assert.Null(row[0]);
        Assert.Equal("second", row[1]);
    }

    [Fact]
    public void ParseXmlSpreadsheet_CellIndexAttribute_SkipsColumnsExcelOmitted()
    {
        // Excel skips emitting a Cell element for leading/interior empty cells and instead gives
        // the next real cell an ss:Index (1-based) saying which column it actually belongs to.
        var xml = Xml("""
            <Worksheet><Table ss:ExpandedColumnCount="3">
              <Row>
                <Cell><Data ss:Type="String">first</Data></Cell>
                <Cell ss:Index="3"><Data ss:Type="Number">42</Data></Cell>
              </Row>
            </Table></Worksheet>
            """);

        var row = Assert.Single(ClipboardHelper.ParseXmlSpreadsheet(xml));
        Assert.Equal("first", row[0]);
        Assert.Null(row[1]);
        Assert.Equal(42m, row[2]);
    }

    [Fact]
    public void ParseXmlSpreadsheet_RowIndexAttribute_FillsSkippedEmptyRowsWithNullCells()
    {
        // Same ss:Index gap-skipping as cells, but for entirely empty rows: a Row with Index="3"
        // means row 2 (1-based) was blank and never emitted at all.
        var xml = Xml("""
            <Worksheet><Table ss:ExpandedColumnCount="2">
              <Row>
                <Cell><Data ss:Type="String">row one</Data></Cell>
              </Row>
              <Row ss:Index="3">
                <Cell><Data ss:Type="String">row three</Data></Cell>
              </Row>
            </Table></Worksheet>
            """);

        var rows = ClipboardHelper.ParseXmlSpreadsheet(xml);

        Assert.Equal(3, rows.Count);
        Assert.Equal("row one", rows[0][0]);
        Assert.Null(rows[1][0]);
        Assert.Null(rows[1][1]);
        Assert.Equal("row three", rows[2][0]);
    }

    [Fact]
    public void ParseXmlSpreadsheet_MissingExpandedColumnCount_Throws()
    {
        var xml = Xml("<Worksheet><Table><Row><Cell><Data>x</Data></Cell></Row></Table></Worksheet>");

        Assert.Throws<NotSupportedException>(() => ClipboardHelper.ParseXmlSpreadsheet(xml));
    }
}
