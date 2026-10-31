using SedentaryReminder.Data;

namespace SedentaryReminder.Log;

/// <summary>提醒中的临时流水编辑器；主窗口的补记入口使用同一份页面控件。</summary>
public sealed class ManualEntryForm : WorkspacePageWindow
{
    public ManualEntryForm(Database db, AppConfig config, WorkLogEntry? editing = null, DateTime? defaultDate = null)
        : this(new ManualEntryPage(db, config, editing, defaultDate, dialogMode: true)) { }

    private ManualEntryForm(ManualEntryPage page) : base(page, new Size(540, 380), new Size(500, 360))
    {
        page.Saved += () => { DialogResult = DialogResult.OK; Close(); };
        page.CancelRequested += () => { DialogResult = DialogResult.Cancel; Close(); };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }
}
