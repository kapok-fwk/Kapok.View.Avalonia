using Avalonia;
using Avalonia.Controls;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// Port of Kapok.View.Wpf's DataPickerExtension.ShowNullWhenDateTimeMinValue attached property -
/// confirmed still a real consumer need (not dead code): DuckAccounting.View.Wpf sets it on the
/// DatePicker in its DonorAccountCardPageControl/ContactPersonCardPageControl XAML, for a nullable
/// date property whose "unset" sentinel on the entity is DateTime.MinValue rather than null.
///
/// Same trick as WPF's original: DatePicker's Popup opens on SelectedDate's month, so binding a
/// null date the moment MinValue is set would still open the popup on year 1 - setting a real date
/// first (then immediately back to null) makes the popup remember "now" instead.
/// </summary>
public static class DataPickerExtension
{
    public static readonly AttachedProperty<bool> ShowNullWhenDateTimeMinValueProperty =
        AvaloniaProperty.RegisterAttached<DatePicker, bool>(
            "ShowNullWhenDateTimeMinValue", typeof(DataPickerExtension));

    static DataPickerExtension()
    {
        ShowNullWhenDateTimeMinValueProperty.Changed.AddClassHandler<DatePicker>(OnShowNullWhenDateTimeMinValueChanged);
    }

    private static void OnShowNullWhenDateTimeMinValueChanged(DatePicker control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
            control.PropertyChanged += OnSelectedDateChanged;
        else
            control.PropertyChanged -= OnSelectedDateChanged;
    }

    private static void OnSelectedDateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != DatePicker.SelectedDateProperty)
            return;

        var control = (DatePicker)sender!;
        if (e.GetNewValue<DateTimeOffset?>() is { } selectedDate && selectedDate.DateTime == DateTime.MinValue)
        {
            control.SetCurrentValue(DatePicker.SelectedDateProperty, DateTimeOffset.Now);
            control.SetCurrentValue(DatePicker.SelectedDateProperty, null);
        }
    }

    public static void SetShowNullWhenDateTimeMinValue(DatePicker element, bool value)
        => element.SetValue(ShowNullWhenDateTimeMinValueProperty, value);

    public static bool GetShowNullWhenDateTimeMinValue(DatePicker element)
        => element.GetValue(ShowNullWhenDateTimeMinValueProperty);
}
