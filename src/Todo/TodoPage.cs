using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

/// <summary>每日待办：按单日或全部日期查看，新建、勾选完成、恢复、编辑和删除即时入库。</summary>
public sealed class TodoPage : WorkspacePage
{
    private readonly Database _db;
    private readonly AppConfig _config;
    private readonly DateTimePicker _dtDate;
    private readonly TextBox _txtNew;
    private readonly TodoTagSelector _tag;
    private readonly RadioButton _byDate;
    private readonly RadioButton _all;
    private readonly TodoBrowser _browser;
    private readonly Button _edit;
    private readonly Button _delete;

    public DateTime SelectedDate => _dtDate.Value.Date;
    public TodoEntry? SelectedEntry => _browser.SelectedEntry;
    public override bool HasUnsavedChanges => _txtNew.TextLength > 0 || _tag.Text.Length > 0;
    public override bool SaveChanges() => AddTodo();

    public void ShowAllPending()
    {
        _all.Checked = true;
        _browser.ShowCompleted = false;
    }

    public TodoPage(Database db, AppConfig config, DateTime? defaultDate = null)
    {
        _db = db;
        _config = config;
        Text = "每日待办";
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;
        AutoScroll = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14), MinimumSize = new Size(0, 360) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var dateControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true, Margin = Padding.Empty,
        };
        dateControls.Controls.Add(new Label { Text = "待办日期", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 0) });
        _dtDate = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Width = 136,
            MinimumSize = new Size(136, 0),
            Value = defaultDate ?? DateTime.Today,
            Margin = new Padding(0, 0, 8, 0),
            AccessibleName = "待办所属日期",
        };
        dateControls.Controls.Add(_dtDate);
        var today = UiLayout.Button("回到今天");
        today.Click += (_, _) => _dtDate.Value = DateTime.Today;
        dateControls.Controls.Add(today);
        dateControls.Controls.Add(new Label
        {
            Text = "新建始终归入此日期", AutoSize = true, Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText, Margin = new Padding(0, 7, 0, 0),
        });
        root.Controls.Add(dateControls, 0, 0);

        var tagRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 10, 0, 0) };
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tagRow.Controls.Add(new Label { Text = "标签（可选）", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        _tag = new TodoTagSelector(config) { Margin = Padding.Empty };
        tagRow.Controls.Add(_tag, 1, 0);
        root.Controls.Add(tagRow, 0, 1);

        var inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 10, 0, 0) };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _txtNew = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "输入待办内容，按 Enter 添加",
            Margin = new Padding(0, 4, 8, 0),
            AccessibleName = "新待办内容",
        };
        _txtNew.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            AddTodo();
        };
        var add = UiLayout.Button("添加", primary: true);
        add.Margin = Padding.Empty;
        add.Click += (_, _) => AddTodo();
        inputRow.Controls.Add(_txtNew, 0, 0);
        inputRow.Controls.Add(add, 1, 0);
        root.Controls.Add(inputRow, 0, 2);

        _byDate = new RadioButton { Text = "按日期查看", AutoSize = true, Checked = true, Margin = new Padding(0, 8, 20, 8) };
        _all = new RadioButton { Text = "全部待办", AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        _byDate.CheckedChanged += (_, _) => ChangeScope();
        _all.CheckedChanged += (_, _) => ChangeScope();
        var scope = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = Padding.Empty,
        };
        scope.Controls.Add(_byDate);
        scope.Controls.Add(_all);
        root.Controls.Add(scope, 0, 3);

        _browser = new TodoBrowser(db, SelectedDate, allowCompletion: true);
        _browser.EntryActivated += EditSelected;
        root.Controls.Add(_browser, 0, 4);

        _edit = UiLayout.Button("编辑");
        _edit.Enabled = false;
        _edit.Click += (_, _) => EditSelected();
        _delete = UiLayout.Button("删除");
        _delete.Enabled = false;
        _delete.Click += (_, _) => DeleteSelected();
        root.Controls.Add(UiLayout.ButtonRow(false, _edit, _delete), 0, 5);

        Controls.Add(root);
        _dtDate.ValueChanged += (_, _) => ChangeScope();
        _browser.SelectionChanged += UpdateButtons;
        UpdateTitle();

        // 筛选、快速录入、列表和操作各占一行；沿用原有控件及事件，切页草稿不变。
        var actions = root.GetControlFromPosition(0, 5)!;
        root.SuspendLayout();
        root.Controls.Clear();
        root.RowCount = 4;
        root.RowStyles.Clear();
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.MinimumSize = new Size(0, 300);
        scope.Controls.Clear();
        scope.WrapContents = true;
        scope.Margin = new Padding(0, 0, 0, 12);
        _all.Margin = _byDate.Margin = new Padding(0, 6, 12, 6);
        scope.Controls.AddRange(new Control[] { _all, _byDate, _dtDate, today });
        var capture = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 14), BackColor = UiLayout.PageBack };
        capture.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        capture.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        capture.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _txtNew.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _txtNew.Margin = new Padding(0, 0, 10, 0);
        _tag.Margin = new Padding(0, 0, 10, 0);
        _tag.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _tag.Dock = DockStyle.None;
        capture.Controls.Add(_txtNew, 0, 0);
        capture.Controls.Add(_tag, 1, 0);
        capture.Controls.Add(add, 2, 0);
        root.Controls.Add(scope, 0, 0);
        root.Controls.Add(capture, 0, 1);
        root.Controls.Add(_browser, 0, 2);
        root.Controls.Add(actions, 0, 3);
        tagRow.Dispose();
        inputRow.Dispose();
        dateControls.Dispose();
        root.ResumeLayout(true);
    }

    public void Reload() => _browser.Reload();

    private void ChangeScope()
    {
        _browser.SetDateScope(_byDate.Checked ? SelectedDate : null);
        UpdateTitle();
    }

    private void UpdateTitle() => Text = _all.Checked ? "每日待办 - 全部待办" : $"每日待办 - {SelectedDate:yyyy-MM-dd}";

    private bool AddTodo()
    {
        var content = _txtNew.Text.Trim();
        if (content.Length == 0)
        {
            MessageBox.Show(FindForm(), "请输入待办内容。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _txtNew.Focus();
            return false;
        }
        try
        {
            _db.InsertTodo(new TodoEntry { TodoDate = SelectedDate.ToString("yyyy-MM-dd"), Content = content, Tag = _tag.Text });
            _txtNew.Clear();
            _tag.Text = "";
            _txtNew.Focus();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(FindForm(), "添加失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void EditSelected()
    {
        if (SelectedEntry is not TodoEntry entry) return;
        using var form = new TodoEditForm(_db, _config, entry);
        form.ShowDialog(FindForm());
    }

    private void DeleteSelected()
    {
        if (SelectedEntry is not TodoEntry entry) return;
        var summary = entry.Content.Length <= 40 ? entry.Content : entry.Content[..40] + "…";
        if (MessageBox.Show(FindForm(), $"确定删除这条待办？删除后不可恢复。\n\n{summary}", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            _db.DeleteTodo(entry.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(FindForm(), "删除失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateButtons() => _edit.Enabled = _delete.Enabled = SelectedEntry != null;
}
