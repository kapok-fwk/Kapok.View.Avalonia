using System.Globalization;
using Avalonia;
using Kapok.View.Avalonia.ValueConverter;
using Xunit;

namespace Kapok.View.Avalonia.UnitTest;

public class DataGridHierarchyColumnLevelToMarginConverterTests
{
    private readonly DataGridHierarchyColumnLevelToMarginConverter _converter = new();

    [Fact]
    public void Convert_NoParameter_IndentsLeftOnlyByLevelTimesIndent()
    {
        var result = _converter.Convert(2, typeof(Thickness), null, CultureInfo.InvariantCulture);

        Assert.Equal(new Thickness(40, 0, 0, 0), result);
    }

    [Fact]
    public void Convert_LevelZero_HasNoIndent()
    {
        var result = _converter.Convert(0, typeof(Thickness), null, CultureInfo.InvariantCulture);

        Assert.Equal(new Thickness(0, 0, 0, 0), result);
    }

    [Fact]
    public void Convert_HLineParameter_AddsExtraNineAndTopOne()
    {
        var result = _converter.Convert(1, typeof(Thickness), "HLine", CultureInfo.InvariantCulture);

        Assert.Equal(new Thickness(29, 1, 0, 0), result);
    }

    [Fact]
    public void Convert_VLineParameter_MatchesPlainIndent()
    {
        var result = _converter.Convert(1, typeof(Thickness), "VLine", CultureInfo.InvariantCulture);

        Assert.Equal(new Thickness(20, 0, 0, 0), result);
    }

    [Fact]
    public void Convert_ToggleButtonOrAnyOtherParameter_AddsTopOneOnly()
    {
        var toggleButton = _converter.Convert(1, typeof(Thickness), "ToggleButton", CultureInfo.InvariantCulture);
        var anythingElse = _converter.Convert(1, typeof(Thickness), "whatever", CultureInfo.InvariantCulture);

        Assert.Equal(new Thickness(20, 1, 0, 0), toggleButton);
        Assert.Equal(new Thickness(20, 1, 0, 0), anythingElse);
    }

    [Fact]
    public void Convert_NonIntValue_ReturnsNull()
    {
        var result = _converter.Convert("not a level", typeof(Thickness), null, CultureInfo.InvariantCulture);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(new Thickness(20, 0, 0, 0), typeof(int), null, CultureInfo.InvariantCulture));
    }
}
