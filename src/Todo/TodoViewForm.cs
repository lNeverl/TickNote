using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

public sealed class TodoViewForm : WorkspacePageWindow
{
    private readonly TodoPage _page;
    public TodoViewForm(Database db, AppConfig config, DateTime? defaultDate = null)
        : this(new TodoPage(db, config, defaultDate)) { }
    private TodoViewForm(TodoPage page) : base(page, new Size(760, 520), new Size(500, 400)) => _page = page;
    public DateTime SelectedDate => _page.SelectedDate;
    public TodoEntry? SelectedEntry => _page.SelectedEntry;
    public void Reload() => _page.Reload();
}
