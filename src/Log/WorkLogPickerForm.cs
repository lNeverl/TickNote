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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var browser = new WorkLogBrowser(db);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var choose = new Button { Text = "继续这件事", AutoSize = true, Enabled = false };
        void SelectEntry()
        {
            if (browser.SelectedEntry == null) return;
            SelectedEntry = browser.SelectedEntry;
            DialogResult = DialogResult.OK;
        }
        choose.Click += (_, _) => SelectEntry();
        browser.EntryActivated += SelectEntry;
        browser.SelectionChanged += () => choose.Enabled = browser.SelectedEntry != null;
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(choose);
        buttons.Controls.Add(cancel);
        root.Controls.Add(browser, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = choose;
        CancelButton = cancel;
    }
}
