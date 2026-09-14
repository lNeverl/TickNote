using SedentaryReminder.Reminder;

namespace SedentaryReminder.Settings;

/// <summary>
/// 系统托盘（M5-1/2/3）：常驻图标 + 完整右键菜单 + 状态图标（绿/黄/灰）与悬停文字。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppContext _app;
    private readonly NotifyIcon _icon;
    private readonly Icon _green;
    private readonly Icon _yellow;
    private readonly Icon _gray;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private string _lastStatus = "";

    public TrayIcon(AppContext app)
    {
        _app = app;
        _green = MakeIcon(Color.FromArgb(46, 160, 60));
        _yellow = MakeIcon(Color.FromArgb(230, 180, 30));
        _gray = MakeIcon(Color.FromArgb(150, 150, 150));

        var menu = new ContextMenuStrip();
        menu.Items.Add("查看今日", null, (_, _) => _app.ShowLogView(DateTime.Today));
        menu.Items.Add("历史流水", null, (_, _) => _app.ShowLogView(null));
        menu.Items.Add("补记一条", null, (_, _) => _app.ShowManualEntry());
        menu.Items.Add("我的报告", null, (_, _) => _app.ShowReportView());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("生成日报（复制提示词）", null, (_, _) => _app.CopyDailyPrompt());
        menu.Items.Add("生成周报（复制提示词）", null, (_, _) => _app.CopyWeeklyPrompt());
        menu.Items.Add("导入报告", null, (_, _) => _app.ShowImportReport());
        menu.Items.Add("导出原始流水…", null, (_, _) => _app.ShowExport());
        menu.Items.Add(new ToolStripSeparator());

        var pauseMenu = new ToolStripMenuItem("暂停提醒");
        pauseMenu.DropDownItems.Add("30 分钟", null, (_, _) => _app.PauseReminder(TimeSpan.FromMinutes(30)));
        pauseMenu.DropDownItems.Add("1 小时", null, (_, _) => _app.PauseReminder(TimeSpan.FromHours(1)));
        pauseMenu.DropDownItems.Add("今天下班前", null, (_, _) => _app.PauseReminderToday());
        menu.Items.Add(pauseMenu);
        menu.Items.Add("恢复提醒", null, (_, _) => _app.ResumeReminder());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("设置", null, (_, _) => _app.ShowSettings());
        menu.Items.Add("关于", null, (_, _) => _app.ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _app.ExitApplication());

        _icon = new NotifyIcon
        {
            Text = "久坐提醒",
            Icon = _green,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _app.ShowLogView(DateTime.Today);

        _app.Engine.StatusChanged += UpdateStatus;
        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();
    }

    /// <summary>M5-3：按引擎状态切换图标颜色与悬停文字。</summary>
    private void UpdateStatus()
    {
        var (state, text) = _app.Engine.GetStatus();
        if (text == _lastStatus) return;
        _lastStatus = text;
        _icon.Icon = state switch
        {
            EngineTrayState.Counting => _green,
            EngineTrayState.Soon => _yellow,
            _ => _gray,
        };
        // 托盘悬停文字上限 63 字符
        _icon.Text = text.Length > 60 ? "久坐提醒\n" + text[..56] + "…" : "久坐提醒\n" + text;
    }

    /// <summary>操作结果的气泡提示（如"提示词已复制"）。</summary>
    public void ShowBalloon(string message)
        => _icon.ShowBalloonTip(2500, "久坐提醒", message, ToolTipIcon.Info);

    /// <summary>运行期生成纯色圆点图标，避免引入 .resx 资源（H2）。</summary>
    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _statusTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _green.Dispose();
        _yellow.Dispose();
        _gray.Dispose();
    }
}
