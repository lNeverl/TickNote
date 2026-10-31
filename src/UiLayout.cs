namespace SedentaryReminder;

/// <summary>流水相关窗口共用的按钮尺寸、颜色和操作区间距；尺寸按 96 DPI 定义。</summary>
internal static class UiLayout
{
    internal static readonly Color PageBack = Color.FromArgb(248, 249, 251);
    internal static readonly Color Accent = Color.FromArgb(40, 90, 160);
    internal static readonly Color Surface = Color.White;
    internal static readonly Color Sidebar = Color.FromArgb(240, 242, 245);
    internal static readonly Color Text = Color.FromArgb(37, 45, 57);
    internal static readonly Color Muted = Color.FromArgb(105, 117, 135);
    internal static readonly Color Border = Color.FromArgb(220, 225, 232);
    internal static readonly Color Selected = Color.FromArgb(232, 239, 249);

    internal static int Scale(Control control, int value) => (int)Math.Round(value * control.DeviceDpi / 96.0);
    internal static string MultilineText(string? value) => (value ?? "").ReplaceLineEndings(Environment.NewLine);

    /// <summary>只统一外观，不在主题遍历中修改控件尺寸或触发二次 DPI 缩放。</summary>
    internal static void ApplyTheme(Control root)
    {
        root.ForeColor = Text;
        foreach (Control control in root.Controls)
        {
            if (control is Label label && (label.ForeColor == SystemColors.GrayText || label.ForeColor == Color.Gray))
                label.ForeColor = Muted;
            if (control is TextBoxBase text)
            {
                if (text.BorderStyle != BorderStyle.None) text.BorderStyle = BorderStyle.FixedSingle;
                text.BackColor = Surface;
                text.ForeColor = Text;
            }
            if (control is ListBox list)
            {
                list.BorderStyle = BorderStyle.None;
                list.BackColor = Surface;
                list.IntegralHeight = false;
            }
            if (control is TabPage tab) tab.BackColor = Surface;
            if (control is Button button && button.AutoSize)
            {
                button.FlatStyle = FlatStyle.Flat;
                var primary = button.BackColor == Accent;
                button.UseVisualStyleBackColor = false;
                button.BackColor = primary ? Accent : Surface;
                button.ForeColor = primary ? Color.White : Text;
                button.FlatAppearance.BorderColor = primary ? Accent : Border;
                button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(50, 105, 180) : Selected;
            }
            ApplyThemeChildren(control);
        }
    }

    private static void ApplyThemeChildren(Control control)
    {
        // ApplyTheme 的根前景色赋值会覆盖辅助说明，保留它再继续处理子树。
        var fore = control.ForeColor;
        ApplyTheme(control);
        control.ForeColor = fore;
    }

    internal static Panel SurfaceHost(Control content, int padding = 16)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(padding), Margin = Padding.Empty };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Paint += (_, e) => { using var pen = new Pen(Border); e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, panel.Width - 1), Math.Max(0, panel.Height - 1)); };
        return panel;
    }

    internal static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(76, 32), Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 8, 0), Anchor = AnchorStyles.Left,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : Text,
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
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
