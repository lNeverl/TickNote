namespace SedentaryReminder.Settings;

public sealed class AboutPage : WorkspacePage
{
    public AboutPage(string dataDir)
    {
        Text = "关于 TickNote";
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "TickNote", AutoSize = true, Font = new Font(Font.FontFamily, 14F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14) });
        root.Controls.Add(new Label { Text = "久坐提醒、工作流水记录与每日待办", AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        root.Controls.Add(new Label { Text = "版本：" + AppInfo.Version, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 14) });
        root.Controls.Add(new Label { Text = "数据目录", AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        root.Controls.Add(new TextBox { Text = dataDir, ReadOnly = true, Dock = DockStyle.Fill, AccessibleName = "数据保存目录", Margin = new Padding(0, 0, 0, 14) });
        var open = UiLayout.Button("打开数据目录");
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{dataDir}\""); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "打开目录失败：" + ex.Message, "提示"); }
        };
        root.Controls.Add(open);
        Controls.Add(root);
    }
}
