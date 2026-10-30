using Microsoft.Data.Sqlite;
using SedentaryReminder.Todo;

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
    public string Source { get; set; } = "popup"; // popup / manual / continue / todo
    public long? ContinueFrom { get; set; }
    public string? WorkItemId { get; set; } // 同一事项的持久关联，删除最早记录不影响其余记录
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

    public event Action? WorkLogsChanged;
    public event Action? TodosChanged;

    private const string LogColumns = "id, log_date, start_time, end_time, tag, content, source, continue_from, work_item_id";
    private const string NormalizedContent = "trim(COALESCE(content, ''), char(9) || char(10) || char(13) || ' ')";
    private const string TodoColumns = "id, todo_date, content, is_completed, created_at, completed_at";

    public Database(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDir, "data.db"),
        }.ToString());
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
                continue_from INTEGER,
                work_item_id  TEXT
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
            CREATE TABLE IF NOT EXISTS todo_item (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                todo_date     TEXT NOT NULL,
                content       TEXT NOT NULL,
                is_completed  INTEGER NOT NULL DEFAULT 0,
                created_at    TEXT NOT NULL,
                completed_at  TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_work_log_date ON work_log(log_date);
            CREATE INDEX IF NOT EXISTS idx_reminder_event_time ON reminder_event(event_time);
            CREATE INDEX IF NOT EXISTS idx_report_period ON report(report_type, period_key);
            CREATE INDEX IF NOT EXISTS idx_todo_item_date ON todo_item(todo_date, is_completed, id);
            """;
        cmd.ExecuteNonQuery();
        MigrateWorkItems();
    }

    private void MigrateWorkItems()
    {
        using var transaction = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "PRAGMA table_info(work_log);";
        bool hasColumn;
        using (var reader = cmd.ExecuteReader())
        {
            hasColumn = false;
            while (reader.Read()) hasColumn |= reader.GetString(1) == "work_item_id";
        }
        if (!hasColumn)
        {
            cmd.CommandText = "ALTER TABLE work_log ADD COLUMN work_item_id TEXT;";
            cmd.ExecuteNonQuery();
        }
        cmd.CommandText = "SELECT COUNT(*) FROM work_log WHERE work_item_id IS NULL;";
        if ((long)cmd.ExecuteScalar()! > 0)
        {
            cmd.CommandText = $"SELECT {LogColumns} FROM work_log ORDER BY id;";
            var logs = ReadLogs(cmd);
            var parents = logs.ToDictionary(log => log.Id, log => log.Id);
            long Root(long id)
            {
                while (parents[id] != id)
                {
                    parents[id] = parents[parents[id]];
                    id = parents[id];
                }
                return id;
            }
            void Join(long left, long right) => parents[Root(left)] = Root(right);
            var contents = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var log in logs)
            {
                if (log.ContinueFrom is long previous && parents.ContainsKey(previous)) Join(log.Id, previous);
                var content = log.Content?.Trim() ?? "";
                if (content.Length == 0) continue;
                if (contents.TryGetValue(content, out var same)) Join(log.Id, same);
                else contents[content] = log.Id;
            }
            var groups = new Dictionary<long, string>();
            foreach (var log in logs.Where(log => log.WorkItemId != null))
                groups.TryAdd(Root(log.Id), log.WorkItemId!);
            foreach (var log in logs.Where(log => log.WorkItemId == null))
            {
                var root = Root(log.Id);
                if (!groups.TryGetValue(root, out var group)) groups[root] = group = Guid.NewGuid().ToString("N");
                cmd.CommandText = "UPDATE work_log SET work_item_id=$group WHERE id=$id;";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$group", group);
                cmd.Parameters.AddWithValue("$id", log.Id);
                cmd.ExecuteNonQuery();
            }
        }
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_work_log_item ON work_log(work_item_id);";
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }

    // ---------------- work_log ----------------

    public long InsertWorkLog(WorkLogEntry e)
    {
        using var transaction = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = transaction;
        // 续记时若改成了另一件事，按新内容建立关联；完全相同的内容自动归为同一事项。
        cmd.CommandText = $"SELECT work_item_id FROM work_log WHERE {NormalizedContent}=$c AND length($c)>0 ORDER BY CASE WHEN id=$cf THEN 0 ELSE 1 END, id DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$c", e.Content?.Trim() ?? "");
        cmd.Parameters.AddWithValue("$cf", (object?)e.ContinueFrom ?? DBNull.Value);
        e.WorkItemId = cmd.ExecuteScalar() as string ?? Guid.NewGuid().ToString("N");
        cmd.Parameters.Clear();
        cmd.CommandText = """
            INSERT INTO work_log (log_date, start_time, end_time, tag, content, source, continue_from, work_item_id)
            VALUES ($d, $st, $et, $tag, $c, $src, $cf, $item); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$d", e.LogDate);
        cmd.Parameters.AddWithValue("$st", (object?)e.StartTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$et", (object?)e.EndTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tag", (object?)e.Tag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", (object?)e.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$src", e.Source);
        cmd.Parameters.AddWithValue("$cf", (object?)e.ContinueFrom ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$item", e.WorkItemId);
        e.Id = (long)(cmd.ExecuteScalar() ?? 0L);
        transaction.Commit();
        WorkLogsChanged?.Invoke();
        return e.Id;
    }

    public void UpdateWorkLog(WorkLogEntry e, bool syncRelated = false)
    {
        using var transaction = _conn.BeginTransaction();
        var original = GetWorkLog(e.Id, transaction) ?? throw new InvalidOperationException("这条记录已被删除，请刷新后重试。");
        var itemId = original.WorkItemId!;
        if (syncRelated && (original.Content != e.Content || original.Tag != e.Tag))
        {
            using var related = RelatedLogsCommand(original, transaction);
            var ids = ReadLogs(related).Select(log => log.WorkItemId!).Distinct().ToList();
            using var sync = _conn.CreateCommand();
            sync.Transaction = transaction;
            for (var i = 0; i < ids.Count; i++) sync.Parameters.AddWithValue($"$g{i}", ids[i]);
            sync.CommandText = $"UPDATE work_log SET content=$c, tag=$tag, work_item_id=$item WHERE work_item_id IN ({string.Join(",", ids.Select((_, i) => $"$g{i}"))});";
            sync.Parameters.AddWithValue("$c", (object?)e.Content ?? DBNull.Value);
            sync.Parameters.AddWithValue("$tag", (object?)e.Tag ?? DBNull.Value);
            sync.Parameters.AddWithValue("$item", itemId);
            sync.ExecuteNonQuery();
        }
        else if (!syncRelated && (original.Content != e.Content || original.Tag != e.Tag))
        {
            itemId = Guid.NewGuid().ToString("N");
        }
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE work_log SET log_date=$d, start_time=$st, end_time=$et, tag=$tag, content=$c, work_item_id=$item WHERE id=$id;
            """;
        cmd.Parameters.AddWithValue("$d", e.LogDate);
        cmd.Parameters.AddWithValue("$st", (object?)e.StartTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$et", (object?)e.EndTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tag", (object?)e.Tag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", (object?)e.Content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.Parameters.AddWithValue("$item", itemId);
        cmd.ExecuteNonQuery();
        e.WorkItemId = itemId;
        transaction.Commit();
        WorkLogsChanged?.Invoke();
    }

    public void DeleteWorkLog(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM work_log WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        WorkLogsChanged?.Invoke();
    }

    public WorkLogEntry? GetWorkLog(long id) => GetWorkLog(id, null);

    private WorkLogEntry? GetWorkLog(long id, SqliteTransaction? transaction)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"SELECT {LogColumns} FROM work_log WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadLogs(cmd).FirstOrDefault();
    }

    public List<WorkLogEntry> GetRelatedLogs(long id)
    {
        var entry = GetWorkLog(id);
        if (entry == null) return new();
        using var cmd = RelatedLogsCommand(entry, null);
        return ReadLogs(cmd);
    }

    private SqliteCommand RelatedLogsCommand(WorkLogEntry entry, SqliteTransaction? transaction)
    {
        var cmd = _conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""
            SELECT {LogColumns} FROM work_log WHERE work_item_id IN (
                SELECT work_item_id FROM work_log WHERE work_item_id=$item
                OR (length($c)>0 AND {NormalizedContent}=$c)
            ) ORDER BY log_date, end_time, id;
            """;
        cmd.Parameters.AddWithValue("$item", (object?)entry.WorkItemId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", entry.Content?.Trim() ?? "");
        return cmd;
    }

    public List<DateTime> GetLogDates()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT log_date FROM work_log ORDER BY log_date DESC;";
        using var reader = cmd.ExecuteReader();
        var dates = new List<DateTime>();
        while (reader.Read())
            if (DateTime.TryParseExact(reader.GetString(0), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date)) dates.Add(date);
        return dates;
    }

    /// <summary>查询某一天的流水；date 为空串时查全部。按时间升序。</summary>
    public List<WorkLogEntry> GetLogs(string date)
    {
        using var cmd = _conn.CreateCommand();
        if (date.Length == 0)
        {
            cmd.CommandText = $"SELECT {LogColumns} FROM work_log ORDER BY log_date, end_time, id;";
        }
        else
        {
            cmd.CommandText = $"SELECT {LogColumns} FROM work_log WHERE log_date=$d ORDER BY end_time, id;";
            cmd.Parameters.AddWithValue("$d", date);
        }
        return ReadLogs(cmd);
    }

    /// <summary>查询日期区间 [from, to]（含两端）的流水。</summary>
    public List<WorkLogEntry> GetLogsRange(string from, string to)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {LogColumns}
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
                WorkItemId = r.IsDBNull(8) ? null : r.GetString(8),
            });
        }
        return list;
    }

    // ---------------- todo_item ----------------

    public long InsertTodo(TodoEntry e)
    {
        var content = NormalizeTodoContent(e.Content);
        if (!DateTime.TryParseExact(e.TodoDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("待办日期无效。", nameof(e));
        e.Content = content;
        e.IsCompleted = false;
        e.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        e.CompletedAt = null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO todo_item (todo_date, content, is_completed, created_at, completed_at)
            VALUES ($d, $c, 0, $ca, NULL); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$d", e.TodoDate);
        cmd.Parameters.AddWithValue("$c", e.Content);
        cmd.Parameters.AddWithValue("$ca", e.CreatedAt);
        e.Id = (long)(cmd.ExecuteScalar() ?? 0L);
        TodosChanged?.Invoke();
        return e.Id;
    }

    public void UpdateTodoContent(long id, string content)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE todo_item SET content=$c WHERE id=$id;";
        cmd.Parameters.AddWithValue("$c", NormalizeTodoContent(content));
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException("这条待办已被删除，请刷新后重试。");
        TodosChanged?.Invoke();
    }

    public void SetTodoCompleted(long id, bool completed)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE todo_item SET is_completed=$done, completed_at=$at WHERE id=$id;";
        cmd.Parameters.AddWithValue("$done", completed ? 1 : 0);
        cmd.Parameters.AddWithValue("$at", completed ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException("这条待办已被删除，请刷新后重试。");
        TodosChanged?.Invoke();
    }

    public void DeleteTodo(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM todo_item WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException("这条待办已被删除，请刷新后重试。");
        TodosChanged?.Invoke();
    }

    public TodoEntry? GetTodo(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {TodoColumns} FROM todo_item WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadTodos(cmd).FirstOrDefault();
    }

    /// <summary>查询某一天的待办，未完成在前，同组内按创建顺序排列。</summary>
    public List<TodoEntry> GetTodos(string date)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {TodoColumns} FROM todo_item WHERE todo_date=$d ORDER BY is_completed, id;";
        cmd.Parameters.AddWithValue("$d", date);
        return ReadTodos(cmd);
    }

    /// <summary>查询全部待办：未完成按所属日期升序，已完成置后并按最近完成优先。</summary>
    public List<TodoEntry> GetAllTodos()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {TodoColumns} FROM todo_item
            ORDER BY is_completed,
                     CASE WHEN is_completed=0 THEN todo_date END,
                     CASE WHEN is_completed=0 THEN id END,
                     completed_at DESC, id DESC;
            """;
        return ReadTodos(cmd);
    }

    private static List<TodoEntry> ReadTodos(SqliteCommand cmd)
    {
        var list = new List<TodoEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TodoEntry
            {
                Id = r.GetInt64(0),
                TodoDate = r.GetString(1),
                Content = r.GetString(2),
                IsCompleted = r.GetInt64(3) != 0,
                CreatedAt = r.GetString(4),
                CompletedAt = r.IsDBNull(5) ? null : r.GetString(5),
            });
        }
        return list;
    }

    private static string NormalizeTodoContent(string? content)
    {
        var normalized = content?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException("待办内容不能为空。", nameof(content));
        return normalized;
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
