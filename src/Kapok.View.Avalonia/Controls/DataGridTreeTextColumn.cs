using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// A text column that renders a hierarchy tree: per-level indentation, connector lines, and an
/// expand/collapse toggle on rows that have children. Port of Kapok.View.Wpf's
/// DataGridTreeTextColumn.
///
/// Drives itself off the three <c>IHierarchyEntry&lt;TEntry&gt;</c> members
/// (<c>Level</c>/<c>HasChildren</c>/<c>IsExpanded</c>) exactly as WPF's version does - the column
/// is generated for any <see cref="ColumnPropertyView"/> with
/// <see cref="ColumnPropertyView.ShowHierarchicalTree"/> set.
///
/// Differences from the WPF original:
///  - **The expander is built here rather than looked up as a resource.** WPF threw
///    <c>NotSupportedException</c> unless a <c>DataGridTreeTextColumn_ToggleButton</c> style was
///    registered in <c>Application.Current.Resources</c> (it lives in DataGridStyling.xaml), so the
///    column could not be used without also importing that dictionary. A self-contained toggle
///    removes that coupling - and is why this port needs nothing from DataGridStyling.xaml for it.
///  - WPF bound visibility through a BooleanToVisibilityConverter; Avalonia binds <c>IsVisible</c>
///    to a bool directly, so the converter is unnecessary (the same simplification the porting
///    plan's mapping table already predicted for every *ToVisibility converter).
/// </summary>
public class DataGridTreeTextColumn : DataGridTemplateColumn
{
    /// <summary>Property path of the text shown in the column.</summary>
    public string PropertyPath { get; set; } = string.Empty;

    /// <summary>Property path of <c>IHierarchyEntry&lt;T&gt;.Level</c>.</summary>
    public string LevelPath { get; set; } = nameof(IHierarchyLevels.Level);

    /// <summary>Property path of <c>IHierarchyEntry&lt;T&gt;.HasChildren</c>.</summary>
    public string HasChildrenPath { get; set; } = nameof(IHierarchyLevels.HasChildren);

    /// <summary>Property path of <c>IHierarchyEntry&lt;T&gt;.IsExpanded</c>.</summary>
    public string IsExpandedPath { get; set; } = nameof(IHierarchyLevels.IsExpanded);

    /// <summary>Optional string format applied to the text, from the column metadata.</summary>
    public string? StringFormat { get; set; }

