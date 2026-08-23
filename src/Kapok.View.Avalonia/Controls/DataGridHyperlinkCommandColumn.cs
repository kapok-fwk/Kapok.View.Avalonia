using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// A read-only column whose cell text is a clickable link running a command - how Kapok renders a
/// property that carries a <c>DrillDownDefinition</c> (clicking it opens the referenced page,
/// filtered to the selected entries). Port of Kapok.View.Wpf's DataGridHyperlinkCommandColumn.
///
/// WPF built this from a <c>TextBlock</c> containing a <c>Hyperlink</c> inline, which is a
/// WPF-specific text-flow element with a <c>Command</c> property. Avalonia has no <c>Hyperlink</c>
/// inline, so the link is a <c>Button</c> styled as a link (the same shape Avalonia's own
/// HyperlinkButton takes) - it carries <c>Command</c>/<c>CommandParameter</c> natively, which is
/// the whole reason WPF reached for Hyperlink in the first place.
///
/// WPF also hard-set <c>TextAlignment = Right</c> here with a TODO saying it should depend on
/// whether the content is a number, because it could not get that information across from
/// CustomDataGrid. That information *is* available here (the generator knows the property type),
/// so the alignment is passed in instead - closing that TODO.
/// </summary>
public class DataGridHyperlinkCommandColumn : DataGridTemplateColumn
{
    /// <summary>Property path of the text shown in the cell.</summary>
    public string PropertyPath { get; set; } = string.Empty;

    /// <summary>Optional string format applied to the text, from the column metadata.</summary>
    public string? StringFormat { get; set; }

    /// <summary>The drill-down command (built from the DataSet's own drill-down action).</summary>
    public ICommand? Command { get; set; }

    /// <summary>
    /// The command parameter binding - the grid's current selection, matching WPF's
    /// <c>CommandParameterBinding</c> pointing at <c>CustomDataGrid.SelectedItems</c>.
    /// </summary>
    public BindingBase? CommandParameterBinding { get; set; }

    /// <summary>Whether the cell text is right aligned (true for numeric properties).</summary>
    public bool AlignRight { get; set; }

    public void BuildTemplates()
    {
        IsReadOnly = true;
        SortMemberPath = PropertyPath;
        ClipboardContentBinding = new Binding(PropertyPath);

        CellTemplate = new FuncDataTemplate<object>((_, _) =>
        {
            var textBlock = new TextBlock
            {
                Foreground = Brushes.RoyalBlue,
                TextDecorations = TextDecorations.Underline,
                VerticalAlignment = VerticalAlignment.Center
            };
            textBlock.Bind(TextBlock.TextProperty, new Binding(PropertyPath)
            {
                Mode = BindingMode.OneWay,
                StringFormat = StringFormat
            });

            var button = new Button
            {
                Content = textBlock,
                Command = Command,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                HorizontalAlignment = AlignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (CommandParameterBinding != null)
                button.Bind(Button.CommandParameterProperty, CommandParameterBinding);

            return button;
        }, supportsRecycling: false);
    }
}
