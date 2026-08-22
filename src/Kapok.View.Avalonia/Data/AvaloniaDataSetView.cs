using Kapok.BusinessLayer;
using Kapok.Data;

namespace Kapok.View.Avalonia.Data;

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
public class AvaloniaDataSetView<TEntry> : DataSetView<TEntry>
    where TEntry : class, new()
{
    public AvaloniaDataSetView(IServiceProvider serviceProvider, IDataDomainScope dataDomainScope,
        IEntityService<TEntry>? entityService = null)
        : base(serviceProvider, dataDomainScope, entityService)
    {
    }

    protected override bool CanToggleFilterVisible() => true;

    protected override void ToggleFilterVisible() => IsFilterVisible = !IsFilterVisible;
}
