namespace SedentaryReminder.Report;

public sealed class ExportForm : WorkspacePageWindow
{
    public ExportForm(AppContext app)
        : base(new ExportPage(app), new Size(400, 240), new Size(360, 210)) { }
}
