namespace SedentaryReminder.Settings;

/// <summary>关于对话框（M5-7）：版本号、数据目录、打开数据目录。</summary>
public sealed class AboutForm : DpiForm
{
    public AboutForm(string dataDir)
    {
        Text = "关于 TickNote";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 190);
        Font = new Font("Microsoft YaHei UI", 9F);

        var version = AppInfo.Version;

        // 固定坐标布局：DpiForm.Scale 对固定 Bounds 是纯乘法，任何 DPI 路径下都不会错乱
        // （AutoSize + TableLayoutPanel 在 DPI 切换时会被中间态污染，小窗口不值得冒这个险）
        var lblTitle = new Label
        {
            Text = "TickNote（久坐提醒 + 工作流水记录）",
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            Bounds = new Rectangle(16, 14, 388, 22),
        };
        var lblVersion = new Label
        {
            Text = $"版本：{version}",
            ForeColor = SystemColors.GrayText,
            Bounds = new Rectangle(16, 44, 388, 22),
        };
        var lblPath = new Label
        {
            Text = "数据目录：" + dataDir,
            ForeColor = SystemColors.GrayText,
            Bounds = new Rectangle(16, 72, 388, 52), // 长路径在固定框内自动折行
        };
        var btnOpen = new Button { Text = "打开数据目录", Bounds = new Rectangle(16, 148, 120, 28) };
        btnOpen.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{dataDir}\""); } catch { }
        };
        var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Bounds = new Rectangle(318, 148, 86, 28) };

        Controls.Add(lblTitle);
        Controls.Add(lblVersion);
        Controls.Add(lblPath);
        Controls.Add(btnOpen);
        Controls.Add(btnOk);
        AcceptButton = btnOk;
    }
}
