using System.Drawing;
using Kapok.BusinessLayer;
using Kapok.Data;

namespace Kapok.View.Avalonia.Data;

/// <summary>
/// The colour hooks a view needs from a DataSet. Mirrors <c>IWpfDataSetView</c> - the entity-level
/// colouring feature itself (the <c>EntryColoring</c> event and
/// <see cref="DataSetEntityColoringEventArgs"/>) is framework-agnostic and lives in core
/// <c>Kapok.View</c>; only these four "ask the DataSet what colour this entity should be"
/// accessors are per-UI, because the core DataSetView keeps <c>RaiseEntryColoring</c> protected.
/// </summary>
public interface IAvaloniaDataSetView : IDataSetView
{
    Color? GetForegroundColorOfEntity(object entity, string? propertyName = null);
    Color? GetBackgroundColorOfEntity(object entity, string? propertyName = null);
    Color? GetForegroundSelectedColorOfEntity(object entity, string? propertyName = null);
    Color? GetBackgroundSelectedColorOfEntity(object entity, string? propertyName = null);
}

/// <summary>
/// The DataSet view this module hands out, replacing the plain core
/// <see cref="DataSetView{TEntry}"/> that <c>AvaloniaViewDomain.CreateDataSetView</c> returned
/// through Phase 6.
///
/// Phase 1 deliberately used the core class directly, noting "an Avalonia-specific subclass can be
/// introduced in the DataGrid phase if the eventual grid choice needs similar hooks". The
/// per-column filter row is that point: core <see cref="DataSetView{TEntry}"/> declares
/// <c>ToggleFilterVisibleAction</c> but leaves <c>CanToggleFilterVisible()</c> returning false and
/// <c>ToggleFilterVisible()</c> throwing <see cref="NotImplementedException"/> - it is the view
/// layer's job to say whether it can show an in-grid filter at all, and
/// <c>WpfDataSetView&lt;TEntry&gt;</c> overrides exactly these two members for the same reason.
/// Found by running it, not by reading: executing the action without this threw inside
/// <c>UIAction.Execute</c>, whose own catch routes to <c>ViewDomain.ShowErrorMessage</c> - a modal
/// dialog with a nested dispatcher frame, which in a headless run simply hangs forever.
///
/// This class deliberately stops there. WPF's <c>WpfDataSetView</c> additionally carries a
/// <c>CollectionViewSource</c>/<c>IEditableCollectionView</c> layer (the grid-native
/// {NewItemPlaceholder} inline add-row and client-side sort/group) that Avalonia's DataGrid has no
/// equivalent of and this port does not use, and a <c>FilterView</c>
/// (<c>FilterSetView&lt;TEntry&gt;</c>) that backs WPF's separate "edit filter" popup - which is
/// built on WPF's own <c>QueryableCollectionViewSource</c> and has no consumer in this port
/// (<c>FilterView</c> is declared nullable in the core contract precisely so a UI without that
/// popup can leave it unset). Both are left out rather than ported without a caller.
/// </summary>
public class AvaloniaDataSetView<TEntry> : DataSetView<TEntry>, IAvaloniaDataSetView
    where TEntry : class, new()
{
    public AvaloniaDataSetView(IServiceProvider serviceProvider, IDataDomainScope dataDomainScope,
        IEntityService<TEntry>? entityService = null)
        : base(serviceProvider, dataDomainScope, entityService)
    {
    }

    protected override bool CanToggleFilterVisible() => true;

    protected override void ToggleFilterVisible() => IsFilterVisible = !IsFilterVisible;

    #region Entity colouring

    private DataSetEntityColoringEventArgs? Ask(object entity, string? propertyName)
    {
        if (entity is not TEntry typedEntity)
            return null;

        var args = new DataSetEntityColoringEventArgs(typedEntity, propertyName);
        RaiseEntryColoring(args);
        return args;
    }

    public Color? GetForegroundColorOfEntity(object entity, string? propertyName = null)
        => Ask(entity, propertyName)?.ForegroundColor;

    public Color? GetBackgroundColorOfEntity(object entity, string? propertyName = null)
        => Ask(entity, propertyName)?.BackgroundColor;

    public Color? GetForegroundSelectedColorOfEntity(object entity, string? propertyName = null)
        => Ask(entity, propertyName)?.ForegroundSelectedColor;

    public Color? GetBackgroundSelectedColorOfEntity(object entity, string? propertyName = null)
        => Ask(entity, propertyName)?.BackgroundSelectedColor;

    #endregion
}
