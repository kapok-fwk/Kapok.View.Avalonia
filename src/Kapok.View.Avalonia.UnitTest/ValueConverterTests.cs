using System.Globalization;
using Kapok.View.Avalonia.ValueConverter;
using Xunit;

namespace Kapok.View.Avalonia.UnitTest;

/// <summary>
/// Port of Kapok.View.Wpf.UnitTest's ValueConverter.cs (NullToBoolConverter's own test) plus
/// InverseNullToBoolConverter, which lives in the same source file here (see
/// ValueConverter/NullToBoolConverter.cs) but has no WPF-side test to port.
/// </summary>
public class ValueConverterTests
{
    [Fact]
    public void NullToBoolConverter_NullValue_ReturnsTrue()
    {
        var converter = new NullToBoolConverter();

        Assert.True((bool)converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void NullToBoolConverter_NonNullValue_ReturnsFalse()
    {
        var converter = new NullToBoolConverter();

        Assert.False((bool)converter.Convert(new object(), typeof(bool), null, CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void NullToBoolConverter_ConvertBack_Throws()
    {
        var converter = new NullToBoolConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(true, typeof(object), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InverseNullToBoolConverter_NullValue_ReturnsFalse()
    {
        var converter = new InverseNullToBoolConverter();

        Assert.False((bool)converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture)!);
    }

    [Fact]
    public void InverseNullToBoolConverter_NonNullValue_ReturnsTrue()
    {
        var converter = new InverseNullToBoolConverter();

        Assert.True((bool)converter.Convert("not null", typeof(bool), null, CultureInfo.InvariantCulture)!);
    }
}
