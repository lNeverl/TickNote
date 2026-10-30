using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

/// <summary>从全部日期的待办中选择正在做的事，只读取事项，不修改其日期或完成状态。</summary>
public sealed class TodoPickerForm : DpiForm
{
    public TodoEntry? SelectedEntry { get; private set; }

    public TodoPickerForm(Database db)
    {
        Text = "从待办选择正在做的事";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 530);
        MinimumSize = new Size(500, 400);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "默认显示全部日期的未完成待办。选择后只填入提醒草稿，不会完成或修改待办。",
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 10),
        }, 0, 0);
        var browser = new TodoBrowser(db, dateScope: null, allowCompletion: false, showCompleted: false);
        root.Controls.Add(browser, 0, 1);
        var choose = UiLayout.Button("选择这件事", primary: true);
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
        root.Controls.Add(UiLayout.ButtonRow(true, cancel, choose), 0, 2);
        Controls.Add(root);
        AcceptButton = choose;
        CancelButton = cancel;
    }
}
