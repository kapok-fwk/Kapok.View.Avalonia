using Kapok.View.Avalonia.Controls;
using Xunit;

namespace Kapok.View.Avalonia.UnitTest;

/// <summary>
/// Tests CustomDataGrid's private-static TryConvert (Excel-paste cell -> property-type
/// conversion) directly, via InternalsVisibleTo (see AssemblyInfo.cs in the main project) -
/// exercising it only indirectly through PasteRows would need a real DataGrid/DataGridColumn
/// wired up under Avalonia.Headless, which is what the ToDoAvaloniaApp KAPOK_HEADLESS_SCREENSHOT_
/// PASTE scenario already does; this covers the pure conversion logic underneath it in isolation.
/// </summary>
public class CustomDataGridTryConvertTests
{
    private enum Priority
    {
        Low,
        High
    }

    private static bool TryConvert(object? value, Type targetType, out object? converted)
        => CustomDataGrid.TryConvert(value, targetType, out converted);

    [Fact]
    public void EmptyStringIntoStringProperty_ClearsToEmptyString()
    {
        Assert.True(TryConvert("", typeof(string), out var converted));
        Assert.Equal(string.Empty, converted);
    }

    [Fact]
    public void NullIntoStringProperty_ClearsToEmptyString()
    {
        Assert.True(TryConvert(null, typeof(string), out var converted));
        Assert.Equal(string.Empty, converted);
    }

    [Fact]
    public void EmptyStringIntoNullableIntProperty_ClearsToNull()
    {
        Assert.True(TryConvert("", typeof(int?), out var converted));
        Assert.Null(converted);
    }

    [Fact]
    public void NullIntoReferenceTypeProperty_ClearsToNull()
    {
        Assert.True(TryConvert(null, typeof(object), out var converted));
        Assert.Null(converted);
    }

    [Fact]
    public void EmptyStringIntoNonNullableValueTypeProperty_IsSkipped()
    {
        // A blank pasted cell must not silently write default(T) (0, 01.01.0001, ...) into a
        // non-nullable value-type property - there is no "empty" to represent, so the cell is
        // left untouched instead.
        Assert.False(TryConvert("", typeof(int), out var converted));
        Assert.Null(converted);
    }

    [Fact]
    public void EmptyStringIntoNonNullableDateTimeProperty_IsSkipped()
    {
        Assert.False(TryConvert("", typeof(DateTime), out _));
    }

    [Fact]
    public void ValueAlreadyAssignableToTargetType_PassesThroughUnchanged()
    {
        var dueDate = new DateTime(2026, 3, 17);

        Assert.True(TryConvert(dueDate, typeof(DateTime), out var converted));
        Assert.Equal(dueDate, converted);
    }

    [Fact]
    public void StringIntoEnumProperty_ParsesByName()
    {
        Assert.True(TryConvert("High", typeof(Priority), out var converted));
        Assert.Equal(Priority.High, converted);
    }

    [Fact]
    public void StringIntoEnumProperty_IsCaseInsensitive()
    {
        Assert.True(TryConvert("high", typeof(Priority), out var converted));
        Assert.Equal(Priority.High, converted);
    }

    [Fact]
    public void InvalidStringIntoEnumProperty_Fails()
    {
        Assert.False(TryConvert("NotAPriority", typeof(Priority), out var converted));
        Assert.Null(converted);
    }

    [Fact]
    public void StringIntoNullableEnumProperty_ParsesByName()
    {
        Assert.True(TryConvert("Low", typeof(Priority?), out var converted));
        Assert.Equal(Priority.Low, converted);
    }

    [Fact]
    public void StringIntoGuidProperty_Parses()
    {
        var guid = Guid.NewGuid();

        Assert.True(TryConvert(guid.ToString(), typeof(Guid), out var converted));
        Assert.Equal(guid, converted);
    }

    [Fact]
    public void InvalidStringIntoGuidProperty_Fails()
    {
        Assert.False(TryConvert("not-a-guid", typeof(Guid), out _));
    }

    [Fact]
    public void StringIntoDecimalProperty_ConvertsUsingChangeType()
    {
        Assert.True(TryConvert("1.25", typeof(decimal), out var converted));
        Assert.Equal(1.25m, converted);
    }

    [Fact]
    public void InvalidStringIntoDecimalProperty_Fails()
    {
        Assert.False(TryConvert("not-a-number", typeof(decimal), out var converted));
        Assert.Null(converted);
    }

    [Fact]
    public void StringIntoNullableDecimalProperty_Converts()
    {
        Assert.True(TryConvert("2", typeof(decimal?), out var converted));
        Assert.Equal(2m, converted);
    }

    [Fact]
    public void StringIntoIntProperty_Converts()
    {
        Assert.True(TryConvert("42", typeof(int), out var converted));
        Assert.Equal(42, converted);
    }

    [Fact]
    public void OverflowingStringIntoIntProperty_Fails()
    {
        Assert.False(TryConvert("99999999999999999999", typeof(int), out _));
    }
}
