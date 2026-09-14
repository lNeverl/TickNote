using Microsoft.Data.Sqlite;

namespace SedentaryReminder.Data;

/// <summary>
/// 自动/手动备份（M6-4/M6-5）：用 SQLite 在线备份 API，复制出的库保证一致。
/// </summary>
public static class BackupService
{
    private const int KeepCount = 7;

    public static string BackupDir(string dataDir) => Path.Combine(dataDir, "backup");

    /// <summary>每天首次启动备份一次（当天已有备份则跳过），只保留最近 7 份。</summary>
    public static void AutoBackupOnStartup(string dataDir)
    {
        try
        {
            var dbPath = Path.Combine(dataDir, "data.db");
            if (!File.Exists(dbPath)) return;
            var dir = BackupDir(dataDir);
            Directory.CreateDirectory(dir);
            var todayPrefix = "data_" + DateTime.Now.ToString("yyyyMMdd");
            if (Directory.GetFiles(dir, todayPrefix + "*.db").Length > 0) return;
            DoBackup(dataDir);
            PruneOld(dir);
        }
        catch
        {
            // 备份失败不影响主流程，下次启动再试
        }
    }

    /// <summary>立即备份，返回备份文件路径。</summary>
    public static string DoBackup(string dataDir)
    {
        var dir = BackupDir(dataDir);
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, "data_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".db");
        ExportDatabase(dataDir, dst);
        PruneOld(dir);
        return dst;
    }

    /// <summary>将运行中的数据库一致地导出到指定文件，包含尚未 checkpoint 的 WAL 记录。</summary>
    public static void ExportDatabase(string dataDir, string filePath)
    {
        var src = Path.GetFullPath(Path.Combine(dataDir, "data.db"));
        var dst = Path.GetFullPath(filePath);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            throw new IOException("导出位置不能是正在使用的数据库文件。");

        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = src,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dst,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        source.Open();
        dest.Open();
        source.BackupDatabase(dest);
    }

    private static void PruneOld(string dir)
    {
        var files = Directory.GetFiles(dir, "data_*.db")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .ToList();
        foreach (var f in files.Skip(KeepCount))
        {
            try { File.Delete(f); } catch { /* 占用时跳过，下次清理 */ }
        }
    }
}
