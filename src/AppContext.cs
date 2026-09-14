using SedentaryReminder.Data;
using SedentaryReminder.Settings;

namespace SedentaryReminder;

/// <summary>
/// 应用主上下文：负责 M0 引导（数据目录定位/向导）、配置与数据库加载、自动备份。
/// 后续里程碑在此挂接提醒引擎、弹窗与托盘。
/// </summary>
public sealed class AppContext : ApplicationContext
{
    // 用户取消向导时构造函数提前退出，字段保持未赋值，随后线程即结束，不会被访问
    private readonly AppConfig _config = null!;
    private readonly Database _db = null!;

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

        _config = AppConfig.Load(dataDir);
        _db = new Database(dataDir);
        BackupService.AutoBackupOnStartup(dataDir);

        MessageBox.Show($"数据目录：{dataDir}\n工程骨架 + M0 + M6 数据层就绪。",
            "SedentaryReminder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        ExitThread();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db.Dispose();
        base.Dispose(disposing);
    }
}
