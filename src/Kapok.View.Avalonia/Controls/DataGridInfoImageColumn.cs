using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Kapok.View.Avalonia.ValueConverter;

namespace Kapok.View.Avalonia.Controls;

/// <summary>
/// A read-only column for an <c>[InfoImages]</c> property (see
/// <c>Kapok.Entity.InfoImagesAttribute</c>) - an <c>ObservableCollection&lt;string&gt;</c> of image
/// names, rendered as a horizontal row of small images. Port of Kapok.View.Wpf's
/// DataGridInfoImageColumn (an <c>ItemsControl</c> with a horizontal <c>StackPanel</c> items panel
/// and a per-item <c>Image</c> template).
///
/// WPF's per-item binding was a plain <c>new Binding()</c> (bind the item itself) that inherited
/// whatever <c>Converter</c> the *column's own* binding happened to carry - i.e. the caller decided
/// how a name became an image. This port has no equivalent "inherit the caller's converter" hook
/// (the column is built directly from <see cref="ColumnPropertyView"/> metadata, not from an
/// existing <c>Binding</c> object), so each item name is resolved through
/// <see cref="ImageNameToImageSourceConverter"/> - the one name-&gt;image lookup this port already
/// has (also used for Ribbon action icons), with the small-image size. A property using this
/// attribute is therefore expected to hold names from that same lookup table
/// (<c>Kapok.View.ImageManager.GetImageResource</c>'s known names, e.g. "account-book"), not
/// arbitrary file paths.
/// </summary>
public class DataGridInfoImageColumn : DataGridTemplateColumn
{
    /// <summary>Property path of the <c>ObservableCollection&lt;string&gt;</c> of image names.</summary>
    public string PropertyPath { get; set; } = string.Empty;

    public void BuildTemplates()
    {
        IsReadOnly = true;
        ClipboardContentBinding = new Binding(PropertyPath);

        CellTemplate = new FuncDataTemplate<object>((_, _) =>
        {
            var itemsControl = new ItemsControl
            {
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
                ItemTemplate = new FuncDataTemplate<object>((itemValue, _) =>
                {
                    var image = new Image { MaxHeight = 20, Margin = new Thickness(0, 0, 3, 0) };
                    image.Bind(Image.SourceProperty, new Binding
                    {
                        Source = itemValue,
                        Converter = new ImageNameToImageSourceConverter(),
                        ConverterParameter = "Small"
                    });
                    return image;
                })
            };
            itemsControl.Bind(ItemsControl.ItemsSourceProperty, new Binding(PropertyPath) { Mode = BindingMode.OneWay });
            return itemsControl;
        }, supportsRecycling: false);
    }
}
