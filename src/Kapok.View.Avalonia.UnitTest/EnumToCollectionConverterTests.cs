using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Kapok.View.Avalonia.ValueConverter;
using Xunit;

namespace Kapok.View.Avalonia.UnitTest;

public class EnumToCollectionConverterTests
{
    private enum PlainEnum
    {
        None,
        First
    }

    private enum DisplayEnum
    {
        // Plain Name, no ResourceType - the shape every real [Display] usage in this port's own
        // showcase app actually uses (ToDoAvaloniaApp.DataModel.TaskPriority, confirmed by
        // reading its source), so this is the realistic case, not just the simplest one.
        [Display(Name = "High priority")]
        High,

        // EnumValueViewModel also has a DisplayNameAttribute branch (checked after Display, before
        // Description) - not exercised here: System.ComponentModel.DisplayNameAttribute's own
        // AttributeUsage does not include AttributeTargets.Field (confirmed by the compiler
        // rejecting [DisplayName] on an enum member outright, CS0592), so that branch can never
        // actually be reached from a real enum in compiled C# - dead code in the same sense as
        // this port's own FilterType.cs finding, just not worth a source change on its own.

        [Description("A low priority task")]
        Low
    }

    [Fact]
    public void EnumValueViewModel_NoAttributes_UsesEnumValueName()
    {
        var vm = new EnumValueViewModel(PlainEnum.First);

        Assert.Equal("First", vm.Name);
        Assert.Null(vm.Description);
        Assert.Equal(PlainEnum.First, vm.Value);
    }

    [Fact]
    public void EnumValueViewModel_NullValue_HasEmptyNameAndNullValue()
    {
        var vm = new EnumValueViewModel(null);

        Assert.Equal(string.Empty, vm.Name);
        Assert.Null(vm.Value);
    }

    [Fact]
    public void EnumValueViewModel_DisplayAttributeWithPlainName_UsesItAsCaption()
    {
        var vm = new EnumValueViewModel(DisplayEnum.High);

        Assert.Equal("High priority", vm.Name);
    }

    [Fact]
    public void EnumValueViewModel_DescriptionAttribute_UsesItAsName()
    {
        var vm = new EnumValueViewModel(DisplayEnum.Low);

        Assert.Equal("A low priority task", vm.Name);
    }

    [Fact]
    public void EnumValueViewModel_ToString_ReturnsName()
    {
        var vm = new EnumValueViewModel(PlainEnum.First);

        Assert.Equal("First", vm.ToString());
    }

    [Fact]
    public void GetListFromType_ReturnsOneEntryPerEnumValue()
    {
        var list = EnumToCollectionConverter.GetListFromType(typeof(PlainEnum), withNullable: false);

        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "None", "First" }, list.Select(v => v.Name));
    }

    [Fact]
    public void GetListFromType_WithNullable_InsertsLeadingEmptyEntry()
    {
        var list = EnumToCollectionConverter.GetListFromType(typeof(PlainEnum), withNullable: true);

        Assert.Equal(3, list.Count);
        Assert.Null(list[0].Value);
        Assert.Equal(string.Empty, list[0].Name);
        Assert.Equal(PlainEnum.None, list[1].Value);
    }

    [Fact]
    public void GetListFromType_NonEnumType_Throws()
    {
        Assert.Throws<ArgumentException>(() => EnumToCollectionConverter.GetListFromType(typeof(string), false));
    }

    [Fact]
    public void Convert_CachedConstructor_AlwaysReturnsTheCachedList()
    {
        var converter = new EnumToCollectionConverter(typeof(PlainEnum));

        var result = converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);

        var list = Assert.IsAssignableFrom<List<EnumValueViewModel>>(result);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Convert_CachedConstructor_NullableBaseType_IncludesEmptyEntry()
    {
        var converter = new EnumToCollectionConverter(typeof(PlainEnum?));

        var result = converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);

        var list = Assert.IsAssignableFrom<List<EnumValueViewModel>>(result);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Convert_UncachedConstructor_ResolvesEnumTypeFromValue()
    {
        var converter = new EnumToCollectionConverter();

        var result = converter.Convert(PlainEnum.First, typeof(object), null, CultureInfo.InvariantCulture);

        var list = Assert.IsAssignableFrom<List<EnumValueViewModel>>(result);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Convert_UncachedConstructor_NullValue_ReturnsNull()
    {
        var converter = new EnumToCollectionConverter();

        Assert.Null(converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_AlwaysReturnsNull()
    {
        var converter = new EnumToCollectionConverter(typeof(PlainEnum));

        Assert.Null(converter.ConvertBack("anything", typeof(PlainEnum), null, CultureInfo.InvariantCulture));
    }
}
