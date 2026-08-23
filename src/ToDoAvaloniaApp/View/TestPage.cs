using Kapok.Data;
using Kapok.View;
using Microsoft.Extensions.DependencyInjection;
using ToDoAvaloniaApp.DataModel;

namespace ToDoAvaloniaApp.View;

/// <summary>
/// Phase 8 item 7's showcase page for two previously-unexercised things at once: TestPage itself
/// (registered here since Phase 1, but never given a window constructor - see ToDoModule.cs -
/// so showing it always threw NotSupportedException("No Avalonia window defined for page")), and
/// InteractivePage.DetailPages -> DockPageWindow's ToolDock wiring (see DockPageWindow.cs's own
/// "not live-verified" comment), which needed a page that is both an InteractivePage (the only
/// kind that has DetailPages) and Dock-hosted (the only kind that renders a ToolDock) - a
/// combination nothing else in this app happens to be, since ListPage/CardPage (which do get
/// DockPageWindow) are DataPage, not InteractivePage.
/// </summary>
public class TestPage : InteractivePage
{
    public TestPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        Title = "Test page";

        var dataDomainScope = ServiceProvider.GetRequiredService<IDataDomain>().CreateScope();
        var detailDataSet = ViewDomain.CreateDataSetView<TaskList>(dataDomainScope);
        DetailPages.Add(new TestPageDetail(ServiceProvider, detailDataSet));
    }
}

/// <summary>
/// A minimal, real <see cref="DetailPage{TEntry}"/> - just enough to prove a detail page genuinely
/// reaches the screen as its own dockable tool pane, not to showcase detail-page content (which
/// Phase 4's native DataGrid baseline and every list/card page already cover).
/// </summary>
public class TestPageDetail : DetailPage<TaskList>
{
    public TestPageDetail(IServiceProvider serviceProvider, IDataSetView<TaskList> tableData)
        : base(serviceProvider, tableData)
    {
        Title = "Task lists (detail)";
    }
}
