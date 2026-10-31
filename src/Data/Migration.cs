using Microsoft.Data.Sqlite;

namespace SedentaryReminder.Data;

/// <summary>
/// 数据目录迁移（M0-7）：用 SQLite 在线备份 API 把当前库一致地复制到新目录，
/// 连同 config.json / ui-state.json / exports / backup 一起搬走，旧目录保留作为兜底。
/// </summary>
public static class Migration
{
    public static void MigrateTo(string currentDataDir, string newDir)
    {
        Directory.CreateDirectory(newDir);

        var srcDb = Path.Combine(currentDataDir, "data.db");
        if (File.Exists(srcDb))
        {
            using var src = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = srcDb,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            using var dst = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(newDir, "data.db"),
            }.ToString());
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }

        CopyIfExists(Path.Combine(currentDataDir, "config.json"), Path.Combine(newDir, "config.json"));
        CopyIfExists(Path.Combine(currentDataDir, "ui-state.json"), Path.Combine(newDir, "ui-state.json"));
        CopyDirIfExists(Path.Combine(currentDataDir, "exports"), Path.Combine(newDir, "exports"));
        CopyDirIfExists(Path.Combine(currentDataDir, "backup"), Path.Combine(newDir, "backup"));
    }

    private static void CopyIfExists(string src, string dst)
    {
        if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
    }

    private static void CopyDirIfExists(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            try { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true); }
            catch { /* 单个文件占用不阻断整体迁移 */ }
        }
    }
}
