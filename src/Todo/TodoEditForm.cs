using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

/// <summary>编辑单条待办内容与标签，所属日期和完成状态保持不变。</summary>
public sealed class TodoEditForm : DpiForm
{
    private readonly Database _db;
    private readonly TodoEntry _editing;
    private readonly TextBox _txtContent;
    private readonly TodoTagSelector _tag;

    public TodoEditForm(Database db, AppConfig config, TodoEntry editing)
    {
        _db = db;
        _editing = editing;
        Text = "编辑待办";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 290);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = $"所属日期：{editing.TodoDate}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 10),
        }, 0, 0);
        var tagRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tagRow.Controls.Add(new Label { Text = "标签（可选）", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        _tag = new TodoTagSelector(config, editing.Tag) { Margin = Padding.Empty };
        tagRow.Controls.Add(_tag, 1, 0);
        root.Controls.Add(tagRow, 0, 1);
        _txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Text = UiLayout.MultilineText(editing.Content),
        };
        root.Controls.Add(_txtContent, 0, 2);

        var save = UiLayout.Button("保存", primary: true);
        save.Click += OnSave;
        var cancel = UiLayout.Button("取消");
        cancel.DialogResult = DialogResult.Cancel;
        root.Controls.Add(UiLayout.ButtonRow(true, cancel, save), 0, 3);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var content = _txtContent.Text.Trim();
        if (content.Length == 0)
        {
            MessageBox.Show(this, "待办内容不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            _db.UpdateTodo(_editing.Id, content, _tag.Text);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
