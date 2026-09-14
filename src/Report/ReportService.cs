using System.Globalization;
using System.Text;
using SedentaryReminder.Data;

namespace SedentaryReminder.Report;

/// <summary>
/// 报告服务（M4）：组装"提示词+数据"复制给 AI；粘贴回来的报告保存入库并写 exports\ 副本；
/// 久坐统计；原始流水导出 Markdown/CSV。
/// </summary>
public sealed class ReportService
{
    private readonly AppConfig _config;
    private readonly Database _db;

    public ReportService(AppConfig config, Database db)
    {
        _config = config;
        _db = db;
    }

    // ---------------- 期次计算 ----------------

    /// <summary>按"周起始日"配置计算 date 所在周的范围与期次键（如 2026-W38）。</summary>
    public (DateTime From, DateTime To, string Key) GetWeekRange(DateTime date)
    {
        bool monday = _config.WeekStartsMonday;
        int dow = (int)date.DayOfWeek; // Sunday=0
        int diff = monday ? (dow == 0 ? 6 : dow - 1) : dow;
        var from = date.Date.AddDays(-diff);
        var to = from.AddDays(6);
        int week, year;
        if (monday)
        {
            week = ISOWeek.GetWeekOfYear(from);
            year = ISOWeek.GetYear(from);
        }
        else
        {
            week = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                from, CalendarWeekRule.FirstFullWeek, DayOfWeek.Sunday);
            year = from.AddDays(6).Year;
        }
        return (from, to, $"{year}-W{week:D2}");
    }

    // ---------------- 提示词组装 ----------------

    /// <summary>M4-3：日报提示词 + 当天全部流水 + 当天久坐统计。</summary>
    public string BuildDailyPrompt(DateTime date)
    {
        var key = date.ToString("yyyy-MM-dd");
        var logs = _db.GetLogs(key);
        return _config.DailyPromptTemplate
            .Replace("{date}", key)
            .Replace("{logs}", FormatLogs(logs, includeDate: false))
            .Replace("{stats}", GetStatsText(key, key));
    }

    /// <summary>M4-4：周报提示词 + 本周流水 + 本周各天日报 + 本周久坐统计。</summary>
    public string BuildWeeklyPrompt(DateTime anyDateInWeek)
    {
        var (from, to, key) = GetWeekRange(anyDateInWeek);
        var logs = _db.GetLogsRange(Fmt(from), Fmt(to));
        var dailies = _db.GetReports("daily", null)
            .Where(r => string.CompareOrdinal(r.PeriodKey, Fmt(from)) >= 0
                     && string.CompareOrdinal(r.PeriodKey, Fmt(to)) <= 0)
            .OrderBy(r => r.PeriodKey, StringComparer.Ordinal)
            .ToList();

        var dailyBlock = dailies.Count == 0
            ? "（本周暂无已生成日报）"
            : string.Join("\n\n", dailies.Select(r => $"--- 日报 {r.PeriodKey} ---\n{r.Content}"));

        return _config.WeeklyPromptTemplate
            .Replace("{week}", $"{key}（{Fmt(from)} ~ {Fmt(to)}）")
            .Replace("{logs}", FormatLogs(logs, includeDate: true))
            .Replace("{daily_reports}", dailyBlock)
            .Replace("{stats}", GetStatsText(Fmt(from), Fmt(to)));
    }

    private static string Fmt(DateTime d) => d.ToString("yyyy-MM-dd");

    private static string FormatLogs(List<WorkLogEntry> logs, bool includeDate)
    {
        if (logs.Count == 0) return "（该时段没有流水记录）";
        var sb = new StringBuilder();
        foreach (var l in logs)
        {
            if (includeDate) sb.Append(l.LogDate).Append(' ');
            sb.Append(l.EndTime ?? l.StartTime ?? "--:--");
            if (l.Tag is { Length: > 0 }) sb.Append(" [").Append(l.Tag).Append(']');
            sb.Append(' ').Append((l.Content ?? "").Replace("\r", " ").Replace("\n", " "));
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    // ---------------- 久坐统计（M4-8） ----------------

    public string GetStatsText(string fromDate, string toDate)
    {
        var events = _db.GetReminderEvents(fromDate, toDate);
        int total = events.Count;
        int done = events.Count(e => e.Action == "done");
        int skip = events.Count(e => e.Action == "skip");
        int postpone = events.Count(e => e.Action == "postpone");
        int skipRate = total > 0 ? (int)Math.Round(skip * 100.0 / total) : 0;
        return $"提醒 {total} 次，活动 {done} 次，跳过 {skip} 次（跳过率 {skipRate}%），推迟 {postpone} 次";
    }

    // ---------------- 报告保存（M4-5） ----------------

    /// <summary>保存粘贴回来的报告；overwriteId 非空时覆盖该条，否则新增（保留两份）。同时写 exports\ 副本。</summary>
    public ReportEntry SaveImportedReport(string type, string periodKey, string content, long? overwriteId)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ReportEntry entry;
        if (overwriteId is long id)
        {
            entry = new ReportEntry { Id = id, ReportType = type, PeriodKey = periodKey, Content = content, CreatedAt = now };
            _db.UpdateReport(entry);
        }
        else
        {
            entry = new ReportEntry { ReportType = type, PeriodKey = periodKey, Content = content, CreatedAt = now };
            entry.Id = _db.InsertReport(entry);
        }
        WriteExportCopy(entry);
        return entry;
    }

    /// <summary>报告视图里手动编辑后的保存（同步更新 exports\ 副本）。</summary>
    public void UpdateReport(ReportEntry entry)
    {
        _db.UpdateReport(entry);
        WriteExportCopy(entry);
    }

    private void WriteExportCopy(ReportEntry entry)
    {
        try
        {
            var dir = Path.Combine(_config.DataDir, "exports");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{entry.ReportType}_{entry.PeriodKey}.md");
            File.WriteAllText(path, entry.Content);
        }
        catch
        {
            // 副本写失败不影响库内数据
        }
    }

    // ---------------- 原始导出（M4-7） ----------------

    public void ExportLogs(string fromDate, string toDate, string format, string filePath)
    {
        var logs = _db.GetLogsRange(fromDate, toDate);
        if (format == "csv")
        {
            var sb = new StringBuilder("日期,开始时间,记录时间,标签,内容,来源\n");
            foreach (var l in logs)
            {
                sb.Append(l.LogDate).Append(',')
                  .Append(l.StartTime).Append(',')
                  .Append(l.EndTime).Append(',')
                  .Append(Csv(l.Tag)).Append(',')
                  .Append(Csv(l.Content)).Append(',')
                  .Append(l.Source).Append('\n');
            }
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        else
        {
            var sb = new StringBuilder($"# 工作流水导出（{fromDate} ~ {toDate}）\n\n");
            string? currentDate = null;
            foreach (var l in logs)
            {
                if (l.LogDate != currentDate)
                {
                    currentDate = l.LogDate;
                    sb.Append("\n## ").Append(currentDate).Append("\n\n");
                }
                sb.Append("- ").Append(l.EndTime ?? l.StartTime ?? "--:--");
                if (l.Tag is { Length: > 0 }) sb.Append(" [").Append(l.Tag).Append(']');
                sb.Append(' ').Append((l.Content ?? "").Replace("\r", " ").Replace("\n", " ")).Append('\n');
            }
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var need = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        return need ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
