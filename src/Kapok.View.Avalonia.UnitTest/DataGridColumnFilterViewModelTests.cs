using Kapok.BusinessLayer;
using Kapok.View.Avalonia.Controls;
using Xunit;

namespace Kapok.View.Avalonia.UnitTest;

/// <summary>
/// Tests DataGridColumnFilterViewModel against a real Kapok.BusinessLayer
/// PropertyFilterCollection&lt;T&gt; (not a mock - the class takes the collection interface
/// directly specifically to make this possible, see its own header comment). Covers the two real
/// bugs the porting plan's Phase 4 audit table records as found in the WPF original while porting
/// this class: (1) a non-string filter (e.g. one set programmatically) never made WPF's filter box
/// redisplay it - "the box stayed empty while the filter was genuinely active"; (2) WPF's
/// UpdateFilter created a non-generic PropertyFilterStringFilter when *adding* a filter but the
/// generic one when *replacing*, so a filter the user had typed could never afterwards be replaced.
/// </summary>
public class DataGridColumnFilterViewModelTests
{
    private enum TestPriority
    {
        Normal,
        High
    }

    private class TestEntry
    {
        public string Name { get; set; } = string.Empty;
        public decimal EstimatedTime { get; set; }
        public TestPriority Priority { get; set; }
    }

    private static (PropertyFilterCollection<TestEntry> Filter, DataGridColumnFilterViewModel Vm) CreateNameFilter()
    {
        var filter = new PropertyFilterCollection<TestEntry>();
        var vm = new DataGridColumnFilterViewModel(filter, typeof(TestEntry), nameof(TestEntry.Name));
        return (filter, vm);
    }

    [Fact]
    public void NewViewModel_NoExistingFilter_StartsEmptyAndNotReadOnly()
    {
        var (_, vm) = CreateNameFilter();

        Assert.Equal(string.Empty, vm.QueryString);
        Assert.False(vm.IsReadOnly);
    }

    [Fact]
    public void UpdateFilter_EmptyQueryStringAndNoExistingFilter_IsANoOp()
    {
        var (filter, vm) = CreateNameFilter();

        vm.UpdateFilter();

        Assert.Empty(filter.Properties);
    }

    [Fact]
    public void UpdateFilter_NewQueryString_AddsAGenericStringFilter()
    {
        var (filter, vm) = CreateNameFilter();

        vm.QueryString = "Buy*";
        vm.UpdateFilter();

        var added = Assert.Single(filter.Properties);
        var stringFilter = Assert.IsType<PropertyFilterStringFilter<TestEntry>>(added);
        Assert.Equal("Buy*", stringFilter.FilterString);
    }

    [Fact]
    public void UpdateFilter_CalledAgainWithNewText_MutatesTheSameFilterInstance()
    {
        var (filter, vm) = CreateNameFilter();
        vm.QueryString = "Buy*";
        vm.UpdateFilter();
        var firstFilter = Assert.Single(filter.Properties);

        vm.QueryString = "Bread*";
        vm.UpdateFilter();

        var stillOnlyFilter = Assert.Single(filter.Properties);
        Assert.Same(firstFilter, stillOnlyFilter);
        Assert.Equal("Bread*", ((PropertyFilterStringFilter<TestEntry>)stillOnlyFilter).FilterString);
    }

    [Fact]
    public void UpdateFilter_ClearingQueryString_RemovesTheFilter()
    {
        var (filter, vm) = CreateNameFilter();
        vm.QueryString = "Buy*";
        vm.UpdateFilter();

        vm.QueryString = string.Empty;
        vm.UpdateFilter();

        Assert.Empty(filter.Properties);
    }

    [Fact]
    public void NonStringFilterAddedProgrammatically_IsShownInQueryStringNotLeftBlank()
    {
        // Bug 1: WPF only refreshed the displayed query string for IPropertyFilterStringFilter
        // filters, so a PropertyStaticFilter set from application code (exactly what
        // ToDoAvaloniaApp's own FILTER scenario exercises via a Priority static filter) was active
        // but invisible in the box.
        var (filter, vm) = CreateNameFilter();

        filter.Properties.Add(new PropertyStaticFilter<TestEntry>(nameof(TestEntry.Name)) { FilterValue = "Bar" });

        Assert.Equal("Bar", vm.QueryString);
        Assert.False(vm.IsReadOnly);
    }

