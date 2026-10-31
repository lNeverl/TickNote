using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

/// <summary>待办管理和提醒选择共用的日期范围、搜索、完成状态筛选及列表。</summary>
public sealed class TodoBrowser : UserControl
{
    private readonly Database _db;
    private readonly bool _allowCompletion;
    private readonly TextBox _search;
    private readonly CheckBox _showCompleted;
    private readonly TodoListView _list;
    private readonly Label _empty;
    private readonly Label _count;
    private readonly Font _completedFont;
    private DateTime? _dateScope;
    private bool _loading;

    public DateTime? DateScope => _dateScope;
    public bool ShowCompleted { get => _showCompleted.Checked; set => _showCompleted.Checked = value; }
    public TodoEntry? SelectedEntry => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as TodoEntry;
    public event Action? EntryActivated;
    public event Action? SelectionChanged;

    public TodoBrowser(Database db, DateTime? dateScope, bool allowCompletion, bool showCompleted = true)
    {
        _db = db;
        _dateScope = dateScope?.Date;
        _allowCompletion = allowCompletion;
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.None;
        Margin = Padding.Empty;
        Font = new Font("Microsoft YaHei UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3, Margin = new Padding(0, 0, 0, 8),
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.Controls.Add(new Label
        {
            Text = "搜索", AutoSize = true, Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 8, 0),
        }, 0, 0);
        _search = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "待办内容、标签或 yyyy-MM-dd",
            AccessibleName = "搜索待办内容、标签或日期",
            Margin = new Padding(0, 4, 8, 0),
        };
        _search.TextChanged += (_, _) => Reload();
        filters.Controls.Add(_search, 1, 0);
        _showCompleted = new CheckBox
        {
            Text = "显示已完成", AutoSize = true, Checked = showCompleted,
            Anchor = AnchorStyles.Left, Margin = Padding.Empty,
        };
        _showCompleted.CheckedChanged += (_, _) => Reload();
        filters.Controls.Add(_showCompleted, 2, 0);
        root.Controls.Add(filters, 0, 0);

