namespace SedentaryReminder;

/// <summary>流水相关窗口共用的按钮尺寸、颜色和操作区间距；尺寸按 96 DPI 定义。</summary>
internal static class UiLayout
{
    internal static readonly Color PageBack = Color.FromArgb(248, 249, 251);
    internal static readonly Color Accent = Color.FromArgb(40, 90, 160);

    internal static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(76, 32), Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 0), Anchor = AnchorStyles.Left,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(45, 52, 64),
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(205, 211, 220);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(50, 105, 180) : Color.FromArgb(235, 242, 251);
        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? primary ? Accent : Color.White : Color.FromArgb(240, 242, 246);
            button.ForeColor = button.Enabled ? primary ? Color.White : Color.FromArgb(45, 52, 64) : SystemColors.GrayText;
            button.FlatAppearance.BorderColor = button.Enabled && primary ? Accent : Color.FromArgb(205, 211, 220);
        };
        return button;
    }

    internal static FlowLayoutPanel ButtonRow(bool rightAligned, params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = rightAligned ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0),
        };
        foreach (var button in rightAligned ? buttons.Reverse() : buttons)
        {
            button.Margin = rightAligned ? new Padding(8, 0, 0, 0) : new Padding(0, 0, 8, 0);
            row.Controls.Add(button);
        }
        return row;
    }

    internal static TableLayoutPanel EqualButtonRow(params Button[] buttons)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = buttons.Length * 2 - 1, RowCount = 1, Margin = new Padding(0, 12, 0, 0),
        };
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < row.ColumnCount; i++)
            row.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.Percent : SizeType.Absolute, i % 2 == 0 ? 1 : 8));
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Dock = DockStyle.Fill;
            buttons[i].Margin = Padding.Empty;
            row.Controls.Add(buttons[i], i * 2, 0);
        }
        return row;
    }
}
