namespace SedentaryReminder.Overlay;

/// <summary>遮罩收起期间的返回入口。关闭窗口也会返回提醒，不丢失草稿。</summary>
public sealed class ReminderAccessForm : DpiForm
{
    private readonly TextBox _draft;

    public ReminderAccessForm(string draft, Action chooseHistory)
    {
        Text = "提醒已收起 · 可切换其他应用";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        TopMost = true;
        Font = new Font("Microsoft YaHei UI", 9F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "输入已保留。现在可以到其他应用查看或复制内容。", AutoSize = true }, 0, 0);
        _draft = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = draft };
        root.Controls.Add(_draft, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var copy = new Button { Text = "复制当前内容", AutoSize = true };
        copy.Click += (_, _) =>
        {
            if (_draft.TextLength == 0) return;
            try { Clipboard.SetText(_draft.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "剪贴板正被占用，请稍后重试。", "复制失败"); }
        };
        var history = new Button { Text = "查看全部历史", AutoSize = true };
        history.Click += (_, _) => chooseHistory();
        var back = new Button { Text = "返回提醒", AutoSize = true };
        back.Click += (_, _) => Close();
        buttons.Controls.AddRange(new Control[] { copy, history, back });
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = back;
    }

    public void SetDraft(string text) => _draft.Text = text;
}
