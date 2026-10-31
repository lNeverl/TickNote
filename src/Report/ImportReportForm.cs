namespace SedentaryReminder.Report;

public sealed class ImportReportForm : WorkspacePageWindow
{
    public ImportReportForm(AppContext app)
        : base(new ImportReportPage(app), new Size(640, 520), new Size(560, 400)) { }
}
