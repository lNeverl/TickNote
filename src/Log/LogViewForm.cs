using SedentaryReminder.Data;

namespace SedentaryReminder.Log;

/// <summary>
/// 流水视图（M4-1）：按时间轴列出某一天的流水，可编辑、删除、补录。
/// 托盘"查看今日"传入今天日期并锁定；"历史流水"允许用日期选择器查任意一天。
/// </summary>
public sealed class LogViewForm : DpiForm
{
    private readonly AppContext _app;
    private readonly DateTimePicker _dtDate;
    private readonly ListView _list;

    public LogViewForm(AppContext app, DateTime? fixedDate)
    {
        _app = app;
        Text = "工作流水";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 460);
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 顶部：日期选择
        var topRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        topRow.Controls.Add(new Label { Text = "日期：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 4, 0) });
        _dtDate = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Width = 120,
            Value = fixedDate ?? DateTime.Today,
            Enabled = fixedDate == null,
        };
        _dtDate.ValueChanged += (_, _) => Reload();
        topRow.Controls.Add(_dtDate);
        var btnRefresh = new Button { Text = "刷新", AutoSize = true };
        btnRefresh.Click += (_, _) => Reload();
        topRow.Controls.Add(btnRefresh);
        root.Controls.Add(topRow, 0, 0);

        // 列表
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
        };
        _list.Columns.Add("时间", 110);
        _list.Columns.Add("标签", 90);
        _list.Columns.Add("内容", 380);
        _list.Columns.Add("来源", 60);
        _list.DoubleClick += (_, _) => EditSelected();
        root.Controls.Add(_list, 0, 1);

        // 底部按钮
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var btnAdd = new Button { Text = "补录一条", AutoSize = true };
        btnAdd.Click += (_, _) =>
        {
            using var f = new ManualEntryForm(_app.Db, _app.Config, null, _dtDate.Value.Date);
            if (f.ShowDialog(this) == DialogResult.OK) Reload();
        };
        var btnEdit = new Button { Text = "编辑", AutoSize = true };
        btnEdit.Click += (_, _) => EditSelected();
        var btnDelete = new Button { Text = "删除", AutoSize = true };
        btnDelete.Click += (_, _) => DeleteSelected();
        var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true };
        btnRow.Controls.Add(btnAdd);
        btnRow.Controls.Add(btnEdit);
        btnRow.Controls.Add(btnDelete);
        btnRow.Controls.Add(btnClose);
        root.Controls.Add(btnRow, 0, 2);

        Controls.Add(root);
        CancelButton = btnClose;
        Reload();
    }

    private WorkLogEntry? Selected =>
        _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as WorkLogEntry;

    private void Reload()
    {
        _list.Items.Clear();
        var logs = _app.Db.GetLogs(_dtDate.Value.ToString("yyyy-MM-dd"));
        foreach (var log in logs)
        {
            var time = log.StartTime is { Length: > 0 } st ? $"{st}~{log.EndTime}" : log.EndTime ?? "";
            var item = new ListViewItem(new[]
            {
                time,
                log.Tag ?? "",
                log.Content ?? "",
                SourceName(log.Source),
            }) { Tag = log };
            _list.Items.Add(item);
        }
        Text = $"工作流水 - {_dtDate.Value:yyyy-MM-dd}（共 {logs.Count} 条）";
    }

    private static string SourceName(string source) => source switch
    {
        "manual" => "补记",
        "continue" => "续记",
        _ => "弹窗",
    };

    private void EditSelected()
    {
        if (Selected is not WorkLogEntry entry) return;
        using var f = new ManualEntryForm(_app.Db, _app.Config, entry);
        if (f.ShowDialog(this) == DialogResult.OK) Reload();
    }

    private void DeleteSelected()
    {
        if (Selected is not WorkLogEntry entry) return;
        if (MessageBox.Show(this, "确定删除这条记录？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _app.Db.DeleteWorkLog(entry.Id);
        Reload();
    }
}
