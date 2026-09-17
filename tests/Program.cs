using Microsoft.Data.Sqlite;
using SedentaryReminder;
using SedentaryReminder.Data;
using SedentaryReminder.Reminder;
using SedentaryReminder.Report;

var testDir = Path.Combine(Path.GetTempPath(), "SedentaryReminderRegression_" + Guid.NewGuid().ToString("N") + ";data");
Directory.CreateDirectory(testDir);
try
{
    using var db = new Database(testDir);
    var entry = new WorkLogEntry
    {
        LogDate = "2026-09-14",
        StartTime = "09:00",
        EndTime = "09:30",
        Content = "修复测试",
        Source = "manual",
    };
    entry.Id = db.InsertWorkLog(entry);
    entry.LogDate = "2026-09-15";
    db.UpdateWorkLog(entry);
    Check(db.GetLogs("2026-09-14").Count == 0, "编辑日期后旧日期仍有记录");
    Check(db.GetLogs("2026-09-15").Count == 1, "编辑日期后新日期没有记录");

    var exported = Path.Combine(testDir, "export.db");
    BackupService.ExportDatabase(testDir, exported);
    using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = exported,
        Mode = SqliteOpenMode.ReadOnly,
    }.ToString()))
    {
        copy.Open();
        using var cmd = copy.CreateCommand();
        cmd.CommandText = "SELECT log_date FROM work_log WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", entry.Id);
        Check((string?)cmd.ExecuteScalar() == "2026-09-15", "在线导出遗漏了最新 WAL 记录");
    }

    var migratedDir = Path.Combine(testDir, "migrated;data");
    Migration.MigrateTo(testDir, migratedDir);
    using (var migrated = new Database(migratedDir))
        Check(migrated.GetLogs("2026-09-15").Count == 1, "特殊字符目录迁移后记录丢失");

    var config = new AppConfig { DataDir = testDir, WeekStartsMonday = false };
    var reports = new ReportService(config, db);
    CheckWeek(reports, new DateTime(2026, 9, 13));
    CheckWeek(reports, new DateTime(2024, 12, 29));
    for (var date = new DateTime(2023, 1, 1); date < new DateTime(2030, 1, 1); date = date.AddDays(1))
        CheckWeek(reports, date);
    config.WeekStartsMonday = true;
    CheckWeek(reports, new DateTime(2026, 12, 31));
    for (var date = new DateTime(2023, 1, 1); date < new DateTime(2030, 1, 1); date = date.AddDays(1))
        CheckWeek(reports, date);

    var first = reports.SaveImportedReport("daily", "2026-09-15", "第一份", null);
    var second = reports.SaveImportedReport("daily", "2026-09-15", "第二份", null);
    var exportDir = Path.Combine(testDir, "exports");
    Check(File.ReadAllText(Path.Combine(exportDir, $"daily_2026-09-15_{first.Id}.md")) == "第一份",
        "第一份报告副本丢失");
    Check(File.ReadAllText(Path.Combine(exportDir, $"daily_2026-09-15_{second.Id}.md")) == "第二份",
        "第二份报告副本丢失");
    reports.DeleteReport(second);
    Check(!File.Exists(Path.Combine(exportDir, $"daily_2026-09-15_{second.Id}.md")),
        "删除报告后副本仍在");
    Check(File.ReadAllText(Path.Combine(exportDir, "daily_2026-09-15.md")) == "第一份",
        "删除最新报告后旧版路径没有恢复上一份");
    reports.DeleteReport(first);
    Check(!File.Exists(Path.Combine(exportDir, "daily_2026-09-15.md")),
        "删除最后一份报告后旧版副本仍在");

    CheckDpiScaling();

    Console.WriteLine("回归检查通过：流水日期、WAL 导出与迁移、周报期次、报告副本、DPI 往返缩放。");
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(testDir, recursive: true);
}

static void CheckWeek(ReportService reports, DateTime date)
{
    var range = reports.GetWeekRange(date);
    Check(reports.ParseWeekKey(range.Key) == range.From, $"周报期次无法还原：{range.Key}");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void CheckDpiScaling()
{
    using var form = new DpiProbeForm();
    var originalClient = form.ClientSize;
    var originalMinimum = form.MinimumSize;
    var originalButtonBounds = form.ProbeButton.Bounds;

    form.ApplyInitialDpi(192);
    CheckNear(form.ClientSize.Width, originalClient.Width * 2, 2, "200% DPI 下窗体宽度不是单次缩放");
    CheckNear(form.ClientSize.Height, originalClient.Height * 2, 2, "200% DPI 下窗体高度不是单次缩放");
    CheckNear(form.ProbeButton.Width, originalButtonBounds.Width * 2, 1, "200% DPI 下控件宽度缩放错误");
    CheckNear(form.ProbeButton.Left, originalButtonBounds.Left * 2, 1, "200% DPI 下控件位置缩放错误");

    var scaledClient = form.ClientSize;
    form.ApplyInitialDpi(192);
    Check(form.ClientSize == scaledClient, "重复应用同一 DPI 时发生二次缩放");

    form.ApplyInitialDpi(96);
    CheckNear(form.ClientSize.Width, originalClient.Width, 2, "DPI 降回 100% 后窗体宽度没有恢复");
    CheckNear(form.ClientSize.Height, originalClient.Height, 2, "DPI 降回 100% 后窗体高度没有恢复");
    CheckNear(form.MinimumSize.Width, originalMinimum.Width, 2, "DPI 往返后最小宽度发生漂移");
    CheckNear(form.MinimumSize.Height, originalMinimum.Height, 2, "DPI 往返后最小高度发生漂移");
    CheckNear(form.ProbeButton.Width, originalButtonBounds.Width, 1, "DPI 往返后控件宽度发生漂移");
    CheckNear(form.ProbeButton.Left, originalButtonBounds.Left, 1, "DPI 往返后控件位置发生漂移");

    // 模拟 Form.OnDpiChanged 已经应用系统建议的外层尺寸后，本类只应缩放子控件。
    var frameworkScaledClient = new Size(originalClient.Width * 2, originalClient.Height * 2);
    form.ClientSize = frameworkScaledClient;
    form.ApplyChildDpi(192);
    Check(form.ClientSize == frameworkScaledClient, "跨屏 DPI 变化时外层窗体被重复缩放");
    CheckNear(form.ProbeButton.Width, originalButtonBounds.Width * 2, 1, "跨屏 DPI 变化时子控件没有缩放");

    form.ClientSize = originalClient;
    form.ApplyChildDpi(96);
    Check(form.ClientSize == originalClient, "DPI 降回 100% 时外层窗体被重复缩小");
    CheckNear(form.ProbeButton.Width, originalButtonBounds.Width, 1, "DPI 降回 100% 时子控件没有恢复");
}

static void CheckNear(int actual, int expected, int tolerance, string message)
{
    Check(Math.Abs(actual - expected) <= tolerance, $"{message}：期望 {expected}±{tolerance}，实际 {actual}");
}

sealed class DpiProbeForm : DpiForm
{
    internal Button ProbeButton { get; } = new()
    {
        Bounds = new Rectangle(20, 24, 100, 32),
    };

    internal DpiProbeForm()
    {
        ClientSize = new Size(400, 300);
        MinimumSize = new Size(320, 240);
        Controls.Add(ProbeButton);
    }

    internal void ApplyInitialDpi(int dpi) => ApplyDpi(dpi, scaleForm: true);

    internal void ApplyChildDpi(int dpi) => ApplyDpi(dpi, scaleForm: false);
}
