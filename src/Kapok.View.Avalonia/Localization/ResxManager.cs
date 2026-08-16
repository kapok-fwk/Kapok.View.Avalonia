using System.Globalization;
using System.Resources;

namespace Kapok.View.Avalonia.Localization;

/// <summary>
/// Minimal Avalonia equivalent of Kapok.View.Wpf's Infralution.Localization.Wpf ResxExtension -
/// looks up a resx string by base name + key for the current UI culture, via the same
/// ResourceManager/satellite-assembly mechanism .NET resx localization always uses.
///
/// ResxExtension itself (Kapok.View.Wpf/src/Infralution.Localization.Wpf/ResxExtension.cs) is a
/// ~1100-line WPF markup extension built around live culture-change notification (a static
/// per-instance target registry refreshed via the never-called ResxExtension.UpdateAllTargets()),
/// MultiBinding/child-Resx composition, and Visual-Studio-designer-only satellite-assembly
/// probing - confirmed by reading its source and cross-checking every .xaml usage in
/// Kapok.View.Wpf that none of this is actually exercised: nothing calls UpdateAllTargets(), no
/// XAML uses the MultiBinding/child-element form, and culture is fixed at process startup either
/// way (matching this port's own ViewDomain.Culture, set once in its constructor from
/// Thread.CurrentThread.CurrentUICulture and never updated - see ViewDomain.cs). What's actually
/// used, in exactly two views (Report/MimeTypeReportPage.cs, Report/ReportPage.cs) via a
/// generated `Res.SomeKey` static accessor, is "look up a resx string by key" - this class does
/// exactly that. No markup-extension wrapper is provided: every window in this port is built in
/// code, not XAML, so a XAML-only markup extension isn't the shape that fits here - a plain static
/// method is the direct equivalent of what `Res.SomeKey` already was.
/// </summary>
public static class ResxManager
{
    private static readonly Dictionary<string, ResourceManager> ResourceManagers = new();

    /// <summary>
    /// Returns the string for <paramref name="key"/> from the embedded resx <paramref name="resxName"/>
    /// (its fully-qualified logical resource name, e.g. "Kapok.View.Avalonia.Report.Resources.MimeTypeReportPage"),
    /// resolved for <paramref name="culture"/> (defaults to <see cref="CultureInfo.CurrentUICulture"/>,
    /// matching ResxExtension's own default).
    /// </summary>
    /// <remarks>
    /// Falls back to "#" + key when the resx or key can't be found - matches
    /// ResxExtension.GetDefaultValue's own fallback, so a missing key is visibly wrong in the
    /// running app instead of silently blank.
    /// </remarks>
    public static string GetString(string resxName, string key, CultureInfo? culture = null)
    {
        try
        {
            var manager = GetResourceManager(resxName);
            return manager.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? "#" + key;
        }
        catch (MissingManifestResourceException)
        {
            return "#" + key;
        }
    }

    private static ResourceManager GetResourceManager(string resxName)
    {
        if (!ResourceManagers.TryGetValue(resxName, out var manager))
        {
            manager = new ResourceManager(resxName, typeof(ResxManager).Assembly);
            ResourceManagers[resxName] = manager;
        }
        return manager;
    }
}
