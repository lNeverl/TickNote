using SedentaryReminder.Data;

namespace SedentaryReminder.Log;

/// <summary>历史查看和跨天续记共用的日期筛选、搜索及全文预览。</summary>
public sealed class WorkLogBrowser : UserControl
{
    private readonly Database _db;
    private readonly bool _fixedDate;
    private readonly Button _dateButton;
    private readonly TextBox _search;
    private readonly ListView _list;
    private readonly TextBox _preview;
    private readonly Label _count;
    private readonly Button _copy;
    public DateTime? SelectedDate { get; private set; }
    public WorkLogEntry? SelectedEntry => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as WorkLogEntry;
    public event Action? EntryActivated;
    public event Action? SelectionChanged;

    public WorkLogBrowser(Database db, DateTime? fixedDate = null)
    {
        _db = db;
        _fixedDate = fixedDate.HasValue;
        SelectedDate = fixedDate?.Date;
        Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _dateButton = new Button { AutoSize = true, Enabled = !_fixedDate };
        _dateButton.Click += (_, _) =>
        {
            using var calendar = new RecordedDatePickerForm(_db.GetLogDates(), SelectedDate);
            if (calendar.ShowDialog(FindForm()) != DialogResult.OK) return;
            SelectedDate = calendar.SelectedDate;
            Reload();
        };
        var all = new Button { Text = "查看全部", AutoSize = true, Visible = !_fixedDate };
        all.Click += (_, _) => { SelectedDate = null; Reload(); };
        _search = new TextBox { Width = 200, PlaceholderText = "搜索内容或标签" };
        _search.TextChanged += (_, _) => Reload();
        var refresh = new Button { Text = "刷新", AutoSize = true };
        refresh.Click += (_, _) => Reload();
        filters.Controls.AddRange(new Control[] { _dateButton, all, _search, refresh });
        root.Controls.Add(filters, 0, 0);
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            GridLines = true, MultiSelect = false, HideSelection = false,
        };
        _list.Columns.Add("日期", 105);
        _list.Columns.Add("时间", 110);
        _list.Columns.Add("标签", 90);
        _list.Columns.Add("内容", 350);
        _list.Columns.Add("来源", 60);
        _list.SizeChanged += (_, _) => ResizeContentColumn();
        _preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        _copy = new Button { Text = "复制选中内容", AutoSize = true, Enabled = false };
        _list.DoubleClick += (_, _) => EntryActivated?.Invoke();
        _list.SelectedIndexChanged += (_, _) =>
        {
            _preview.Text = SelectedEntry?.Content ?? "";
            _copy.Enabled = !string.IsNullOrEmpty(SelectedEntry?.Content);
            SelectionChanged?.Invoke();
        };
        root.Controls.Add(_list, 0, 1);
        var previewHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _count = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        _copy.Click += (_, _) =>
        {
            if (SelectedEntry?.Content is not { Length: > 0 } text) return;
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(FindForm(), "剪贴板正被其他程序占用，请稍后重试。", "复制失败");
            }
        };
        previewHeader.Controls.Add(_count, 0, 0);
        previewHeader.Controls.Add(_copy, 1, 0);
        root.Controls.Add(previewHeader, 0, 2);
        root.Controls.Add(_preview, 0, 3);
        Controls.Add(root);
        _db.WorkLogsChanged += Reload;
        Reload();
    }

    public void Reload()
    {
        var selectedId = SelectedEntry?.Id;
        var dates = _db.GetLogDates();
        if (!_fixedDate && SelectedDate is DateTime selected && !dates.Contains(selected)) SelectedDate = null;
        _dateButton.Text = SelectedDate is DateTime date ? $"日期：{date:yyyy-MM-dd}" : "全部日期 · 选择日期…";
        _dateButton.Enabled = !_fixedDate && dates.Count > 0;
        var logs = _db.GetLogs(SelectedDate?.ToString("yyyy-MM-dd") ?? "");
        var query = _search.Text.Trim();
        IEnumerable<WorkLogEntry> filtered = logs.Where(log => query.Length == 0
            || (log.Content?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (log.Tag?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        if (SelectedDate == null) filtered = filtered.OrderByDescending(log => log.LogDate).ThenByDescending(log => log.EndTime).ThenByDescending(log => log.Id);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var log in filtered)
            {
                var time = log.StartTime is { Length: > 0 } start ? $"{start}~{log.EndTime}" : log.EndTime ?? "";
                var item = new ListViewItem(new[] { log.LogDate, time, log.Tag ?? "", (log.Content ?? "").ReplaceLineEndings(" "), SourceName(log.Source) }) { Tag = log };
                _list.Items.Add(item);
                if (log.Id == selectedId) item.Selected = true;
            }
        }
        finally { _list.EndUpdate(); }
        _preview.Text = SelectedEntry?.Content ?? "";
        _copy.Enabled = !string.IsNullOrEmpty(SelectedEntry?.Content);
        _count.Text = $"共 {_list.Items.Count} 条 · 选中记录可查看全文";
        SelectionChanged?.Invoke();
    }

    private static string SourceName(string source) => source switch { "manual" => "补记", "continue" => "续记", _ => "弹窗" };

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        // DpiForm 的手动缩放不会调整原生 ListView 列宽，需要与字体同步缩放。
        if (_list == null) return;
        foreach (ColumnHeader column in _list.Columns)
            column.Width = (int)Math.Round(column.Width * factor.Width);
        ResizeContentColumn();
    }

    private void ResizeContentColumn()
    {
        if (_list.Columns.Count < 5) return;
        var otherWidths = _list.Columns.Cast<ColumnHeader>().Where(column => column.Index != 3).Sum(column => column.Width);
        _list.Columns[3].Width = Math.Max((int)Math.Round(200 * DeviceDpi / 96.0), _list.ClientSize.Width - otherWidths - SystemInformation.VerticalScrollBarWidth - 4);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _db.WorkLogsChanged -= Reload;
        base.Dispose(disposing);
    }
}
