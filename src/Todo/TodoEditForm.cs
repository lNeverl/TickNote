using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

/// <summary>编辑单条待办内容，所属日期和完成状态保持不变。</summary>
public sealed class TodoEditForm : DpiForm
{
    private readonly Database _db;
    private readonly TodoEntry _editing;
    private readonly TextBox _txtContent;

    public TodoEditForm(Database db, TodoEntry editing)
    {
        _db = db;
        _editing = editing;
        Text = "编辑待办";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 250);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
        _txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Text = editing.Content,
        };
        root.Controls.Add(_txtContent, 0, 1);

        var save = UiLayout.Button("保存", primary: true);
        save.Click += OnSave;
        var cancel = UiLayout.Button("取消");
        cancel.DialogResult = DialogResult.Cancel;
        root.Controls.Add(UiLayout.ButtonRow(true, cancel, save), 0, 2);
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
            _db.UpdateTodoContent(_editing.Id, content);
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
