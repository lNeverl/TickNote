using SedentaryReminder.Reminder;

namespace SedentaryReminder.Settings;

/// <summary>
/// 系统托盘（M5-1/2/3）：常驻图标 + 完整右键菜单 + 状态颜色（绿/黄/灰）与悬停文字。
/// 图标是软件 logo，外圈叠倒计时进度环：从 12 点方向顺时针填充，表示距下次提醒的进度，
/// 环的颜色随状态变化（绿=计时中，黄=临近，灰=推迟中）；
/// 暂停/锁屏/非工作时段等没有进行中倒计时的状态不画环，只显示 logo。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppContext _app;
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly Bitmap? _logo;
    private Icon? _currentIcon;
    private string _lastStatus = "";
    private EngineTrayState _lastState = EngineTrayState.Idle;
    private int _lastProgressPct = -1;

    private static readonly Color Green = Color.FromArgb(46, 160, 60);
    private static readonly Color Yellow = Color.FromArgb(230, 180, 30);
    private static readonly Color Gray = Color.FromArgb(150, 150, 150);

    public TrayIcon(AppContext app)
    {
        _app = app;
        _logo = LoadLogo();

        var menu = new ContextMenuStrip();
        // 版本抬头：EXE 文件名固定为 TickNote.exe 后，这里是识别当前运行版本最顺手的位置
        menu.Items.Add($"TickNote v{AppInfo.Version}").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("查看今日", null, (_, _) => _app.ShowLogView(DateTime.Today));
        menu.Items.Add("每日待办", null, (_, _) => _app.ShowTodoView());
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
            Text = "TickNote",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _app.ShowLogView(DateTime.Today);

        _app.Engine.StatusChanged += UpdateStatus;
        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();
    }

    private static readonly string HoverPrefix = $"TickNote v{AppInfo.Version}\n";

    /// <summary>M5-3：刷新悬停文字与图标（状态色 + 倒计时进度环）。</summary>
    private void UpdateStatus()
    {
        var (state, text) = _app.Engine.GetStatus();
        if (text != _lastStatus)
        {
            _lastStatus = text;
            // 托盘悬停文字上限 63 字符；抬头带版本号，鼠标悬停即可识别当前版本
            const int maxHover = 63;
            var hover = HoverPrefix + text;
            _icon.Text = hover.Length <= maxHover ? hover : hover[..(maxHover - 1)] + "…";
        }

        // 进度量化到 1%，状态和进度都没变就不重绘，避免托盘图标闪烁
        var progress = _app.Engine.GetCountdownProgress();
        var pct = progress is double p ? (int)Math.Round(p * 100) : -1;
        if (_currentIcon != null && state == _lastState && pct == _lastProgressPct) return;
        _lastState = state;
        _lastProgressPct = pct;
        var color = state switch
        {
            EngineTrayState.Counting => Green,
            EngineTrayState.Soon => Yellow,
            _ => Gray,
        };
        SwapIcon(MakeIcon(color, progress));
    }

    /// <summary>操作结果的气泡提示（如"提示词已复制"）。</summary>
    public void ShowBalloon(string message)
        => _icon.ShowBalloonTip(2500, "TickNote", message, ToolTipIcon.Info);

    /// <summary>从嵌入资源读 logo 原图（SedentaryReminder.Assets.TickNote.png），供进度环图标叠画。</summary>
    private static Bitmap? LoadLogo()
    {
        var s = typeof(TrayIcon).Assembly.GetManifestResourceStream("SedentaryReminder.Assets.TickNote.png");
        return s != null ? new Bitmap(s) : null;
    }

    /// <summary>
    /// 运行期绘制托盘图标：logo 居中；有倒计时进度时外圈加状态色进度环
    /// （淡色整圈作底，实色弧从 12 点方向顺时针填充）。
    /// </summary>
    private Icon MakeIcon(Color ringColor, double? progress)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            if (progress is double p)
            {
                using var trackPen = new Pen(Color.FromArgb(70, ringColor), 4);
                g.DrawEllipse(trackPen, 2, 2, size - 5, size - 5);
                if (p > 0)
                {
                    using var arcPen = new Pen(ringColor, 4);
                    g.DrawArc(arcPen, 2, 2, size - 5, size - 5, -90, (float)(p * 360));
                }
                if (_logo != null) g.DrawImage(_logo, 7, 7, size - 15, size - 15);
            }
            else if (_logo != null)
            {
                g.DrawImage(_logo, 1, 1, size - 2, size - 2);
            }
            else
            {
                // logo 资源缺失时退回纯色圆点
                using var brush = new SolidBrush(ringColor);
                g.FillEllipse(brush, 2, 2, size - 5, size - 5);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>
    /// 换图标。Icon.FromHandle 创建的句柄不归 Icon 对象所有，Dispose 不会释放它，
    /// 必须 DestroyIcon，否则进度环每次重绘都会泄漏一个 GDI 句柄。
    /// </summary>
    private void SwapIcon(Icon next)
    {
        var old = _currentIcon;
        _icon.Icon = next;
        _currentIcon = next;
        if (old == null) return;
        var h = old.Handle;
        old.Dispose();
        NativeMethods.DestroyIcon(h);
    }

    public void Dispose()
    {
        _statusTimer.Dispose();
        _icon.Visible = false;
        if (_currentIcon != null)
        {
            var h = _currentIcon.Handle;
            _icon.Icon = null;
            _currentIcon.Dispose();
            NativeMethods.DestroyIcon(h);
            _currentIcon = null;
        }
        _icon.Dispose();
        _logo?.Dispose();
    }
}
