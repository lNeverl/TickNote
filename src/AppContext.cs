using Microsoft.Win32;
using SedentaryReminder.Data;
using SedentaryReminder.Overlay;
using SedentaryReminder.Reminder;
using SedentaryReminder.Settings;

namespace SedentaryReminder;

/// <summary>
/// 应用主上下文：M0 引导（数据目录定位/向导）、配置与数据库加载、自动备份，
/// 并挂接 M1 提醒引擎、M2 全屏弹窗与 M5 托盘。
/// </summary>
public sealed class AppContext : ApplicationContext
{
    internal AppConfig Config = null!;
    internal Database Db = null!;
    internal ReminderEngine Engine = null!;
    internal OverlayManager Overlay = null!;
    private NotifyIcon _tray = null!;

    public AppContext()
    {
        var dataDir = ResolveDataDir();
        if (dataDir == null)
        {
            ExitThread();
            return;
        }

        // M0-8：数据目录自包含结构
        Directory.CreateDirectory(Path.Combine(dataDir, "exports"));
        Directory.CreateDirectory(Path.Combine(dataDir, "backup"));

        Config = AppConfig.Load(dataDir);
        Db = new Database(dataDir);
        BackupService.AutoBackupOnStartup(dataDir); // M6-4 每天首次启动自动备份

        Overlay = new OverlayManager(Config, Db);
        Overlay.Closed += action =>
        {
            Engine.NotifyOverlayClosed(action);
            // 关窗后今日流水可能新增，无其他界面需要即时刷新
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

        BuildTray();
        Engine.Start();
    }

    /// <summary>读取引导配置定位数据目录；缺失/失效时走首次向导（M0-1/M0-6）。</summary>
    private static string? ResolveDataDir()
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

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        _tray = new NotifyIcon
        {
            Text = "久坐提醒",
            Icon = SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    internal void ExitApplication()
    {
        Engine.Stop();
        _tray.Visible = false;
        _tray.Dispose();
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