        var listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _list = new TodoListView
        {
            Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true,
            FullRowSelect = true, MultiSelect = false, HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable, GridLines = false, Margin = Padding.Empty,
        };
        _list.Columns.Add("待办内容", 420);
        _list.Columns.Add("标签", 100);
        _list.Columns.Add("完成时间", 110);
        _list.Columns.Add("所属日期", _dateScope == null ? 105 : 0);
        _list.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke();
        _list.MouseDoubleClick += (_, e) =>
        {
            if ((_list.HitTest(e.Location).Location & ListViewHitTestLocations.StateImage) == 0)
                EntryActivated?.Invoke();
        };
        _list.ItemCheck += OnItemCheck;
        _list.SizeChanged += (_, _) => ResizeContentColumn();
        _empty = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText, BackColor = Color.White, Padding = new Padding(20),
        };
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_empty);
        root.Controls.Add(listHost, 0, 1);
        _count = new Label
        {
            AutoSize = true, ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0),
        };
        root.Controls.Add(_count, 0, 2);
        Controls.Add(root);
        _completedFont = new Font(Font, FontStyle.Strikeout);
        _db.TodosChanged += Reload;
        Reload();
    }

    public void SetDateScope(DateTime? date)
    {
        var normalized = date?.Date;
        if (_dateScope == normalized) return;
        _dateScope = normalized;
        Reload();
    }

    public void Reload()
    {
        var selectedId = SelectedEntry?.Id;
        var todos = _dateScope is DateTime date
            ? _db.GetTodos(date.ToString("yyyy-MM-dd"))
            : _db.GetAllTodos();
        var query = _search.Text.Trim();
        var visible = todos.Where(todo => (_showCompleted.Checked || !todo.IsCompleted)
            && (query.Length == 0
                || todo.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (todo.Tag?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || todo.TodoDate.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        _loading = true;
        try
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var todo in visible)
                {
                    var item = new ListViewItem(todo.Content)
                    {
                        Tag = todo,
                        Checked = todo.IsCompleted,
                        Font = todo.IsCompleted ? _completedFont : _list.Font,
                        ForeColor = todo.IsCompleted ? SystemColors.GrayText : SystemColors.ControlText,
                    };
                    item.SubItems.Add(todo.Tag ?? "");
                    item.SubItems.Add(todo.CompletedAt is { Length: >= 16 } at ? at[11..16] : "");
                    item.SubItems.Add(todo.TodoDate);
                    _list.Items.Add(item);
                    if (todo.Id == selectedId) item.Selected = true;
                }
            }
            finally { _list.EndUpdate(); }

            // 显示原本隐藏的列表也可能创建句柄并恢复勾选状态，仍属于加载过程。
            _list.Visible = visible.Count > 0;
            _empty.Visible = visible.Count == 0;
            _empty.Text = query.Length > 0
                ? "没有符合搜索条件的待办。"
                : todos.Count == 0
                    ? _dateScope != null ? "这一天还没有待办，在上方输入后按 Enter 添加。" : "还没有待办。"
                    : "没有可显示的未完成待办，可勾选“显示已完成”查看。";
            _count.Text = $"显示 {visible.Count} / {todos.Count} 条 · 未完成 {todos.Count(todo => !todo.IsCompleted)} 条 · 已完成 {todos.Count(todo => todo.IsCompleted)} 条";
            ResizeContentColumn();
        }
        finally { _loading = false; }
        SelectionChanged?.Invoke();
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        // 创建/重建原生句柄时的状态恢复不是用户勾选，不能写库并重入 Reload。
        if (_loading || _list.IsInitializingHandle || !IsHandleCreated || !_list.IsHandleCreated) return;
        if (!_allowCompletion)
        {
            e.NewValue = e.CurrentValue;
            return;
        }
        if (e.NewValue == e.CurrentValue || _list.Items[e.Index].Tag is not TodoEntry todo) return;
        var id = todo.Id;
        // 数据库快照要等异步保存后才更新，连续切换时必须保留每一次实际操作。
        var completed = e.NewValue == CheckState.Checked;
        void Save()
        {
            if (IsDisposed) return;
            try
            {
                _db.SetTodoCompleted(id, completed);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                Reload();
                MessageBox.Show(FindForm(), "更新待办状态失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        BeginInvoke(Save);
    }

    private void ResizeContentColumn()
    {
        if (_list.Columns.Count < 4) return;
        // 一次提交全部列宽，促使原生列表重新计算滚动范围，避免列已缩短却残留横向滚动条。
        _list.BeginUpdate();
        try
        {
            var factor = DeviceDpi / 96.0;
            var compact = _list.ClientSize.Width < 560 * factor;
            _list.Columns[1].Width = (int)Math.Round((compact ? 80 : 100) * factor);
            _list.Columns[3].Width = _dateScope == null ? (int)Math.Round(105 * factor) : 0;
            var minimum = (int)Math.Round((compact ? 120 : 160) * factor);
            var otherWidths = _list.Columns[1].Width + _list.Columns[2].Width + _list.Columns[3].Width;
            var width = _list.ClientSize.Width - otherWidths - SystemInformation.VerticalScrollBarWidth - 4;
            _list.Columns[0].Width = Math.Max(minimum, width);
        }
        finally { _list.EndUpdate(); }
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        if (_list == null) return;
        foreach (ColumnHeader column in _list.Columns)
            column.Width = (int)Math.Round(column.Width * factor.Width);
        ResizeContentColumn();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _db.TodosChanged -= Reload;
            _completedFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class TodoListView : ThemedListView
    {
        private bool _suppressDoubleClickCheck;
        internal bool IsInitializingHandle { get; private set; }

        protected override void CreateHandle()
        {
            IsInitializingHandle = true;
            try { base.CreateHandle(); }
            finally { IsInitializingHandle = false; }
        }

        protected override void WndProc(ref Message m)
        {
            const int WmLeftButtonDoubleClick = 0x0203;
            var previous = _suppressDoubleClickCheck;
            if (m.Msg == WmLeftButtonDoubleClick)
                _suppressDoubleClickCheck = (HitTest(new Point(unchecked((int)m.LParam))).Location
                    & ListViewHitTestLocations.StateImage) == 0;
            try { base.WndProc(ref m); }
            finally { _suppressDoubleClickCheck = previous; }
        }

        protected override void OnItemCheck(ItemCheckEventArgs e)
        {
            // 正文双击用于编辑或选择，拦住 ListView 默认附带的勾选切换。
            if (_suppressDoubleClickCheck) e.NewValue = e.CurrentValue;
            else base.OnItemCheck(e);
        }
    }
}
