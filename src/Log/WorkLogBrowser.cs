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
    private readonly TableLayoutPanel _filters;
    private readonly Panel _searchHost;
    private readonly Button _all;
    private readonly Button _refresh;
    private readonly Button _clearSearch;
    private bool _compactFilters;
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
        AutoScaleMode = AutoScaleMode.None;
        Margin = Padding.Empty;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        _filters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4, RowCount = 2, Margin = new Padding(0, 0, 0, 12),
        };
        _filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _filters.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _filters.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _dateButton = UiLayout.Button("全部日期 ▾");
        _dateButton.MinimumSize = new Size(148, 32);
        _dateButton.Enabled = !_fixedDate;
        _dateButton.Click += (_, _) =>
        {
            using var calendar = new RecordedDatePickerForm(_db.GetLogDates(), SelectedDate);
            if (calendar.ShowDialog(FindForm()) != DialogResult.OK) return;
            SelectedDate = calendar.SelectedDate;
            Reload();
        };
        _all = UiLayout.Button("查看全部");
        _all.Visible = !_fixedDate;
        _all.Click += (_, _) => { SelectedDate = null; Reload(); };
        _searchHost = new Panel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 32, MinimumSize = new Size(0, 32),
            BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White,
            Padding = new Padding(8, 0, 4, 0), Margin = new Padding(0, 0, 8, 0),
        };
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(new Label { Text = "搜索", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        _search = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right, BorderStyle = BorderStyle.None,
            PlaceholderText = "内容或标签", Margin = Padding.Empty, AccessibleName = "搜索工作流水内容或标签",
        };
        _clearSearch = new Button
        {
            Text = "×", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = Padding.Empty,
            BackColor = Color.White, ForeColor = SystemColors.GrayText, Enabled = false, AccessibleName = "清空搜索",
        };
        _clearSearch.FlatAppearance.BorderSize = 0;
        _clearSearch.Click += (_, _) => { _search.Clear(); _search.Focus(); };
        _search.TextChanged += (_, _) => { _clearSearch.Enabled = _search.TextLength > 0; Reload(); };
        searchRow.Controls.Add(_search, 1, 0);
        searchRow.Controls.Add(_clearSearch, 2, 0);
        _searchHost.Controls.Add(searchRow);
        _refresh = UiLayout.Button("刷新");
        _refresh.Margin = Padding.Empty;
        _refresh.Click += (_, _) => Reload();
        _filters.Controls.Add(_dateButton, 0, 0);
        _filters.Controls.Add(_all, 1, 0);
        _filters.Controls.Add(_searchHost, 2, 0);
        _filters.Controls.Add(_refresh, 3, 0);
        _filters.SizeChanged += (_, _) => LayoutFilters();
        _filters.Layout += (_, _) =>
        {
            // 搜索外框保持与按钮等高；Dock.Fill 会让 AutoSize 行把自身越撑越高。
            var height = Math.Max(_dateButton.GetPreferredSize(Size.Empty).Height, _refresh.GetPreferredSize(Size.Empty).Height);
            if (_searchHost.Height != height) _searchHost.Height = height;
            // 初次 DPI 缩放可能发生在换行之后，按实际 DPI 重新确定间距，避免被缩放两次。
            var gap = (int)Math.Round(8 * DeviceDpi / 96.0);
            _searchHost.Margin = new Padding(0, _compactFilters ? gap : 0, gap, 0);
            _refresh.Margin = new Padding(0, _compactFilters ? gap : 0, 0, 0);
        };
        root.Controls.Add(_filters, 0, 0);
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            GridLines = false, MultiSelect = false, HideSelection = false, Margin = Padding.Empty,
        };
        _list.Columns.Add("日期", 105);
        _list.Columns.Add("时间", 110);
        _list.Columns.Add("标签", 90);
        _list.Columns.Add("内容", 350);
        _list.Columns.Add("来源", 60);
        _list.SizeChanged += (_, _) => ResizeContentColumn();
        _preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Margin = Padding.Empty };
        _copy = UiLayout.Button("复制选中内容");
        _copy.Enabled = false;
        _copy.Margin = Padding.Empty;
        _list.DoubleClick += (_, _) => EntryActivated?.Invoke();
        _list.SelectedIndexChanged += (_, _) =>
        {
            _preview.Text = SelectedEntry?.Content ?? "";
            _copy.Enabled = !string.IsNullOrEmpty(SelectedEntry?.Content);
            SelectionChanged?.Invoke();
        };
        root.Controls.Add(_list, 0, 1);
        var previewHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0, 10, 0, 8) };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _count = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText, Margin = Padding.Empty };
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
        _dateButton.Text = SelectedDate is DateTime date ? $"{date:yyyy-MM-dd} ▾" : "全部日期 ▾";
        _dateButton.Enabled = !_fixedDate && dates.Count > 0;
        _all.Enabled = SelectedDate != null;
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
                var item = new ListViewItem(new[] { log.LogDate, time, log.Tag ?? "", (log.Content ?? "").ReplaceLineEndings(" "), SourceName(log.Source) })
                {
                    Tag = log, BackColor = _list.Items.Count % 2 == 0 ? Color.White : Color.FromArgb(247, 249, 252),
                };
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

    private void LayoutFilters()
    {
        var compact = _filters.Width < (int)Math.Round(560 * DeviceDpi / 96.0);
        if (compact == _compactFilters) return;
        _compactFilters = compact;
        _filters.SuspendLayout();
        _filters.SetCellPosition(_searchHost, new TableLayoutPanelCellPosition(compact ? 0 : 2, compact ? 1 : 0));
        _filters.SetColumnSpan(_searchHost, compact ? 3 : 1);
        _filters.SetCellPosition(_refresh, new TableLayoutPanelCellPosition(3, compact ? 1 : 0));
        _filters.ResumeLayout(true);
    }

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
        var factor = DeviceDpi / 96.0;
        var compact = _list.ClientSize.Width < 560 * factor;
        _list.Columns[2].Width = (int)Math.Round((compact ? 64 : 90) * factor);
        _list.Columns[4].Width = (int)Math.Round((compact ? 52 : 60) * factor);
        var otherWidths = _list.Columns.Cast<ColumnHeader>().Where(column => column.Index != 3).Sum(column => column.Width);
        _list.Columns[3].Width = Math.Max((int)Math.Round(120 * factor), _list.ClientSize.Width - otherWidths - SystemInformation.VerticalScrollBarWidth - 4);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _db.WorkLogsChanged -= Reload;
        base.Dispose(disposing);
    }
}
