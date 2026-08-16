using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Kapok.View.Avalonia.Controls;
using Kapok.View.Avalonia.Data;
using ToDoAvaloniaApp.DataModel;
using Task = ToDoAvaloniaApp.DataModel.Task;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// Hand-built control for TaskCard, registered via AvaloniaViewDomain.RegisterPageControlType in
/// ToDoModule.cs - matches the porting plan's note that a generic ICardPage control isn't built
/// yet (see Kapok.View.Avalonia.DefaultPageControls.CardPageView's own doc comment); real card
/// pages are expected to supply their own, same as WPF.
///
/// The real point of this class for Phase 5: it exercises LookupComboBox/CustomComboBox with a
/// real bound TaskListId lookup, not just a compiled-but-unused control.
/// </summary>
public class TaskCardView : UserControl
{
    private readonly LookupComboBox _taskListCombo;

    public TaskCardView()
    {
        var nameBox = new TextBox();
        nameBox.Bind(TextBox.TextProperty, new Binding("DataSet.Current.Name") { Mode = BindingMode.TwoWay });

        var descriptionBox = new TextBox { AcceptsReturn = true, Height = 60, TextWrapping = TextWrapping.Wrap };
        descriptionBox.Bind(TextBox.TextProperty, new Binding("DataSet.Current.Description") { Mode = BindingMode.TwoWay });

        // SelectedValueBinding tells the combo how to turn a selected TaskList item into the
        // value written back to Task.TaskListId (Guid?) - the Avalonia equivalent of WPF
        // ComboBox's SelectedValuePath.
        _taskListCombo = new LookupComboBox
        {
            SelectedValueBinding = new Binding(nameof(TaskList.Id)),
            ItemTemplate = new FuncDataTemplate<object>((item, _) => new TextBlock
            {
                Text = (item as TaskList)?.Name ?? item?.ToString(),
                VerticalAlignment = VerticalAlignment.Center
            })
        };
        _taskListCombo.Bind(SelectingItemsControl.SelectedValueProperty,
            new Binding("DataSet.Current.TaskListId") { Mode = BindingMode.TwoWay });

        var grid = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };

        void AddRow(int row, string label, Control input)
        {
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            Grid.SetRow(input, row);
            Grid.SetColumn(input, 1);
            input.Margin = new Thickness(0, 0, 0, 8);
            grid.Children.Add(text);
            grid.Children.Add(input);
        }

        AddRow(0, "Name", nameBox);
        AddRow(1, "Description", descriptionBox);
        AddRow(2, "Task list", _taskListCombo);

        Content = grid;

        AttachedToVisualTree += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        if (DataContext is not TaskCard taskCard)
            return;

        // AvaloniaPropertyLookupView.GetItems() (not part of the IPropertyLookupView contract -
        // see its own doc comment) enforces the lazy refresh on first access, so the DataGrid
        // inside LookupComboBox's dropdown gets real, already-queried TaskList rows the moment
        // it's assigned, not an empty collection that fills in later (see LookupComboBox's own
        // comment on why that ordering matters for Avalonia.Controls.DataGrid's
        // auto-column-generation).
        if (taskCard.PropertyViewDefinitions.LookupViews.TryGetValue(nameof(Task.TaskListId), out var lookupView) &&
            lookupView is AvaloniaPropertyLookupView avaloniaLookupView)
        {
            _taskListCombo.ItemsSource = avaloniaLookupView.GetItems();
        }
    }
}
