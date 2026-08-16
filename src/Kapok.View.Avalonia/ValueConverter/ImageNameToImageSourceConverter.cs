using System.Diagnostics;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kapok.View;

namespace Kapok.View.Avalonia.ValueConverter;

/// <summary>
/// Resolves a Kapok action's Image name (e.g. "save") to an Avalonia Bitmap, mirroring
/// Kapok.View.Wpf's ImageNameToImageSourceConverter. Reuses the same
/// <see cref="ImageManager.GetImageResource"/> name-to-filename lookup table (framework-agnostic,
/// lives in Kapok.View core) but resolves through Avalonia's avares:// asset scheme against this
/// module's own copy of the icon PNGs (see Resources/Icons and the csproj comment above it),
/// rather than WPF's pack:// URIs pointing at Kapok.View.Wpf's embedded resources.
/// </summary>
public class ImageNameToImageSourceConverter : IValueConverter, IMultiValueConverter
{
    private const string AssemblyAssetRoot = "avares://Kapok.View.Avalonia/Resources/Icons";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return null;

        var size = ParseSize(parameter);
        if (size == null)
        {
            Debug.WriteLine("ImageNameToImageSourceConverter: Parameter was not specified, no image source was created");
            return null;
        }

        return LoadBitmap(value.ToString()!, size.Value);
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 || values[0] == null || parameter == null)
            return null;

        var size = ParseSize(parameter);
        if (size == null)
        {
            Debug.WriteLine("ImageNameToImageSourceConverter: Parameter was not specified, no image source was created");
            return null;
        }

        // Matches WpfViewDomain's LargeImageSource MultiBinding: the second value (ImageIsBig)
        // suppresses the large image when the action explicitly opted out of one.
        if (size == ImageManager.ImageSize.Large && values.Count > 1 && values[1] is false)
            return null;

        return LoadBitmap(values[0]!.ToString()!, size.Value);
    }

    private static ImageManager.ImageSize? ParseSize(object parameter)
    {
        return parameter.ToString() switch
        {
            "Large" => ImageManager.ImageSize.Large,
            "Small" => ImageManager.ImageSize.Small,
            _ => null
        };
    }

    private static Bitmap? LoadBitmap(string name, ImageManager.ImageSize size)
    {
        // ImageManager.GetImageResource returns a WPF pack:// URI whose last path segment is the
        // file name we actually need (e.g. "save_small.png") - only the file name is reused here,
        // the scheme/host portion is WPF-specific and discarded.
        var resourceString = ImageManager.GetImageResource(name, size);
        var fileName = resourceString[(resourceString.LastIndexOf('/') + 1)..];
        var uri = new Uri($"{AssemblyAssetRoot}/{fileName}");

        if (!AssetLoader.Exists(uri))
        {
            Debug.WriteLine($"ImageNameToImageSourceConverter: no asset found for {uri}");
            return null;
        }

        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    #region ConvertBack (not supported)

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    #endregion
}
