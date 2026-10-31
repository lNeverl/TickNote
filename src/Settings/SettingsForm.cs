namespace SedentaryReminder.Settings;

public sealed class SettingsForm : WorkspacePageWindow
{
    public SettingsForm(AppContext app)
        : base(new SettingsPage(app), new Size(620, 560), new Size(560, 400)) { }
}
