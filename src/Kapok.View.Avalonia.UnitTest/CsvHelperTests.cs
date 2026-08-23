using Kapok.View.Avalonia.Helper;
using Xunit;

namespace Kapok.View.Avalonia.UnitTest;

public class CsvHelperTests
{
    [Theory]
    [InlineData("a\tb\tc", CsvHelper.LineSeparator.Tab)]
    [InlineData("a;b;c", CsvHelper.LineSeparator.Semicolon)]
    [InlineData("a,b,c", CsvHelper.LineSeparator.Comma)]
    [InlineData("just one cell", CsvHelper.LineSeparator.Unknown)]
    public void GuessCsvSeparator_PicksTheSeparatorThatProducesTheMostCells(string line, CsvHelper.LineSeparator expected)
    {
        Assert.Equal(expected, CsvHelper.GuessCsvSeparator(line));
    }

    [Fact]
    public void ParseLineCommaSeparated_HandlesQuotedCellsWithEmbeddedCommaAndEscapedQuote()
    {
        var cells = CsvHelper.ParseLineCommaSeparated("Buy milk,\"Two litres, whole\",\"He said \"\"hi\"\"\"");

        Assert.Equal(new[] { "Buy milk", "Two litres, whole", "He said \"hi\"" }, cells);
    }

    [Fact]
    public void ParseLineTabSeparated_TrimsWhitespaceAroundCells()
    {
        var cells = CsvHelper.ParseLineTabSeparated(" Buy milk \t Two litres \t 1.25 ");

        Assert.Equal(new[] { "Buy milk", "Two litres", "1.25" }, cells);
    }

    [Fact]
    public void ParseText_DetectsCrLfLineEnding()
    {
        var rows = CsvHelper.ParseText("Buy milk\tHigh\r\nBuy bread\tNormal\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Buy milk", "High" }, rows[0]);
        Assert.Equal(new[] { "Buy bread", "Normal" }, rows[1]);
    }

    [Fact]
    public void ParseText_DetectsLfOnlyLineEnding()
    {
        var rows = CsvHelper.ParseText("a,b\nc,d\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "c", "d" }, rows[1]);
    }

    [Fact]
    public void ParseText_DetectsCrOnlyLineEnding()
    {
        var rows = CsvHelper.ParseText("a,b\rc,d\r");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "c", "d" }, rows[1]);
    }

    [Fact]
    public void ParseText_SingleLineWithNoLineEnding_ReturnsOneRow()
    {
        var rows = CsvHelper.ParseText("Buy milk\tHigh");

        var row = Assert.Single(rows);
        Assert.Equal(new[] { "Buy milk", "High" }, row);
    }

    [Fact]
    public void ParseLines_SkipsBlankLines()
    {
        var rows = CsvHelper.ParseLines(new[] { "a,b", "", "   ", "c,d" });

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "c", "d" }, rows[1]);
    }

    [Fact]
    public void ParseLines_UnknownSeparator_TreatsEachLineAsASingleCell()
    {
        var rows = CsvHelper.ParseLines(new[] { "just one cell", "still one cell" });

        Assert.Equal(new[] { "just one cell" }, rows[0]);
        Assert.Equal(new[] { "still one cell" }, rows[1]);
    }
}
