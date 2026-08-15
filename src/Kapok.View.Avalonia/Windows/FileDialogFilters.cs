using Avalonia.Platform.Storage;

namespace Kapok.View.Avalonia.Windows;

/// <summary>
/// Parses the WPF-style "Name|*.ext|Name2|*.ext2" filter strings used throughout Kapok.View
/// (e.g. ListPage&lt;TEntry&gt;.ExportAsExcelSheet's "Excel sheet (*.xlsx)|*.xlsx|All files|*")
/// into Avalonia's FilePickerFileType list, so callers don't need to change their fileMask strings.
/// </summary>
internal static class FileDialogFilters
{
    public static List<FilePickerFileType> Parse(string fileMask)
    {
        var result = new List<FilePickerFileType>();
        var parts = fileMask.Split('|');

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i];
            var patterns = parts[i + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            result.Add(new FilePickerFileType(name) { Patterns = patterns });
        }

        return result.Count > 0 ? result : new List<FilePickerFileType> { FilePickerFileTypes.All };
    }
}
