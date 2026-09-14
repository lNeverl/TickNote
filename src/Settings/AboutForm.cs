using System.Reflection;

namespace SedentaryReminder.Settings;

/// <summary>关于对话框（M5-7）：版本号、数据目录、打开数据目录。</summary>
public sealed class AboutForm : Form
{
    public AboutForm(string dataDir)
    {
        Text = "关于 久坐提醒";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 190);
        Font = new Font("Microsoft YaHei UI", 9F);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
        root.Controls.Add(new Label
        {
            Text = $"久坐提醒 + 工作流水记录  v{version}",
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        });
        root.Controls.Add(new Label
        {
            Text = "数据目录：" + dataDir,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14),
        });

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        var btnOpen = new Button { Text = "打开数据目录", AutoSize = true };
        btnOpen.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{dataDir}\""); } catch { }
        };
        var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
        btnRow.Controls.Add(btnOpen);
        btnRow.Controls.Add(btnOk);
        root.Controls.Add(btnRow);

        Controls.Add(root);
        AcceptButton = btnOk;
    }
}
