using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;

namespace Kapok.View.Avalonia.Report;

/// <summary>
/// Avalonia equivalent of Kapok.View.Wpf's ReportParameterList.xaml + ReportParameterTemplateSelector:
/// one row (label + a type-appropriate editor) per ReportParameterViewModel. WPF's version picks
/// between five XAML-resource DataTemplates (TextBox/CheckBox/BooleanNullable/DatePicker/ComboBox)
/// via a DataTemplateSelector; this builds the same five editor shapes directly in code (matching
/// how every other control in this project is built), via a plain IDataTemplate switching on
/// ReportParameter.DataType. The "editable combo with proposal values" branch WPF's selector left
/// commented out (ComboBoxEditableTemplate, never actually wired) is not ported - dead code there
/// too, not just here.
/// </summary>
public class ReportParameterList : UserControl
{
    public static readonly StyledProperty<Collection<ReportParameterViewModel>?> ParameterListProperty =
        AvaloniaProperty.Register<ReportParameterList, Collection<ReportParameterViewModel>?>(nameof(ParameterList));

    public Collection<ReportParameterViewModel>? ParameterList
    {
        get => GetValue(ParameterListProperty);
        set => SetValue(ParameterListProperty, value);
    }

    public ReportParameterList()
    {
        var itemsControl = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<ReportParameterViewModel>((vm, _) => BuildParameterRow(vm))
        };
        itemsControl.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(ParameterList)) { Source = this });

        Content = itemsControl;
    }

    private static Control BuildParameterRow(ReportParameterViewModel? vm)
    {
        if (vm == null)
            return new TextBlock();

        var label = new TextBlock
        {
            Text = vm.ReportParameter.Caption?.LanguageOrDefault(System.Globalization.CultureInfo.CurrentUICulture) ?? vm.ReportParameter.Name,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4)
        };

        var editor = BuildEditor(vm);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(label);
        grid.Children.Add(editor);

        return grid;
    }

    private static Control BuildEditor(ReportParameterViewModel vm)
    {
        if (vm.ProposalValues is { Count: > 0 })
        {
            var comboBox = new ComboBox { ItemsSource = vm.ProposalValues };
            comboBox.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding(nameof(ReportParameterViewModel.Value)) { Source = vm, Mode = BindingMode.TwoWay });
            return comboBox;
        }

        if (vm.ReportParameter.DataType == typeof(bool) || vm.ReportParameter.DataType == typeof(bool?))
        {
            var checkBox = new CheckBox { IsThreeState = vm.ReportParameter.DataType == typeof(bool?) };
            checkBox.Bind(ToggleButton.IsCheckedProperty,
                new Binding(nameof(ReportParameterViewModel.Value)) { Source = vm, Mode = BindingMode.TwoWay });
            return checkBox;
        }

        if (vm.ReportParameter.DataType == typeof(DateTime))
        {
            var datePicker = new DatePicker();
            datePicker.Bind(DatePicker.SelectedDateProperty,
                new Binding(nameof(ReportParameterViewModel.Value)) { Source = vm, Mode = BindingMode.TwoWay });
            return datePicker;
        }

        // string, Guid, TimeSpan, and all numeric types - matches WPF's TextBoxTemplate fallback
        var textBox = new TextBox();
        textBox.Bind(TextBox.TextProperty,
            new Binding(nameof(ReportParameterViewModel.Value)) { Source = vm, Mode = BindingMode.TwoWay });
        return textBox;
    }
}
