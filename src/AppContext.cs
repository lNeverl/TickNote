using Microsoft.Win32;
using SedentaryReminder.Data;
using SedentaryReminder.Log;
using SedentaryReminder.Overlay;
using SedentaryReminder.Reminder;
using SedentaryReminder.Report;
using SedentaryReminder.Settings;

namespace SedentaryReminder;

/// <summary>
/// 应用主上下文：M0 引导（数据目录定位/向导）、配置与数据库加载、自动备份，
/// 并挂接 M1 提醒引擎、M2 全屏弹窗、M4 报告服务与 M5 托盘。
/// </summary>
public sealed class AppContext : ApplicationContext
{
    internal AppConfig Config = null!;
    internal Database Db = null!;
    internal ReminderEngine Engine = null!;
    internal OverlayManager Overlay = null!;
    internal ReportService Reports = null!;
    internal TrayIcon Tray = null!;

    // 非模态窗口单例：重复点菜单项时拉到前台，不再开新窗（两个窗口内容不一致会互相覆盖）
    private LogViewForm? _logViewToday;
    private LogViewForm? _logViewHistory;
    private ReportViewForm? _reportView;
    // 模态窗口单例：模态循环下托盘菜单仍可能触发菜单项
    private readonly Dictionary<Type, Form> _openDialogs = new();

    public AppContext(string dataDir)
    {
        // M0-8：数据目录自包含结构
        Directory.CreateDirectory(Path.Combine(dataDir, "exports"));
        Directory.CreateDirectory(Path.Combine(dataDir, "backup"));

        Config = AppConfig.Load(dataDir);
        Db = new Database(dataDir);
        BackupService.AutoBackupOnStartup(dataDir); // M6-4 每天首次启动自动备份
        AutoStart.EnsurePathCurrent(Config.AutoStart); // M5-4 EXE 路径变化自动修正

        Reports = new ReportService(Config, Db);

        Overlay = new OverlayManager(Config, Db);
        Overlay.Prewarm(); // 预热分层窗口/字体/JIT，首次弹窗背景与卡片同现不拖沓
        Overlay.Closed += action =>
        {
            Engine.NotifyOverlayClosed(action);
            // 保存成功给用户一个确定的反馈（M3-1 即写即存）
            if (Overlay.LastCloseSavedLog) Tray.ShowBalloon("已保存一条工作记录，可在“查看今日”中查看");
        };
        Engine = new ReminderEngine(Config);
        Engine.FireRequested += cycleStart =>
        {
            Engine.NotifyOverlayOpened();
            Overlay.Show(cycleStart);
        };

        // M1-6 锁屏暂停 / N4 显示器热插拔
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += (_, _) => Overlay.OnDisplaySettingsChanged();

        Tray = new TrayIcon(this);
        Engine.Start();
    }

    /// <summary>读取引导配置定位数据目录；缺失/失效时走首次向导（M0-1/M0-6）。</summary>
    internal static string? ResolveDataDir()
    {
        var boot = BootConfig.Load();
        if (boot != null) return boot.DataDir;

        using var wizard = new WizardForm();
        if (wizard.ShowDialog() != DialogResult.OK || wizard.SelectedDataDir == null)
            return null;

        BootConfig.Save(wizard.SelectedDataDir);
        return wizard.SelectedDataDir;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) Engine.SetLocked(true);
        else if (e.Reason == SessionSwitchReason.SessionUnlock) Engine.SetLocked(false);
    }

    // ---------------- 托盘菜单动作 ----------------

    /// <summary>已打开的窗口拉到前台（最小化先还原）；已关闭/未打开返回 false。</summary>
    private static bool TryActivate(Form? form)
    {
        if (form is not { IsDisposed: false }) return false;
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate();
        return true;
    }

    internal void ShowLogView(DateTime? fixedDate)
    {
        if (fixedDate.HasValue)
        {
            if (TryActivate(_logViewToday)) return;
            _logViewToday = new LogViewForm(this, fixedDate);
            _logViewToday.FormClosed += (_, _) => _logViewToday = null;
            _logViewToday.Show();
        }
        else
        {
            if (TryActivate(_logViewHistory)) return;
            _logViewHistory = new LogViewForm(this, null);
            _logViewHistory.FormClosed += (_, _) => _logViewHistory = null;
            _logViewHistory.Show();
        }
    }

    internal void ShowManualEntry() => ShowDialogOnce(() => new ManualEntryForm(Db, Config));

    internal void ShowReportView()
    {
        if (TryActivate(_reportView)) return;
        _reportView = new ReportViewForm(this);
        _reportView.FormClosed += (_, _) => _reportView = null;
        _reportView.Show();
    }

    internal void CopyDailyPrompt()
    {
        try
        {
            Clipboard.SetText(Reports.BuildDailyPrompt(DateTime.Today));
            Tray.ShowBalloon("日报提示词+今日流水已复制，请直接粘贴给 AI");
        }
        catch (Exception ex)
        {
            Tray.ShowBalloon("复制失败，剪贴板被其他程序占用：" + ex.Message);
        }
    }

    internal void CopyWeeklyPrompt()
    {
        try
        {
            Clipboard.SetText(Reports.BuildWeeklyPrompt(DateTime.Today));
            Tray.ShowBalloon("周报提示词+本周数据已复制，请直接粘贴给 AI");
        }
        catch (Exception ex)
        {
            Tray.ShowBalloon("复制失败，剪贴板被其他程序占用：" + ex.Message);
        }
    }

    /// <summary>模态窗口单例：已打开时拉到前台而不是再开一个。</summary>
    private void ShowDialogOnce<T>(Func<T> create) where T : Form
    {
        if (_openDialogs.TryGetValue(typeof(T), out var existing) && TryActivate(existing)) return;
        using var f = create();
        _openDialogs[typeof(T)] = f;
        try { f.ShowDialog(); }
        finally { _openDialogs.Remove(typeof(T)); }
    }

    internal void ShowImportReport() => ShowDialogOnce(() => new ImportReportForm(this));

    internal void ShowExport() => ShowDialogOnce(() => new ExportForm(this));

    internal void ShowSettings() => ShowDialogOnce(() => new SettingsForm(this));

    internal void ShowAbout() => ShowDialogOnce(() => new AboutForm(Config.DataDir));

    internal void PauseReminder(TimeSpan duration)
    {
        Engine.PauseUntil(DateTime.Now.Add(duration));
        Tray.ShowBalloon($"已暂停提醒，{DateTime.Now.Add(duration):HH:mm} 自动恢复");
    }

    internal void PauseReminderToday()
    {
        Engine.PauseRestOfToday();
        Tray.ShowBalloon("已暂停提醒到今天下班");
    }

    internal void ResumeReminder()
    {
        Engine.ResumeFromPause();
        Tray.ShowBalloon("已恢复提醒");
    }

    internal void RestartApplication()
    {
        Engine.Stop();
        Tray.Dispose();
        System.Windows.Forms.Application.Restart();
        ExitThread();
    }

    internal void ExitApplication()
    {
        Engine.Stop();
        Tray.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            Db.Dispose();
        }
        base.Dispose(disposing);
    }
}
