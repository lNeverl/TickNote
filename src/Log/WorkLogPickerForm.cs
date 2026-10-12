using SedentaryReminder.Data;

namespace SedentaryReminder.Log;

public sealed class WorkLogPickerForm : DpiForm
{
    public WorkLogEntry? SelectedEntry { get; private set; }

    public WorkLogPickerForm(Database db)
    {
        Text = "从全部历史记录选择事项";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 530);
        MinimumSize = new Size(500, 360);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var browser = new WorkLogBrowser(db);
        var choose = UiLayout.Button("继续这件事", primary: true);
        choose.Enabled = false;
        void SelectEntry()
        {
            if (browser.SelectedEntry == null) return;
            SelectedEntry = browser.SelectedEntry;
            DialogResult = DialogResult.OK;
        }
        choose.Click += (_, _) => SelectEntry();
        browser.EntryActivated += SelectEntry;
        browser.SelectionChanged += () => choose.Enabled = browser.SelectedEntry != null;
        var cancel = UiLayout.Button("取消");
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = UiLayout.ButtonRow(true, cancel, choose);
        root.Controls.Add(browser, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = choose;
        CancelButton = cancel;
    }
}
