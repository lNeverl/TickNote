using Microsoft.Data.Sqlite;

namespace SedentaryReminder.Data;

/// <summary>工作流水条目（原料数据，表 work_log）。</summary>
public sealed class WorkLogEntry
{
    public long Id { get; set; }
    public string LogDate { get; set; } = "";   // 2026-09-14
    public string? StartTime { get; set; }      // 周期开始 10:00
    public string? EndTime { get; set; }        // 记录时间 10:50
    public string? Tag { get; set; }
    public string? Content { get; set; }
    public string Source { get; set; } = "popup"; // popup / manual / continue
    public long? ContinueFrom { get; set; }
}

/// <summary>提醒事件（久坐统计用，表 reminder_event）。</summary>
public sealed class ReminderEventEntry
{
    public long Id { get; set; }
    public string EventTime { get; set; } = "";
    public string Action { get; set; } = "";    // done / skip / postpone
    public int BreakMin { get; set; }
}

/// <summary>AI 生成的正式报告（成品数据，表 report）。</summary>
public sealed class ReportEntry
{
    public long Id { get; set; }
    public string ReportType { get; set; } = ""; // daily / weekly
    public string PeriodKey { get; set; } = "";  // 2026-09-14 / 2026-W38
    public string Content { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// 本地 SQLite 数据访问（M6-1/M6-2）：单连接、即写即存，崩溃不丢当天数据。
/// </summary>
public sealed class Database : IDisposable
{
    private readonly SqliteConnection _conn;

    public Database(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _conn = new SqliteConnection($"Data Source={Path.Combine(dataDir, "data.db")}");
        _conn.Open();
        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS work_log (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                log_date      TEXT NOT NULL,
                start_time    TEXT,
                end_time      TEXT,
                tag           TEXT,
                content       TEXT,
                source        TEXT DEFAULT 'popup',
                continue_from INTEGER
            );
            CREATE TABLE IF NOT EXISTS reminder_event (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                event_time  TEXT NOT NULL,
                action      TEXT NOT NULL,
                break_min   INTEGER
            );
            CREATE TABLE IF NOT EXISTS report (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                report_type TEXT NOT NULL,
                period_key  TEXT NOT NULL,
                content     TEXT NOT NULL,
                created_at  TEXT NOT NULL,
                updated_at  TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_work_log_date ON work_log(log_date);
            CREATE INDEX IF NOT EXISTS idx_reminder_event_time ON reminder_event(event_time);
            CREATE INDEX IF NOT EXISTS idx_report_period ON report(report_type, period_key);
            """;
        cmd.ExecuteNonQuery();
    }

    // ---------------- work_log ----------------

    public long InsertWorkLog(WorkLogEntry e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO work_log (log_date, start_time, end_time, tag, content, source, continue_from)
            VALUES ($d, $st, $et, $tag, $c, $src, $cf); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$d", e.LogDate);
        cmd.Parameters.AddWithValue("$st", (object?)e.StartTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$et", (object?)e.EndTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tag", (object?)e.Tag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", (object?)e.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$src", e.Source);
        cmd.Parameters.AddWithValue("$cf", (object?)e.ContinueFrom ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void UpdateWorkLog(WorkLogEntry e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE work_log SET start_time=$st, end_time=$et, tag=$tag, content=$c WHERE id=$id;
            """;
        cmd.Parameters.AddWithValue("$st", (object?)e.StartTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$et", (object?)e.EndTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tag", (object?)e.Tag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", (object?)e.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteWorkLog(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM work_log WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>查询某一天的流水；date 为空串时查全部。按时间升序。</summary>
    public List<WorkLogEntry> GetLogs(string date)
    {
        using var cmd = _conn.CreateCommand();
        if (date.Length == 0)
        {
            cmd.CommandText = "SELECT id, log_date, start_time, end_time, tag, content, source, continue_from FROM work_log ORDER BY log_date, end_time, id;";
        }
        else
        {
            cmd.CommandText = "SELECT id, log_date, start_time, end_time, tag, content, source, continue_from FROM work_log WHERE log_date=$d ORDER BY end_time, id;";
            cmd.Parameters.AddWithValue("$d", date);
        }
        return ReadLogs(cmd);
    }

    /// <summary>查询日期区间 [from, to]（含两端）的流水。</summary>
    public List<WorkLogEntry> GetLogsRange(string from, string to)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, log_date, start_time, end_time, tag, content, source, continue_from
            FROM work_log WHERE log_date >= $f AND log_date <= $t ORDER BY log_date, end_time, id;
            """;
        cmd.Parameters.AddWithValue("$f", from);
        cmd.Parameters.AddWithValue("$t", to);
        return ReadLogs(cmd);
    }

    private static List<WorkLogEntry> ReadLogs(SqliteCommand cmd)
    {
        var list = new List<WorkLogEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new WorkLogEntry
            {
                Id = r.GetInt64(0),
                LogDate = r.GetString(1),
                StartTime = r.IsDBNull(2) ? null : r.GetString(2),
                EndTime = r.IsDBNull(3) ? null : r.GetString(3),
                Tag = r.IsDBNull(4) ? null : r.GetString(4),
                Content = r.IsDBNull(5) ? null : r.GetString(5),
                Source = r.IsDBNull(6) ? "popup" : r.GetString(6),
                ContinueFrom = r.IsDBNull(7) ? null : r.GetInt64(7),
            });
        }
        return list;
    }

    // ---------------- reminder_event ----------------

    public void InsertReminderEvent(string action, int breakMin)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO reminder_event (event_time, action, break_min) VALUES ($t, $a, $b);";
        cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$a", action);
        cmd.Parameters.AddWithValue("$b", breakMin);
        cmd.ExecuteNonQuery();
    }

    /// <summary>统计区间 [from, to]（含两端，按日期前缀匹配）内的提醒事件。</summary>
    public List<ReminderEventEntry> GetReminderEvents(string from, string to)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, event_time, action, break_min FROM reminder_event
            WHERE substr(event_time,1,10) >= $f AND substr(event_time,1,10) <= $t ORDER BY event_time, id;
            """;
        cmd.Parameters.AddWithValue("$f", from);
        cmd.Parameters.AddWithValue("$t", to);
        var list = new List<ReminderEventEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ReminderEventEntry
            {
                Id = r.GetInt64(0),
                EventTime = r.GetString(1),
                Action = r.GetString(2),
                BreakMin = r.IsDBNull(3) ? 0 : r.GetInt32(3),
            });
        }
        return list;
    }

    // ---------------- report ----------------

    public long InsertReport(ReportEntry e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO report (report_type, period_key, content, created_at, updated_at)
            VALUES ($t, $k, $c, $ca, $ua); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$t", e.ReportType);
        cmd.Parameters.AddWithValue("$k", e.PeriodKey);
        cmd.Parameters.AddWithValue("$c", e.Content);
        cmd.Parameters.AddWithValue("$ca", e.CreatedAt);
        cmd.Parameters.AddWithValue("$ua", (object?)e.UpdatedAt ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void UpdateReport(ReportEntry e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE report SET content=$c, updated_at=$ua WHERE id=$id;";
        cmd.Parameters.AddWithValue("$c", e.Content);
        cmd.Parameters.AddWithValue("$ua", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteReport(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM report WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>按类型+期次精确查报告（同一期次可能有多份，返回列表）。</summary>
    public List<ReportEntry> GetReports(string? type, string? periodKey)
    {
        using var cmd = _conn.CreateCommand();
        var sql = "SELECT id, report_type, period_key, content, created_at, updated_at FROM report";
        var conds = new List<string>();
        if (type != null) { conds.Add("report_type=$t"); cmd.Parameters.AddWithValue("$t", type); }
        if (periodKey != null) { conds.Add("period_key=$k"); cmd.Parameters.AddWithValue("$k", periodKey); }
        if (conds.Count > 0) sql += " WHERE " + string.Join(" AND ", conds);
        sql += " ORDER BY period_key DESC, id DESC;";
        cmd.CommandText = sql;
        var list = new List<ReportEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ReportEntry
            {
                Id = r.GetInt64(0),
                ReportType = r.GetString(1),
                PeriodKey = r.GetString(2),
                Content = r.GetString(3),
                CreatedAt = r.GetString(4),
                UpdatedAt = r.IsDBNull(5) ? null : r.GetString(5),
            });
        }
        return list;
    }

    public void Dispose() => _conn.Dispose();
}
