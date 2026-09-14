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

    internal void ShowLogView(DateTime? fixedDate)
        => new LogViewForm(this, fixedDate).Show();

    internal void ShowManualEntry()
    {
        using var f = new ManualEntryForm(Db, Config);
        f.ShowDialog();
    }

    internal void ShowReportView()
        => new ReportViewForm(this).Show();

    internal void CopyDailyPrompt()
    {
        Clipboard.SetText(Reports.BuildDailyPrompt(DateTime.Today));
        Tray.ShowBalloon("日报提示词+今日流水已复制，请直接粘贴给 AI");
    }

    internal void CopyWeeklyPrompt()
    {
        Clipboard.SetText(Reports.BuildWeeklyPrompt(DateTime.Today));
        Tray.ShowBalloon("周报提示词+本周数据已复制，请直接粘贴给 AI");
    }

    internal void ShowImportReport()
    {
        using var f = new ImportReportForm(this);
        f.ShowDialog();
    }

    internal void ShowExport()
    {
        using var f = new ExportForm(this);
        f.ShowDialog();
    }

    internal void ShowSettings()
    {
        using var f = new SettingsForm(this);
        f.ShowDialog();
    }

    internal void ShowAbout()
    {
        using var f = new AboutForm(Config.DataDir);
        f.ShowDialog();
    }

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
