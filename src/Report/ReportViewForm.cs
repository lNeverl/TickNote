namespace SedentaryReminder.Report;

public sealed class ReportViewForm : WorkspacePageWindow
{
    public ReportViewForm(AppContext app)
        : base(new ReportPage(app), new Size(800, 540), new Size(640, 400)) { }
}
