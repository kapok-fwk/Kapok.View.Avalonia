using Kapok.View;

namespace ToDoAvaloniaApp.View;

public class TestPage : InteractivePage
{
    public TestPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        Title = "Test page";
    }
}
