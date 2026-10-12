using SedentaryReminder.Data;

namespace SedentaryReminder.Log;

/// <summary>今日流水固定日期，历史流水默认查看全部；编辑、删除、补录均即时刷新。</summary>
public sealed class LogViewForm : DpiForm
{
    private readonly AppContext _app;
    private readonly WorkLogBrowser _browser;

    public LogViewForm(AppContext app, DateTime? fixedDate)
    {
        _app = app;
        Text = fixedDate is DateTime date ? $"工作流水 - {date:yyyy-MM-dd}" : "历史流水 - 全部记录";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 540);
        MinimumSize = new Size(500, 360);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _browser = new WorkLogBrowser(app.Db, fixedDate);
        _browser.EntryActivated += EditSelected;
        root.Controls.Add(_browser, 0, 0);
        var add = UiLayout.Button("补录一条");
        add.Click += (_, _) =>
        {
            using var form = new ManualEntryForm(_app.Db, _app.Config, defaultDate: _browser.SelectedDate);
            form.ShowDialog(this);
        };
        var edit = UiLayout.Button("编辑");
        edit.Enabled = false;
        edit.Click += (_, _) => EditSelected();
        var delete = UiLayout.Button("删除");
        delete.Enabled = false;
        delete.Click += (_, _) => DeleteSelected();
        _browser.SelectionChanged += () => edit.Enabled = delete.Enabled = _browser.SelectedEntry != null;
        var close = UiLayout.Button("关闭");
        close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => Close();
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(UiLayout.ButtonRow(false, add, edit, delete), 0, 0);
        buttons.Controls.Add(UiLayout.ButtonRow(true, close), 1, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        CancelButton = close;
    }

    private void EditSelected()
    {
        if (_browser.SelectedEntry is not WorkLogEntry entry) return;
        using var form = new ManualEntryForm(_app.Db, _app.Config, entry);
        form.ShowDialog(this);
    }

    private void DeleteSelected()
    {
        if (_browser.SelectedEntry is not WorkLogEntry entry) return;
        if (MessageBox.Show(this, "确定删除这条记录？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            _app.Db.DeleteWorkLog(entry.Id);
    }
}
