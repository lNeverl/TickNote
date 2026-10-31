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

    private MainForm? _mainWindow;
    private readonly bool _startServices;
    private bool _exiting;
    private readonly Control? _eventDispatcher;
    internal MainForm? MainWindow => _mainWindow;

    public AppContext(string dataDir) : this(dataDir, startServices: true) { }

    internal AppContext(string dataDir, bool startServices)
    {
        _startServices = startServices;
        // M0-8：数据目录自包含结构
        Directory.CreateDirectory(Path.Combine(dataDir, "exports"));
        Directory.CreateDirectory(Path.Combine(dataDir, "backup"));

        Config = AppConfig.Load(dataDir);
        Db = new Database(dataDir);
        BackupService.AutoBackupOnStartup(dataDir); // M6-4 每天首次启动自动备份
        if (startServices) AutoStart.EnsurePathCurrent(Config.AutoStart); // M5-4 EXE 路径变化自动修正

        Reports = new ReportService(Config, Db);

        Overlay = new OverlayManager(Config, Db);
        if (startServices) Overlay.Prewarm(); // 预热分层窗口/字体/JIT，首次弹窗背景与卡片同现不拖沓
        Overlay.Closed += action =>
        {
            Engine.NotifyOverlayClosed(action);
            // 保存成功给用户一个确定的反馈（M3-1 即写即存）
            if (Overlay.LastCloseSavedLog) Notify("已保存一条工作记录，可在“查看今日”中查看");
        };
        Engine = new ReminderEngine(Config);
        Engine.FireRequested += OpenReminder;

        if (startServices)
        {
            // 系统事件可能来自后台线程，统一送回创建 WinForms 窗口的线程。
            _eventDispatcher = new Control();
            _ = _eventDispatcher.Handle;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Tray = new TrayIcon(this);
            Engine.Start();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => OnUiThread(Overlay.OnDisplaySettingsChanged);

    private void OnUiThread(Action action)
    {
        var dispatcher = _eventDispatcher;
        if (_exiting || dispatcher == null || dispatcher.IsDisposed) return;
        if (!dispatcher.InvokeRequired) { action(); return; }
        try { dispatcher.BeginInvoke((MethodInvoker)(() => { if (!_exiting) action(); })); }
        catch (InvalidOperationException) when (_exiting || dispatcher.IsDisposed) { }
    }

    internal void OpenReminder(DateTime cycleStart)
    {
        if (_exiting || Overlay.IsOpen) return;
        Engine.NotifyOverlayOpened();
        try { Overlay.Show(cycleStart); }
        catch (Exception ex)
        {
            // 显示失败时回退引擎状态，避免永久停在“弹窗显示中”。
            Engine.NotifyOverlayClosed("postpone");
            Notify("提醒界面未能打开，已推迟后重试：" + ex.Message);
        }
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
        OnUiThread(() =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) Engine.SetLocked(true);
            else if (e.Reason == SessionSwitchReason.SessionUnlock) Engine.SetLocked(false);
        });
    }

    // ---------------- 托盘菜单动作 ----------------

    internal void ShowMainWindow(MainPage? page = null)
    {
        if (_exiting) return;
        if (_mainWindow is not { IsDisposed: false }) _mainWindow = new MainForm(this);
        if (page.HasValue) _mainWindow.Navigate(page.Value);
        _mainWindow.Show();
        if (_mainWindow.WindowState == FormWindowState.Minimized) _mainWindow.WindowState = FormWindowState.Normal;
        _mainWindow.Activate();
    }

    internal void ShowLogView(DateTime? fixedDate)
    {
        ShowMainWindow(MainPage.History);
        (_mainWindow!.CurrentPage as LogPage)?.SetDateScope(fixedDate);
    }

    internal void ShowManualEntry() => ShowMainWindow(MainPage.ManualEntry);
    internal void ShowTodoView() => ShowMainWindow(MainPage.Todos);
    internal void ShowReportView() => ShowMainWindow(MainPage.Reports);
    internal void ShowImportReport() => ShowMainWindow(MainPage.ImportReport);
    internal void ShowExport() => ShowMainWindow(MainPage.Export);
    internal void ShowSettings() => ShowMainWindow(MainPage.Settings);
    internal void ShowAbout() => ShowMainWindow(MainPage.About);

    internal void Notify(string message)
    {
        Tray?.ShowBalloon(message);
        _mainWindow?.ShowNotification(message);
    }

    internal void ApplyAutoStart(bool enabled) { if (_startServices) AutoStart.Apply(enabled); }
    internal bool ConfirmExit(WorkspacePage? except = null) => _mainWindow?.ConfirmExit(except) ?? true;

    internal bool PrepareDataMigration()
    {
        if (!Overlay.TrySavePendingDraft()) return false;
        _mainWindow?.SaveWindowState();
        return true;
    }

    internal void CopyDailyPrompt()
    {
        try
        {
            Clipboard.SetText(Reports.BuildDailyPrompt(DateTime.Today));
            Notify("日报提示词+今日流水已复制，请直接粘贴给 AI");
        }
        catch (Exception ex)
        {
            Notify("复制失败，剪贴板被其他程序占用：" + ex.Message);
        }
    }

    internal void CopyWeeklyPrompt()
    {
        try
        {
            Clipboard.SetText(Reports.BuildWeeklyPrompt(DateTime.Today));
            Notify("周报提示词+本周数据已复制，请直接粘贴给 AI");
        }
        catch (Exception ex)
        {
            Notify("复制失败，剪贴板被其他程序占用：" + ex.Message);
        }
    }

    internal void PauseReminder(TimeSpan duration)
    {
        Engine.PauseUntil(DateTime.Now.Add(duration));
        Notify($"已暂停提醒，{DateTime.Now.Add(duration):HH:mm} 自动恢复");
    }

    internal void PauseReminderToday()
    {
        Engine.PauseRestOfToday();
        Notify("已暂停提醒到今天下班");
    }

    internal void ResumeReminder()
    {
        Engine.ResumeFromPause();
        Notify("已恢复提醒");
    }

    internal void RestartApplication(bool confirmChanges = true) => Shutdown(restart: true, confirmChanges);
    internal void ExitApplication() => Shutdown(restart: false, confirmChanges: true);

    private void Shutdown(bool restart, bool confirmChanges)
    {
        if (_exiting || confirmChanges && !ConfirmExit()) return;
        if (!Overlay.TryCloseForShutdown()) return;
        _exiting = true;
        _mainWindow?.CloseForExit();
        Engine.Stop();
        Tray?.Dispose();
        Tray = null!;
        if (restart) System.Windows.Forms.Application.Restart();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exiting = true;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Engine.Dispose();
            _eventDispatcher?.Dispose();
            Overlay.Dispose();
            _mainWindow?.Dispose();
            Tray?.Dispose();
            Db.Dispose();
        }
        base.Dispose(disposing);
    }
}
