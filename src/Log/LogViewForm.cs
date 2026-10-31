namespace SedentaryReminder.Log;

public sealed class LogViewForm : WorkspacePageWindow
{
    public LogViewForm(AppContext app, DateTime? fixedDate)
        : base(new LogPage(app, fixedDate), new Size(800, 540), new Size(500, 360)) { }
}