    private static readonly IBrush LineBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));

    /// <summary>
    /// Chrome-less ToggleButton theme for the expander - see BuildExpanderToggle.
    /// </summary>
    private static readonly ControlTheme ExpanderToggleTheme = new(typeof(ToggleButton))
    {
        Setters =
        {
            new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToggleButton>((_, _) =>
                new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = new Binding(nameof(ContentControl.Content))
                        { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) },
                    Background = Brushes.Transparent,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                }))
        }
    };

    public void BuildTemplates(bool isReadOnly)
    {
        SortMemberPath = PropertyPath;
        ClipboardContentBinding = new Binding(PropertyPath);
        IsReadOnly = isReadOnly;

        CellTemplate = new FuncDataTemplate<object>((_, _) => BuildCell(editing: false), supportsRecycling: false);

        if (!isReadOnly)
            CellEditingTemplate = new FuncDataTemplate<object>((_, _) => BuildCell(editing: true), supportsRecycling: false);
    }

    private Control BuildCell(bool editing)
    {
        var levelToMargin = new DataGridHierarchyColumnLevelToMarginConverter();

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new(GridLength.Auto) { MinWidth = 19 },
                new(GridLength.Auto),
                new(new GridLength(1, GridUnitType.Star))
            },
            RowDefinitions = new RowDefinitions { new(GridLength.Auto), new(new GridLength(1, GridUnitType.Star)) }
        };

        // Horizontal connector line into the row's own node.
        var horizontalLine = new Rectangle { Height = 1, Fill = LineBrush, VerticalAlignment = VerticalAlignment.Top };
        horizontalLine.Bind(Layoutable.MarginProperty, new Binding(LevelPath)
        {
            Converter = levelToMargin,
            ConverterParameter = "HLine",
            FallbackValue = new Thickness(9, 1, 0, 0)
        });
        grid.Children.Add(horizontalLine);

        // Vertical connector line down the level's own column.
        var verticalLine = new Rectangle { Width = 1, Fill = LineBrush, HorizontalAlignment = HorizontalAlignment.Left };
        verticalLine.Bind(Layoutable.MarginProperty, new Binding(LevelPath)
        {
            Converter = levelToMargin,
            ConverterParameter = "VLine",
            FallbackValue = new Thickness(0)
        });
        Grid.SetRow(verticalLine, 0);
        Grid.SetRowSpan(verticalLine, 2);
        Grid.SetColumn(verticalLine, 0);
        grid.Children.Add(verticalLine);

        var toggleButton = BuildExpanderToggle();
        Grid.SetRow(toggleButton, 0);
        Grid.SetColumn(toggleButton, 0);
        toggleButton.Bind(ToggleButton.IsCheckedProperty, new Binding(IsExpandedPath) { Mode = BindingMode.TwoWay });
        toggleButton.Bind(Visual.IsVisibleProperty, new Binding(HasChildrenPath) { FallbackValue = false });
        toggleButton.Bind(Layoutable.MarginProperty, new Binding(LevelPath)
        {
            Converter = levelToMargin,
            ConverterParameter = "ToggleButton",
            FallbackValue = new Thickness(0, 1, 0, 0)
        });
        grid.Children.Add(toggleButton);

        Control text;
        if (editing)
        {
            var textBox = new TextBox { BorderThickness = new Thickness(0), MinHeight = 0, MinWidth = 0, Padding = new Thickness(1) };
            textBox.Bind(TextBox.TextProperty, new Binding(PropertyPath)
            {
                Mode = BindingMode.TwoWay,
                StringFormat = StringFormat,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            });
            text = textBox;
        }
        else
        {
            var textBlock = new TextBlock { Margin = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            textBlock.Bind(TextBlock.TextProperty, new Binding(PropertyPath)
            {
                Mode = BindingMode.OneWay,
                StringFormat = StringFormat
            });
            text = textBlock;
        }

        Grid.SetRow(text, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumnSpan(text, 2);
        grid.Children.Add(text);

        return grid;
    }

    /// <summary>
    /// The expand/collapse control: a borderless toggle whose glyph is a triangle pointing right
    /// when collapsed and down when checked, matching WPF's DataGridTreeTextColumn_ToggleButton
    /// style from DataGridStyling.xaml.
    /// </summary>
    private static ToggleButton BuildExpanderToggle()
    {
        var glyph = new global::Avalonia.Controls.Shapes.Path
        {
            Fill = Brushes.Gray,
            Width = 8,
            Height = 8,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = StreamGeometry.Parse("M 0,0 L 8,4 L 0,8 Z")
        };

        var toggleButton = new ToggleButton
        {
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = glyph,
            // A bare ControlTheme, not just Background/BorderThickness setters: the Fluent
            // ToggleButton theme paints its *checked* state with the accent brush, and since an
            // expanded node is a checked toggle, every expander rendered as a solid blue square
            // that hid the glyph completely (seen in a screenshot). Setting local property values
            // does not help - the theme's :checked style wins over them. Replacing the theme with a
            // plain content presenter is the supported way to get an unchromed toggle.
            Theme = ExpanderToggleTheme
        };

        // Rotate the glyph 90 degrees when expanded. A RotateTransform driven from IsChecked is the
        // Avalonia-idiomatic equivalent of WPF's checked-state template trigger.
        toggleButton.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                glyph.RenderTransform = new RotateTransform(toggleButton.IsChecked == true ? 90 : 0);
        };

        return toggleButton;
    }
}

/// <summary>
/// Member-name anchor for the <c>IHierarchyEntry&lt;TEntry&gt;</c> properties a tree column binds
/// to. <c>IHierarchyEntry&lt;TEntry&gt;</c> itself is generic and self-referencing
/// (<c>where TEntry : IHierarchyEntry&lt;TEntry&gt;</c>), so it cannot be named in a
/// <c>nameof</c> from non-generic code - this interface exists only so the default paths above are
/// compiler-checked names rather than string literals.
/// </summary>
internal interface IHierarchyLevels
{
    int Level { get; }
    bool HasChildren { get; }
    bool IsExpanded { get; }
}