    [Fact]
    public void NonStringFilter_ThatCannotRoundTripToAFilterString_MakesTheQueryStringReadOnly()
    {
        // A string containing a double quote can't round-trip through AsFilterString (no escaping
        // support - see PropertyFilterExtension.AsFilterString / the filter-string grammar), so
        // this is a real "cannot be shown as editable text" case, not a contrived one.
        var (filter, vm) = CreateNameFilter();

        filter.Properties.Add(new PropertyStaticFilter<TestEntry>(nameof(TestEntry.Name)) { FilterValue = "has \"quote\"" });

        Assert.True(vm.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => vm.QueryString = "anything");
    }

    [Fact]
    public void TypingOverANonStringFilter_ReplacesItWithAGenericStringFilter()
    {
        // Bug 2: WPF's UpdateFilter used the non-generic PropertyFilterStringFilter when adding
        // but the generic PropertyFilterStringFilter<T> when replacing via
        // IFilterCollection.ReplacePropertyFilter - PropertyFilterCollection<T>.ReplacePropertyFilter
        // casts both filters to IPropertyFilter<T>, which the non-generic class doesn't implement,
        // so a filter added this way could never later be replaced again. Both paths use the
        // generic form here - this proves the *replace* path (typing over an existing
        // PropertyStaticFilter) produces a filter of the same generic type that would itself later
        // support being replaced again.
        var (filter, vm) = CreateNameFilter();
        filter.Properties.Add(new PropertyStaticFilter<TestEntry>(nameof(TestEntry.Name)) { FilterValue = "Bar" });

        vm.QueryString = "Baz*";
        vm.UpdateFilter();

        var replaced = Assert.Single(filter.Properties);
        var stringFilter = Assert.IsType<PropertyFilterStringFilter<TestEntry>>(replaced);
        Assert.Equal("Baz*", stringFilter.FilterString);
    }

    [Fact]
    public void ReplacingAFilterTwice_DoesNotThrow()
    {
        // Regression coverage for bug 2 from the other direction: replace once (static -> string),
        // then replace again (string -> string) - the second replace is exactly the call that threw
        // InvalidCastException in WPF's version, because the *first* replace there had produced a
        // non-generic filter that a second ReplacePropertyFilter call couldn't cast back to
        // IPropertyFilter<T>.
        var (filter, vm) = CreateNameFilter();
        filter.Properties.Add(new PropertyStaticFilter<TestEntry>(nameof(TestEntry.Name)) { FilterValue = "Bar" });
        vm.QueryString = "Baz*";
        vm.UpdateFilter();

        vm.QueryString = "Qux*";
        var exception = Record.Exception(() => vm.UpdateFilter());

        Assert.Null(exception);
        var replaced = Assert.Single(filter.Properties);
        Assert.Equal("Qux*", ((PropertyFilterStringFilter<TestEntry>)replaced).FilterString);
    }

    [Fact]
    public void InvalidFilterExpression_SetsHasErrorsOnTheViewModel()
    {
        var filter = new PropertyFilterCollection<TestEntry>();
        var vm = new DataGridColumnFilterViewModel(filter, typeof(TestEntry), nameof(TestEntry.EstimatedTime));

        vm.QueryString = "not-a-number";
        vm.UpdateFilter();

        Assert.True(vm.HasErrors);
        Assert.NotEmpty(vm.GetErrors(nameof(DataGridColumnFilterViewModel.QueryString)).Cast<object>());
    }

    [Fact]
    public void ClearingAnInvalidFilter_ClearsTheErrorState()
    {
        var filter = new PropertyFilterCollection<TestEntry>();
        var vm = new DataGridColumnFilterViewModel(filter, typeof(TestEntry), nameof(TestEntry.EstimatedTime));
        vm.QueryString = "not-a-number";
        vm.UpdateFilter();
        Assert.True(vm.HasErrors);

        vm.QueryString = string.Empty;
        vm.UpdateFilter();

        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Detach_StopsReactingToFurtherCollectionChanges()
    {
        var (filter, vm) = CreateNameFilter();

        vm.Detach();
        filter.Properties.Add(new PropertyStaticFilter<TestEntry>(nameof(TestEntry.Name)) { FilterValue = "Bar" });

        Assert.Equal(string.Empty, vm.QueryString);
    }
}
