using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

/// <summary>每日待办：按单日或全部日期查看，新建、勾选完成、恢复、编辑和删除即时入库。</summary>
public sealed class TodoViewForm : DpiForm
{
    private readonly Database _db;
    private readonly DateTimePicker _dtDate;
    private readonly TextBox _txtNew;
    private readonly RadioButton _byDate;
    private readonly RadioButton _all;
    private readonly TodoBrowser _browser;
    private readonly Button _edit;
    private readonly Button _delete;

    public DateTime SelectedDate => _dtDate.Value.Date;
    public TodoEntry? SelectedEntry => _browser.SelectedEntry;

    public TodoViewForm(Database db, DateTime? defaultDate = null)
    {
        _db = db;
        Text = "每日待办";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(500, 400);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
        root.Controls.Add(inputRow, 0, 1);

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
        root.Controls.Add(scope, 0, 2);

        _browser = new TodoBrowser(db, SelectedDate, allowCompletion: true);
        root.Controls.Add(_browser, 0, 3);

        _edit = UiLayout.Button("编辑");
        _edit.Enabled = false;
        _edit.Click += (_, _) => EditSelected();
        _delete = UiLayout.Button("删除");
        _delete.Enabled = false;
        _delete.Click += (_, _) => DeleteSelected();
        var close = UiLayout.Button("关闭");
        close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => Close();
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(UiLayout.ButtonRow(false, _edit, _delete), 0, 0);
        buttons.Controls.Add(UiLayout.ButtonRow(true, close), 1, 0);
        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);
        CancelButton = close;
        _dtDate.ValueChanged += (_, _) => ChangeScope();
        _browser.SelectionChanged += UpdateButtons;
        UpdateTitle();
    }

    public void Reload() => _browser.Reload();

    private void ChangeScope()
    {
        _browser.SetDateScope(_byDate.Checked ? SelectedDate : null);
        UpdateTitle();
    }

    private void UpdateTitle() => Text = _all.Checked ? "每日待办 - 全部待办" : $"每日待办 - {SelectedDate:yyyy-MM-dd}";

    private void AddTodo()
    {
        var content = _txtNew.Text.Trim();
        if (content.Length == 0)
        {
            MessageBox.Show(this, "请输入待办内容。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _txtNew.Focus();
            return;
        }
        try
        {
            _db.InsertTodo(new TodoEntry { TodoDate = SelectedDate.ToString("yyyy-MM-dd"), Content = content });
            _txtNew.Clear();
            _txtNew.Focus();
        }
        catch (Exception ex) when (ex is ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(this, "添加失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EditSelected()
    {
        if (SelectedEntry is not TodoEntry entry) return;
        using var form = new TodoEditForm(_db, entry);
        form.ShowDialog(this);
    }

    private void DeleteSelected()
    {
        if (SelectedEntry is not TodoEntry entry) return;
        var summary = entry.Content.Length <= 40 ? entry.Content : entry.Content[..40] + "…";
        if (MessageBox.Show(this, $"确定删除这条待办？删除后不可恢复。\n\n{summary}", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            _db.DeleteTodo(entry.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(this, "删除失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateButtons() => _edit.Enabled = _delete.Enabled = SelectedEntry != null;
}
