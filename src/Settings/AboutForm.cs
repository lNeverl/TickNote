namespace SedentaryReminder.Settings;

/// <summary>关于对话框（M5-7）：版本号、数据目录、打开数据目录。</summary>
public sealed class AboutForm : WorkspacePageWindow
{
    public AboutForm(string dataDir)
        : base(new AboutPage(dataDir), new Size(520, 290), new Size(420, 240)) { }
}
