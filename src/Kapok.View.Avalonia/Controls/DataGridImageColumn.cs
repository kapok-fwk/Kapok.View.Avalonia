using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// A read-only column for a <c>[BinaryImage]</c> <c>byte[]</c> property (see
/// <c>Kapok.Entity.BinaryImageAttribute</c>) - renders the bytes as an actual image, via
/// <see cref="BinaryImageConverter"/>. Port of Kapok.View.Wpf's DataGridImageColumn.
///
/// WPF's version left editing as an open TODO ("needs somehow a way to edit it... a context menu").
/// Not picked up here either - still nothing to build on for it, and out of scope for this column's
/// own item.
/// </summary>
public class DataGridImageColumn : DataGridTemplateColumn
{
    /// <summary>Property path of the <c>byte[]</c> image data.</summary>
    public string PropertyPath { get; set; } = string.Empty;

    public void BuildTemplates()
    {
        IsReadOnly = true;
        ClipboardContentBinding = new Binding(PropertyPath);

        CellTemplate = new FuncDataTemplate<object>((_, _) =>
        {
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                MaxHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            image.Bind(Image.SourceProperty, new Binding(PropertyPath)
            {
                Mode = BindingMode.OneWay,
                Converter = new BinaryImageConverter()
            });
            return image;
        }, supportsRecycling: false);
    }
}
