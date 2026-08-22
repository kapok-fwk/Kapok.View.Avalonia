using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Avalonia.Data.Converters;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// One selectable value of an enum, with its localized display name. Direct port of
/// Kapok.View.Wpf's EnumValueViewModel - it is plain reflection over DataAnnotations attributes,
/// with no WPF API in it at all; only the empty INotifyPropertyChanged implementation (a WPF
/// memory-leak workaround for its binding engine keeping strong references to sources) is kept
/// because Avalonia's binding engine looks for the same interface and the same argument applies.
/// </summary>
public class EnumValueViewModel : INotifyPropertyChanged
{
    public EnumValueViewModel(Enum? enumValue)
    {
        Value = enumValue;

        if (enumValue == null)
        {
            Name = string.Empty;
            return;
        }

        var fieldInfo = enumValue.GetType().GetField(enumValue.ToString());
        if (fieldInfo == null)
        {
            Name = enumValue.ToString();
            return;
        }

        if (fieldInfo.GetCustomAttributes(typeof(DisplayAttribute), false).SingleOrDefault() is DisplayAttribute displayAttribute)
        {
            var resourceManager =
                (System.Resources.ResourceManager?)displayAttribute.ResourceType?
                    .GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static)?.GetMethod?
                    .Invoke(null, null);

            Name = !string.IsNullOrEmpty(displayAttribute.Name)
                ? resourceManager?.GetString(displayAttribute.Name) ?? displayAttribute.Name
                : enumValue.ToString();

            if (!string.IsNullOrEmpty(displayAttribute.Description))
                Description = resourceManager?.GetString(displayAttribute.Description) ?? displayAttribute.Description;
        }
        else if (fieldInfo.GetCustomAttributes(typeof(DisplayNameAttribute), false).SingleOrDefault() is DisplayNameAttribute displayNameAttribute)
        {
            Name = displayNameAttribute.DisplayName;
        }
        else if (fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false).SingleOrDefault() is DescriptionAttribute descriptionAttribute)
        {
            Name = descriptionAttribute.Description;
        }
        else
        {
            Name = enumValue.ToString();
        }
    }

    /// <summary>
    /// Value of the enum value.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Display name of the enum value.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Additional description of the enum value.
    /// </summary>
    public string? Description { get; }

    #region INotifyPropertyChanged

    /// <summary>
    /// Implemented as a no-op to avoid a memory leak when binding to the properties (they are
    /// immutable, so there is never anything to notify about).
    /// </summary>
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add { }
        remove { }
    }

    #endregion

    public override string ToString() => Name;
}

/// <summary>
/// Turns an enum type (or a value of one) into the list of its selectable
/// <see cref="EnumValueViewModel"/>s. Direct port of Kapok.View.Wpf's EnumToCollectionConverter,
/// minus its MarkupExtension base class - every consumer in this port constructs it from C# code
/// (see CustomDataGrid's enum column generation), and Avalonia's markup-extension model is a
/// different shape anyway.
/// </summary>
public class EnumToCollectionConverter : IValueConverter
{
    private readonly List<EnumValueViewModel>? _cachedEnumValueList;

    /// <summary>
    /// Generic version resolving the enum type from the converted value at runtime. Does not
    /// support nullable values (there is no type to read the underlying enum from when the value
    /// is null).
    /// </summary>
    public EnumToCollectionConverter()
    {
    }

    /// <summary>
    /// Caching version for a statically-known property type, including <c>Nullable&lt;TEnum&gt;</c>
    /// (which gets a leading empty entry so the value can be cleared).
    /// </summary>
    public EnumToCollectionConverter(Type baseType)
    {
        var withNullable = false;

        if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            withNullable = true;
            baseType = baseType.GenericTypeArguments[0];
        }

        _cachedEnumValueList = GetListFromType(baseType, withNullable);
    }

    public static List<EnumValueViewModel> GetListFromType(Type type, bool withNullable)
    {
        if (!type.IsEnum)
            throw new ArgumentException($"The parameter {nameof(type)} must be an enum type.");

        var list = Enum.GetValues(type)
            .Cast<Enum>()
            .Select(e => new EnumValueViewModel(e))
            .ToList();

        if (withNullable)
            list.Insert(0, new EnumValueViewModel(null));

        return list;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (_cachedEnumValueList != null)
            return _cachedEnumValueList;

        if (value == null)
            return null;

        Type type;
        if (value is Enum)
            type = value.GetType();
        else if (value is Type typeValue)
            type = typeValue;
        else
            return null;

        return GetListFromType(type, false);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
