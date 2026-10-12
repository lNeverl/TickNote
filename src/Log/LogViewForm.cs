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
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _browser = new WorkLogBrowser(app.Db, fixedDate);
        _browser.EntryActivated += EditSelected;
        root.Controls.Add(_browser, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var add = new Button { Text = "补录一条", AutoSize = true };
        add.Click += (_, _) =>
        {
            using var form = new ManualEntryForm(_app.Db, _app.Config, defaultDate: _browser.SelectedDate);
            form.ShowDialog(this);
        };
        var edit = new Button { Text = "编辑", AutoSize = true };
        edit.Click += (_, _) => EditSelected();
        var delete = new Button { Text = "删除", AutoSize = true };
        delete.Click += (_, _) => DeleteSelected();
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        buttons.Controls.AddRange(new Control[] { add, edit, delete, close });
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
