using SedentaryReminder.Data;

namespace SedentaryReminder.Log;

/// <summary>今日流水固定日期，历史流水默认查看全部；编辑、删除、补录均即时刷新。</summary>
public sealed class LogPage : WorkspacePage
{
    private readonly AppContext _app;
    private readonly WorkLogBrowser _browser;

    public LogPage(AppContext app, DateTime? fixedDate)
    {
        _app = app;
        Text = fixedDate is DateTime date ? $"工作流水 - {date:yyyy-MM-dd}" : "历史流水 - 全部记录";
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _browser = new WorkLogBrowser(app.Db);
        _browser.SetDateScope(fixedDate);
        _browser.EntryActivated += EditSelected;
        root.Controls.Add(_browser, 0, 0);
        var add = UiLayout.Button("补录一条");
        add.Click += (_, _) =>
        {
            using var form = new ManualEntryForm(_app.Db, _app.Config, defaultDate: _browser.SelectedDate);
            form.ShowDialog(FindForm());
        };
        var edit = UiLayout.Button("编辑");
        edit.Enabled = false;
        edit.Click += (_, _) => EditSelected();
        var delete = UiLayout.Button("删除");
        delete.Enabled = false;
        delete.Click += (_, _) => DeleteSelected();
        _browser.SelectionChanged += () => edit.Enabled = delete.Enabled = _browser.SelectedEntry != null;
        var today = UiLayout.Button("查看今日");
        today.Click += (_, _) => SetDateScope(DateTime.Today);
        root.Controls.Add(UiLayout.ButtonRow(false, today, add, edit, delete), 0, 1);
        Controls.Add(root);
    }

    public void SetDateScope(DateTime? date) => _browser.SetDateScope(date);

    private void EditSelected()
    {
        if (_browser.SelectedEntry is not WorkLogEntry entry) return;
        using var form = new ManualEntryForm(_app.Db, _app.Config, entry);
        form.ShowDialog(FindForm());
    }

    private void DeleteSelected()
    {
        if (_browser.SelectedEntry is not WorkLogEntry entry) return;
        if (MessageBox.Show(FindForm(), "确定删除这条记录？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            _app.Db.DeleteWorkLog(entry.Id);
    }
}
