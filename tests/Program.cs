using Microsoft.Data.Sqlite;
using SedentaryReminder;
using SedentaryReminder.Data;
using SedentaryReminder.Reminder;
using SedentaryReminder.Report;
using SedentaryReminder.Log;
using SedentaryReminder.Overlay;
using SedentaryReminder.Todo;
using System.Reflection;

Application.SetHighDpiMode(args.Contains("--dpi-unaware") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();

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
    CheckWorkItems(Path.Combine(testDir, "linked"));
    CheckLegacyWorkItems(Path.Combine(testDir, "legacy"));
    CheckLogUi(Path.Combine(testDir, "ui"));
    CheckTodos(Path.Combine(testDir, "todos"));
    CheckTodoUi(Path.Combine(testDir, "todo-ui"));

    Console.WriteLine("回归检查通过：流水日期、WAL 导出与迁移、周报期次、报告副本、DPI 往返缩放、跨天事项关联与事务回滚、旧库升级、历史筛选与日历、提醒收起和恢复、全部待办查询与搜索、待办增删改与完成恢复、提醒选择待办及来源保存和界面布局。");
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(testDir, recursive: true);
}

static void CheckTodos(string dir)
{
    using var db = new Database(dir);
    var changed = 0;
    db.TodosChanged += () => changed++;
    var first = new TodoEntry { TodoDate = "2026-10-31", Content = "  第一项待办  " };
    var second = new TodoEntry { TodoDate = "2026-10-31", Content = "第二项待办" };
    var other = new TodoEntry { TodoDate = "2026-11-01", Content = "另一天待办" };
    db.InsertTodo(first);
    db.InsertTodo(second);
    db.InsertTodo(other);
    Check(first.Content == "第一项待办" && !first.IsCompleted && first.CompletedAt == null && first.CreatedAt.Length > 0,
        "新建待办未清理内容或初始状态错误");
    Check(db.GetTodos("2026-10-31").Select(todo => todo.Id).SequenceEqual(new[] { first.Id, second.Id }),
        "待办日期隔离或创建顺序错误");
    Check(db.GetAllTodos().Select(todo => todo.Id).SequenceEqual(new[] { first.Id, second.Id, other.Id }),
        "全部待办遗漏了其他日期或未按日期升序");
    var createdAt = first.CreatedAt;
    db.SetTodoCompleted(first.Id, true);
    Check(db.GetTodo(first.Id) is { IsCompleted: true, CompletedAt: not null }, "勾选完成后状态或完成时间未保存");
    Check(db.GetTodo(first.Id)!.CreatedAt == createdAt, "完成待办改变了创建时间");
    Check(db.GetTodos("2026-10-31").Select(todo => todo.Id).SequenceEqual(new[] { second.Id, first.Id }),
        "已完成待办没有移到未完成事项之后");
    Check(db.GetAllTodos().Select(todo => todo.Id).SequenceEqual(new[] { second.Id, other.Id, first.Id }),
        "全部待办没有将已完成事项置后");
    db.SetTodoCompleted(other.Id, true);
    Check(db.GetAllTodos().Select(todo => todo.Id).SequenceEqual(new[] { second.Id, other.Id, first.Id }),
        "全部待办没有按最近完成优先排列");
    db.SetTodoCompleted(first.Id, false);
    Check(db.GetTodo(first.Id) is { IsCompleted: false, CompletedAt: null }, "恢复未完成后状态或完成时间未还原");
    Check(db.GetTodos("2026-10-31").Select(todo => todo.Id).SequenceEqual(new[] { first.Id, second.Id }),
        "恢复未完成后没有回到原创建顺序");
    db.UpdateTodoContent(second.Id, "  修改后的第二项  ");
    Check(db.GetTodo(second.Id) is { Content: "修改后的第二项", IsCompleted: false, TodoDate: "2026-10-31" },
        "编辑待办修改了无关字段或未保存内容");
    var rejected = false;
    try { db.InsertTodo(new TodoEntry { TodoDate = "2026-10-31", Content = "  " }); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "空白待办未被拒绝");
    rejected = false;
    try { db.UpdateTodoContent(second.Id, "  "); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "待办可被编辑为空白");
    db.DeleteTodo(other.Id);
    Check(db.GetTodos("2026-11-01").Count == 0 && db.GetTodo(other.Id) == null, "删除待办失败或影响了日期查询");
    Check(changed == 8, "待办数据变化通知次数错误");
    Check(db.GetLogs("").Count == 0 && db.GetReminderEvents("0001-01-01", "9999-12-31").Count == 0,
        "待办操作写入了工作流水或提醒事件");

    var copyDir = Path.Combine(dir, "migrated");
    Migration.MigrateTo(dir, copyDir);
    using (var migrated = new Database(copyDir))
        Check(migrated.GetTodos("2026-10-31").Count == 2 && migrated.GetTodo(second.Id)?.Content == "修改后的第二项",
            "数据目录迁移后待办丢失");
    var exported = Path.Combine(dir, "todo-export.db");
    BackupService.ExportDatabase(dir, exported);
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = exported, Mode = SqliteOpenMode.ReadOnly }.ToString());
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM todo_item;";
    Check((long)cmd.ExecuteScalar()! == 2, "数据库导出遗漏待办");
}

static void CheckTodoUi(string dir)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { CheckTodoUiOnSta(dir); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw new Exception("待办界面回归失败", failure);
}

static void CheckTodoUiOnSta(string dir)
{
    using var db = new Database(dir);
    var day = new DateTime(2026, 10, 31);
    using var form = new TodoViewForm(db, day) { Opacity = 0, ShowInTaskbar = false };
    form.Show();
    Application.DoEvents();
    var list = Descendants(form).OfType<ListView>().Single();
    var input = Descendants(form).OfType<TextBox>().Single(text => text.AccessibleName == "新待办内容");
    var add = Descendants(form).OfType<Button>().Single(button => button.Text == "添加");
    var showCompleted = Descendants(form).OfType<CheckBox>().Single();
    var picker = Descendants(form).OfType<DateTimePicker>().Single();
    ListViewItem ItemFor(long id) => list.Items.Cast<ListViewItem>().Single(item => ((TodoEntry)item.Tag!).Id == id);

    input.Text = "界面第一项";
    add.PerformClick();
    input.Text = "界面第二项";
    add.PerformClick();
    Application.DoEvents();
    Check(input.TextLength == 0 && list.Items.Count == 2, "待办窗口新增后输入框或列表未刷新");
    var firstId = db.GetTodos("2026-10-31")[0].Id;
    var secondId = db.GetTodos("2026-10-31")[1].Id;
    ItemFor(firstId).Checked = true;
    Application.DoEvents();
    Check(db.GetTodo(firstId)?.IsCompleted == true && ((TodoEntry)list.Items[1].Tag!).Id == firstId,
        "界面勾选未完成入库或已完成置底");
    Check(ItemFor(firstId).Font.Strikeout, "已完成待办没有删除线");
    showCompleted.Checked = false;
    Application.DoEvents();
    Check(list.Items.Count == 1 && db.GetTodo(firstId)?.IsCompleted == true, "隐藏已完成误删了数据或筛选项错误");
    showCompleted.Checked = true;
    Application.DoEvents();
    ItemFor(firstId).Checked = false;
    Application.DoEvents();
    Check(db.GetTodo(firstId) is { IsCompleted: false, CompletedAt: null } && ((TodoEntry)list.Items[0].Tag!).Id == firstId,
        "界面取消勾选没有恢复未完成");

    using (var editor = new TodoEditForm(db, db.GetTodo(firstId)!) { Opacity = 0, ShowInTaskbar = false })
    {
        editor.Show();
        Application.DoEvents();
        var content = Descendants(editor).OfType<TextBox>().Single(text => text.Multiline);
        content.Text = "界面编辑后的第一项";
        Descendants(editor).OfType<Button>().Single(button => button.Text == "保存").PerformClick();
        Check(editor.DialogResult == DialogResult.OK, "有效待办编辑未保存关闭");
    }
    Application.DoEvents();
    Check(db.GetTodo(firstId)?.Content == "界面编辑后的第一项" && ItemFor(firstId).Text == "界面编辑后的第一项",
        "编辑窗口或主列表未同步更新");

    picker.Value = new DateTime(2026, 11, 1);
    Application.DoEvents();
    Check(list.Items.Count == 0 && Descendants(form).OfType<Label>().Any(label => label.Visible && label.Text.StartsWith("这一天还没有待办")),
        "空日期没有显示空状态");
    input.Text = "未来待办";
    add.PerformClick();
    Application.DoEvents();
    Check(db.GetTodos("2026-11-01").Single().Content == "未来待办" && db.GetTodos("2026-10-31").Count == 2,
        "切换日期后待办写入错误日期");
    picker.Value = day;
    Application.DoEvents();
    Check(list.Items.Count == 2 && list.Columns[2].Width == 0, "返回原日期后待办列表或日期列未恢复");

    var all = Descendants(form).OfType<RadioButton>().Single(button => button.Text == "全部待办");
    var byDate = Descendants(form).OfType<RadioButton>().Single(button => button.Text == "按日期查看");
    all.Checked = true;
    Application.DoEvents();
    Check(list.Items.Count == 3 && list.Columns[2].Width > 0, "全部待办没有列出其他日期或显示日期列");
    picker.Value = new DateTime(2026, 11, 2);
    input.Text = "全部视图未来待办";
    add.PerformClick();
    Application.DoEvents();
    Check(db.GetTodos("2026-11-02").Single().Content == "全部视图未来待办" && list.Items.Count == 4,
        "全部视图新增没有归入日期控件选择的日期");
    var search = Descendants(form).OfType<TextBox>().Single(text => text.AccessibleName == "搜索待办内容或日期");
    search.Text = "2026-11-01";
    Check(list.Items.Count == 1 && ((TodoEntry)list.Items[0].Tag!).Content == "未来待办", "全部待办日期搜索错误");
    search.Text = "界面编辑后的第一项";
    Check(list.Items.Count == 1 && ((TodoEntry)list.Items[0].Tag!).Id == firstId, "全部待办内容搜索错误");
    search.Clear();
    picker.Value = day;
    byDate.Checked = true;
    Application.DoEvents();
    Check(list.Items.Count == 2 && list.Columns[2].Width == 0, "切回单日视图后范围或日期列错误");

    list.Items[0].Selected = true;
    Application.DoEvents();
    var edit = Descendants(form).OfType<Button>().Single(button => button.Text == "编辑");
    var delete = Descendants(form).OfType<Button>().Single(button => button.Text == "删除");
    Check(edit.Enabled && delete.Enabled, "选中待办后编辑和删除仍不可用");
    db.DeleteTodo(firstId);
    Application.DoEvents();
    Check(list.Items.Count == 1 && db.GetTodo(secondId) != null, "外部删除后待办列表未即时刷新");

    var completedWidth = list.Columns[1].Width;
    form.Scale(new SizeF(2, 2));
    CheckNear(list.Columns[1].Width, completedWidth * 2, 1, "待办完成时间列未跟随 200% 缩放");
    form.Scale(new SizeF(0.5F, 0.5F));
    CheckNear(list.Columns[1].Width, completedWidth, 1, "待办完成时间列缩放往返后漂移");
    var factor = form.DeviceDpi / 96.0;
    form.ClientSize = new Size((int)(500 * factor), (int)(400 * factor));
    form.PerformLayout();
    Check(list.Columns.Cast<ColumnHeader>().Sum(column => column.Width) <= list.ClientSize.Width,
        "窄窗口待办列表出现横向溢出");

    var completedTodo = new TodoEntry { TodoDate = "2026-10-30", Content = "已完成的选择器事项" };
    db.InsertTodo(completedTodo);
    db.SetTodoCompleted(completedTodo.Id, true);
    using var todoPicker = new TodoPickerForm(db) { Opacity = 0, ShowInTaskbar = false };
    todoPicker.Show();
    Application.DoEvents();
    var pickerList = Descendants(todoPicker).OfType<ListView>().Single();
    Check(pickerList.Items.Count == 3 && pickerList.Items.Cast<ListViewItem>().All(item => !((TodoEntry)item.Tag!).IsCompleted)
        && pickerList.Columns[2].Width > 0, "待办选择器未默认列出全部日期的未完成事项");
    Check(!Descendants(todoPicker).OfType<Button>().Any(button => button.Text is "添加" or "编辑" or "删除"),
        "待办选择器出现了管理操作");
    var pickerSearch = Descendants(todoPicker).OfType<TextBox>().Single(text => text.AccessibleName == "搜索待办内容或日期");
    pickerSearch.Text = "2026-11-01";
    Check(pickerList.Items.Count == 1 && ((TodoEntry)pickerList.Items[0].Tag!).Content == "未来待办", "待办选择器搜索错误");
    pickerSearch.Clear();
    var pickerShowCompleted = Descendants(todoPicker).OfType<CheckBox>().Single();
    pickerShowCompleted.Checked = true;
    Check(pickerList.Items.Count == 4 && pickerList.Items.Cast<ListViewItem>().Any(item => ((TodoEntry)item.Tag!).Id == completedTodo.Id),
        "待办选择器不能显示已完成事项");
    pickerShowCompleted.Checked = false;
    var readOnlyItem = pickerList.Items.Cast<ListViewItem>().Single(item => ((TodoEntry)item.Tag!).Id == secondId);
    readOnlyItem.Checked = true;
    Application.DoEvents();
    Check(!readOnlyItem.Checked && db.GetTodo(secondId)?.IsCompleted == false, "待办选择器可以改变完成状态");
    readOnlyItem.Selected = true;
    Application.DoEvents();
    var choose = Descendants(todoPicker).OfType<Button>().Single(button => button.Text == "选择这件事");
    Check(choose.Enabled, "选中待办后选择按钮仍不可用");
    choose.PerformClick();
    Check(todoPicker.SelectedEntry?.Id == secondId && todoPicker.DialogResult == DialogResult.OK, "待办选择器没有返回所选事项");
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

static void CheckWorkItems(string dir)
{
    using var db = new Database(dir);
    var changed = 0;
    db.WorkLogsChanged += () => changed++;
    var first = new WorkLogEntry { LogDate = "2026-09-14", StartTime = "09:00", EndTime = "10:00", Tag = "开发", Content = "项目A", Source = "manual" };
    db.InsertWorkLog(first);
    var second = new WorkLogEntry { LogDate = "2026-09-15", StartTime = "14:00", EndTime = "15:00", Tag = "开发", Content = "项目A", Source = "continue", ContinueFrom = first.Id };
    db.InsertWorkLog(second);
    var duplicate = new WorkLogEntry { LogDate = "2026-09-17", Content = "项目A" };
    db.InsertWorkLog(duplicate);
    var other = new WorkLogEntry { LogDate = "2026-09-17", Content = "项目A的另一事项" };
    db.InsertWorkLog(other);
    Check(changed == 4, "新增记录通知次数错误");
    Check(first.WorkItemId == second.WorkItemId && first.WorkItemId == duplicate.WorkItemId, "跨天续记及相同内容未关联");
    Check(other.WorkItemId != first.WorkItemId, "相似内容被误关联");
    Check(db.GetLogs("").Count == 4 && db.GetLogs("2026-09-17").Count == 2, "全部或按日期查询错误");
    Check(db.GetLogDates().SequenceEqual(new[] { new DateTime(2026, 9, 17), new DateTime(2026, 9, 15), new DateTime(2026, 9, 14) }), "有记录日期未去重或排序错误");
    var before = db.GetRelatedLogs(second.Id).ToDictionary(log => log.Id);
    second.Content = "项目A已修订";
    second.Tag = "验证";
    second.LogDate = "2026-09-16";
    second.StartTime = "15:30";
    db.UpdateWorkLog(second, syncRelated: true);
    Check(db.GetRelatedLogs(second.Id).Count == 3, "同步修改后关联丢失");
    foreach (var log in db.GetRelatedLogs(second.Id))
    {
        Check(log.Content == second.Content && log.Tag == "验证", "关联内容或标签未同步");
        Check(log.Source == before[log.Id].Source && log.ContinueFrom == before[log.Id].ContinueFrom, "同步篡改了来源或续记出处");
        if (log.Id != second.Id)
            Check(log.LogDate == before[log.Id].LogDate && log.StartTime == before[log.Id].StartTime && log.EndTime == before[log.Id].EndTime, "同步篡改其他记录的日期或时间");
    }
    Check(db.GetLogs("2026-09-15").Count == 0, "同步编辑日期后旧日期未清空");
    Check(db.GetWorkLog(other.Id)?.Content == other.Content, "同步修改影响无关记录");
    db.DeleteWorkLog(first.Id);
    Check(db.GetRelatedLogs(second.Id).Count == 2, "删除关联首条后其余关联丢失");
    var continued = new WorkLogEntry { LogDate = "2026-09-18", Content = second.Content, Source = "continue", ContinueFrom = second.Id };
    db.InsertWorkLog(continued);
    Check(continued.WorkItemId == second.WorkItemId, "修订后续记未继承关联");

    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "data.db") }.ToString()))
    {
        connection.Open();
        using var trigger = connection.CreateCommand();
        trigger.CommandText = $"CREATE TRIGGER reject_sync BEFORE UPDATE ON work_log WHEN OLD.id={duplicate.Id} BEGIN SELECT RAISE(ABORT, 'test rollback'); END;";
        trigger.ExecuteNonQuery();
        var original = db.GetWorkLog(second.Id)!;
        second.Content = "应回滚的修改";
        var notifications = changed;
        var failed = false;
        try { db.UpdateWorkLog(second, syncRelated: true); }
        catch (SqliteException) { failed = true; }
        Check(failed, "回滚验证未触发数据库错误");
        Check(db.GetRelatedLogs(second.Id).All(log => log.Content == original.Content), "同步失败后出现部分写入");
        Check(changed == notifications, "事务回滚后仍发出成功通知");
        trigger.CommandText = "DROP TRIGGER reject_sync;";
        trigger.ExecuteNonQuery();
    }
    second.Content = "仅今天的新事项";
    db.UpdateWorkLog(second);
    Check(db.GetRelatedLogs(second.Id).Count == 1, "仅改当前条未解除原关联");
    Check(db.GetWorkLog(duplicate.Id)?.Content == continued.Content, "仅改当前条影响了其他记录");
    var alteredCopy = new WorkLogEntry { LogDate = "2026-09-19", Content = "另一件事", ContinueFrom = continued.Id, Source = "continue" };
    db.InsertWorkLog(alteredCopy);
    Check(alteredCopy.WorkItemId != continued.WorkItemId, "续记改写内容后仍错误关联原事项");
    var copyDir = Path.Combine(dir, "migrated");
    Migration.MigrateTo(dir, copyDir);
    using var migrated = new Database(copyDir);
    Check(migrated.GetWorkLog(second.Id)?.WorkItemId == second.WorkItemId, "迁移或重启破坏独立修改后的关联");
    Check(migrated.GetRelatedLogs(continued.Id).Count == 2, "迁移后关联数错误");
}

static void CheckLegacyWorkItems(string dir)
{
    Directory.CreateDirectory(dir);
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "data.db") }.ToString()))
    {
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE work_log (id INTEGER PRIMARY KEY AUTOINCREMENT, log_date TEXT NOT NULL,
                start_time TEXT, end_time TEXT, tag TEXT, content TEXT, source TEXT, continue_from INTEGER);
            INSERT INTO work_log (id, log_date, content, continue_from) VALUES
                (1,'2026-09-01','旧项目',2), (2,'2026-09-02','旧项目详情',1),
                (3,'2026-09-03',' 旧项目 ',NULL), (4,'2026-09-04','无关',999),
                (5,'2026-09-05','',NULL), (6,'2026-09-06',NULL,NULL);
            """;
        cmd.ExecuteNonQuery();
    }
    string? itemId;
    using (var db = new Database(dir))
    {
        var logs = db.GetLogs("");
        Check(logs.Count == 6 && logs.All(log => !string.IsNullOrEmpty(log.WorkItemId)), "旧库升级丢数据或未补关联");
        Check(db.GetRelatedLogs(1).Count == 3, "旧库续记链、环及重复内容未正确归组");
        Check(db.GetRelatedLogs(4).Count == 1, "旧库悬空续记错误关联");
        Check(db.GetRelatedLogs(5).Count == 1 && db.GetRelatedLogs(6).Count == 1, "空内容被错误合并");
        Check(db.GetWorkLog(3)?.Content == " 旧项目 ", "升级擅自修改旧内容");
        itemId = db.GetWorkLog(1)!.WorkItemId;
        var editing = db.GetWorkLog(2)!;
        editing.Content = "单独修正旧记录";
        db.UpdateWorkLog(editing);
    }
    using var reopened = new Database(dir);
    Check(reopened.GetWorkLog(1)?.WorkItemId == itemId && reopened.GetRelatedLogs(1).Count == 2, "重启重新归组，破坏已保存关联");
    Check(reopened.GetRelatedLogs(2).Count == 1, "重启重新关联了单独修改的旧续记");
}

static void CheckLogUi(string dir)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { CheckLogUiOnSta(dir); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw new Exception("界面回归失败", failure);
}

static void CheckLogUiOnSta(string dir)
{
    using var db = new Database(dir);
    var first = new WorkLogEntry { LogDate = "2026-09-01", Content = "跨天项目", Tag = "开发" };
    db.InsertWorkLog(first);
    var second = new WorkLogEntry { LogDate = "2026-09-03", Content = "跨天项目", ContinueFrom = first.Id };
    db.InsertWorkLog(second);
    var older = new WorkLogEntry { LogDate = "2026-07-20", Content = "另一个项目" };
    db.InsertWorkLog(older);
    using var calendar = new RecordedDatePickerForm(db.GetLogDates(), new DateTime(2026, 9, 1));
    var days = Descendants(calendar).OfType<Button>().Where(button => button.Tag is DateTime).ToList();
    Check(days.Count == 30 && days.Where(button => button.Enabled).Select(button => (DateTime)button.Tag!).OrderBy(date => date)
        .SequenceEqual(new[] { new DateTime(2026, 9, 1), new DateTime(2026, 9, 3) }), "日历未严格禁用无记录日期");
    Check(days.Single(button => button.Text == "2").BackColor != days.Single(button => button.Text == "3").BackColor, "日历有记录日期未高亮");
    var month = Descendants(calendar).OfType<ComboBox>().Single();
    Check(month.Items.Count == 2, "日历未支持跨月历史定位");
    month.SelectedIndex = 0;
    Check(Descendants(calendar).OfType<Button>().Count(button => button.Tag is DateTime && button.Enabled) == 1, "跨月日历可选日期错误");
    using var empty = new RecordedDatePickerForm(Array.Empty<DateTime>());
    Check(!Descendants(empty).OfType<Button>().Any(button => button.Tag is DateTime && button.Enabled), "空日历有可选日期");

    using var browser = new WorkLogBrowser(db);
    var list = Descendants(browser).OfType<ListView>().Single();
    Check(browser.SelectedDate == null && list.Items.Count == 3 && list.Items[0].SubItems[0].Text == "2026-09-03", "历史界面未默认列出全部最新记录");
    var dateWidth = list.Columns[0].Width;
    browser.Scale(new SizeF(2, 2));
    CheckNear(list.Columns[0].Width, dateWidth * 2, 1, "历史日期列未跟随 200% DPI 缩放");
    browser.Scale(new SizeF(0.5F, 0.5F));
    CheckNear(list.Columns[0].Width, dateWidth, 1, "历史日期列 DPI 往返后漂移");
    var search = Descendants(browser).OfType<TextBox>().Single(text => !text.Multiline);
    search.Text = "跨天";
    Check(list.Items.Count == 2, "历史内容搜索错误");
    search.Text = "开发";
    Check(list.Items.Count == 1, "历史标签搜索错误");
    search.Text = "";
    using var today = new WorkLogBrowser(db, new DateTime(2026, 9, 1));
    Check(Descendants(today).OfType<ListView>().Single().Items.Count == 1, "今日视图日期限制丢失");
    db.DeleteWorkLog(older.Id);
    Check(list.Items.Count == 2 && db.GetLogDates().Count == 2, "删除后列表或可选日期未即时更新");
    using var editor = new ManualEntryForm(db, new AppConfig(), db.GetWorkLog(first.Id));
    var sync = Descendants(editor).OfType<CheckBox>().Single();
    Check(sync.Checked && sync.Text.Contains("2 条"), "关联编辑默认同步或影响数量提示错误");
    using var picker = new WorkLogPickerForm(db);
    Check(Descendants(picker).OfType<ListView>().Single().Items.Count == 2, "续记选择器未列出所有历史记录");
    CheckUiLayout(db);

    // 构造真实多屏窗口后，收起状态再重建；不等计时器，不向用户数据目录写入。
    var manager = new OverlayManager(new AppConfig(), db);
    manager.Show(DateTime.Now);
    try
    {
        Invoke(manager, "OnContinueRequested", db.GetWorkLog(first.Id)!);
        using (var cancelPicker = new System.Windows.Forms.Timer { Interval = 50 })
        {
            cancelPicker.Tick += (_, _) =>
            {
                var opened = Application.OpenForms.OfType<WorkLogPickerForm>().FirstOrDefault();
                if (opened == null) return;
                cancelPicker.Stop();
                opened.DialogResult = DialogResult.Cancel;
            };
            cancelPicker.Start();
            Invoke(manager, "OnHistoryRequested");
        }
        Check(!manager.IsTemporarilyReleased && Field<List<OverlayForm>>(manager, "_forms").All(form => form.Visible), "取消历史选择后未恢复提醒");
        Check(Field<string>(manager, "_inputText") == first.Content, "取消历史选择覆盖了草稿");
        var logsBeforeRelease = db.GetLogs("").Count;
        var eventsBeforeRelease = db.GetReminderEvents("0001-01-01", "9999-12-31").Count;
        Invoke(manager, "OnAccessRequested");
        Check(manager.IsOpen && manager.IsTemporarilyReleased, "临时收起被误算为关闭提醒");
        Check(db.GetLogs("").Count == logsBeforeRelease && db.GetReminderEvents("0001-01-01", "9999-12-31").Count == eventsBeforeRelease, "收起误保存了流水或提醒事件");
        var forms = Field<List<OverlayForm>>(manager, "_forms");
        var dims = Field<System.Collections.IEnumerable>(manager, "_dimForms");
        Check(forms.All(form => !form.Visible) && dims.Cast<Form>().All(form => !form.Visible), "收起时仍有屏幕遮罩拦截");
        var edited = db.GetWorkLog(first.Id)!;
        edited.Content = "修订后的跨天项目";
        db.UpdateWorkLog(edited, syncRelated: true);
        Check(Field<string>(manager, "_inputText") == edited.Content, "同步编辑后未更新仍未改写的续记草稿");
        first.Content = edited.Content;
        typeof(OverlayManager).GetField("_countdownRemainingSec", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, 0);
        Invoke(manager, "OnDisplaySettingsChanged");
        forms = Field<List<OverlayForm>>(manager, "_forms");
        Check(forms.All(form => !form.Visible), "收起期间屏幕重建又弹出遮罩");
        var access = Field<ReminderAccessForm>(manager, "_accessForm");
        Check(Descendants(access).OfType<TextBox>().Single().Text == first.Content, "收起后草稿丢失");
        access.Close();
        Check(!manager.IsTemporarilyReleased && forms.All(form => form.Visible), "返回时未恢复所有屏幕");
        Check(forms.All(form => Descendants(form).OfType<TextBox>().Single().Text == first.Content), "返回时跨屏草稿不同步");
        Check(forms.All(form => Descendants(form).OfType<Button>().Any(button => button.Text == "刚活动完 ✓")), "到时后重建屏幕丢失完成提示");
        Invoke(manager, "OnAction", "skip");
        Check(!manager.IsOpen && db.GetLogs(DateTime.Today.ToString("yyyy-MM-dd")).Single().WorkItemId == first.WorkItemId, "返回后保存丢失跨天关联");
    }
    finally { if (manager.IsOpen) Invoke(manager, "OnAction", "skip"); }
    CheckReminderTodo(db);
}

static void CheckReminderTodo(Database db)
{
    var continued = db.GetLogs("").OrderBy(log => log.Id).First();
    var todo = new TodoEntry { TodoDate = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"), Content = "从提醒选择的历史待办" };
    var future = new TodoEntry { TodoDate = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"), Content = "选择器中的未来待办" };
    var completed = new TodoEntry { TodoDate = DateTime.Today.ToString("yyyy-MM-dd"), Content = "选择器默认隐藏的已完成待办" };
    db.InsertTodo(todo);
    db.InsertTodo(future);
    db.InsertTodo(completed);
    db.SetTodoCompleted(completed.Id, true);

    var manager = new OverlayManager(new AppConfig(), db);
    manager.Show(DateTime.Now);
    try
    {
        Invoke(manager, "OnContinueRequested", continued);
        var logsBeforeCancel = db.GetLogs("").Count;
        var eventsBeforeCancel = db.GetReminderEvents("0001-01-01", "9999-12-31").Count;
        using (var cancelPicker = new System.Windows.Forms.Timer { Interval = 50 })
        {
            cancelPicker.Tick += (_, _) =>
            {
                var opened = Application.OpenForms.OfType<TodoPickerForm>().FirstOrDefault();
                if (opened == null) return;
                cancelPicker.Stop();
                opened.DialogResult = DialogResult.Cancel;
            };
            cancelPicker.Start();
            Invoke(manager, "OnTodoRequested");
        }
        Check(!manager.IsTemporarilyReleased && Field<List<OverlayForm>>(manager, "_forms").All(form => form.Visible),
            "取消待办选择后未恢复提醒");
        Check(Field<string>(manager, "_inputText") == continued.Content && Field<long?>(manager, "_continueFromId") == continued.Id,
            "取消待办选择覆盖了原草稿或续记来源");
        Check(db.GetLogs("").Count == logsBeforeCancel && db.GetReminderEvents("0001-01-01", "9999-12-31").Count == eventsBeforeCancel,
            "取消待办选择误保存了流水或提醒事件");

        var defaultVisible = -1;
        var completedVisible = true;
        var dateShown = false;
        var accepted = false;
        using (var selectTodo = new System.Windows.Forms.Timer { Interval = 50 })
        {
            selectTodo.Tick += (_, _) =>
            {
                var opened = Application.OpenForms.OfType<TodoPickerForm>().FirstOrDefault();
                if (opened == null) return;
                selectTodo.Stop();
                var list = Descendants(opened).OfType<ListView>().Single();
                defaultVisible = list.Items.Count;
                completedVisible = list.Items.Cast<ListViewItem>().Any(item => ((TodoEntry)item.Tag!).IsCompleted);
                dateShown = list.Columns[2].Width > 0;
                list.Items.Cast<ListViewItem>().Single(item => ((TodoEntry)item.Tag!).Id == todo.Id).Selected = true;
                Application.DoEvents();
                Descendants(opened).OfType<Button>().Single(button => button.Text == "选择这件事").PerformClick();
                accepted = true;
            };
            selectTodo.Start();
            Invoke(manager, "OnTodoRequested");
        }
        Check(accepted && defaultVisible == 2 && !completedVisible && dateShown,
            "提醒待办选择器未列出全部日期的未完成事项");
        var forms = Field<List<OverlayForm>>(manager, "_forms");
        Check(!manager.IsTemporarilyReleased && forms.All(form => form.Visible), "确认待办选择后未恢复所有屏幕");
        Check(forms.All(form => Descendants(form).OfType<TextBox>().Single().Text == todo.Content),
            "待办内容没有同步到所有屏幕的草稿");
        Check(forms.All(form => Descendants(form).OfType<Button>().Any(button => button.Text == "从待办选择")),
            "提醒卡片缺少待办选择入口");
        Check(Field<long?>(manager, "_selectedTodoId") == todo.Id && Field<long?>(manager, "_continueFromId") == null
            && Field<string?>(manager, "_selectedTag") == null, "待办来源没有正确替换历史续记");
        Check(db.GetTodo(todo.Id) is { IsCompleted: false, CompletedAt: null } && db.GetTodo(todo.Id)!.TodoDate == todo.TodoDate,
            "选择待办改变了原事项状态或日期");

        var beforeSave = db.GetLogs("").Count;
        var primary = forms.Single(form => form.IsPrimary);
        Descendants(primary).OfType<Button>().Single(button => button.Text == "保存").PerformClick();
        Check(db.GetLogs("").Count == beforeSave + 1, "待办草稿没有通过提醒保存按钮入库");
        var saved = db.GetLogs(DateTime.Today.ToString("yyyy-MM-dd")).OrderByDescending(log => log.Id).First();
        Check(saved.Content == todo.Content && saved.Source == "todo" && saved.ContinueFrom == null
            && saved.LogDate == DateTime.Today.ToString("yyyy-MM-dd"), "待办流水内容、日期、来源或续记关联错误");
        Check(Field<long?>(manager, "_selectedTodoId") == null && forms.All(form => Descendants(form).OfType<TextBox>().Single().TextLength == 0),
            "待办流水保存后来源或草稿未清空");
        Check(db.GetTodo(todo.Id) is { IsCompleted: false, CompletedAt: null } && db.GetTodo(todo.Id)!.TodoDate == todo.TodoDate
            && db.GetTodo(completed.Id)?.IsCompleted == true, "保存流水自动完成了待办或改变了日期");
        using (var history = new WorkLogBrowser(db))
        {
            var savedItem = Descendants(history).OfType<ListView>().Single().Items.Cast<ListViewItem>()
                .Single(item => ((WorkLogEntry)item.Tag!).Id == saved.Id);
            Check(savedItem.SubItems[4].Text == "待办", "历史流水没有显示待办来源");
        }
        Invoke(manager, "OnAction", "skip");
        Check(!manager.IsOpen && db.GetLogs("").Count == beforeSave + 1, "关闭空草稿提醒重复保存了待办流水");
    }
    finally { if (manager.IsOpen) Invoke(manager, "OnAction", "skip"); }
}

static IEnumerable<Control> Descendants(Control root)
{
    foreach (Control child in root.Controls)
    {
        yield return child;
        foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

static void CheckUiLayout(Database db)
{
    using var picker = new WorkLogPickerForm(db) { Opacity = 0, ShowInTaskbar = false };
    picker.Show();
    Application.DoEvents();
    var browser = Descendants(picker).OfType<WorkLogBrowser>().Single();
    var date = Field<Button>(browser, "_dateButton");
    var refresh = Field<Button>(browser, "_refresh");
    var host = Field<Panel>(browser, "_searchHost");
    var filters = Field<TableLayoutPanel>(browser, "_filters");
    CheckNear(host.Top, date.Top, 1, "宽窗口搜索框与日期按钮未对齐");
    CheckNear(host.Height, date.Height, 1, "搜索框与按钮高度不一致");
    CheckNear(refresh.Top, date.Top, 1, "刷新与日期按钮未对齐");
    var factor = picker.DeviceDpi / 96.0;
    picker.ClientSize = new Size((int)(500 * factor), (int)(480 * factor));
    picker.PerformLayout();
    Check(host.Top > date.Bottom, "窄窗口搜索栏未换到下一行");
    CheckNear(host.Top, refresh.Top, 1, "窄窗口搜索栏与刷新按钮未对齐");
    Check(host.Left >= 0 && host.Right <= filters.ClientSize.Width && refresh.Right <= filters.ClientSize.Width, "窄窗口工具栏溢出");
    var narrowList = Descendants(browser).OfType<ListView>().Single();
    Check(narrowList.Columns.Cast<ColumnHeader>().Sum(column => column.Width) <= narrowList.ClientSize.Width, "窄窗口隐藏了来源列，需要额外横向滚动");
    picker.ClientSize = new Size((int)(800 * factor), (int)(530 * factor));
    picker.PerformLayout();
    CheckNear(host.Top, date.Top, 1, "放宽窗口后搜索栏未恢复同一行");
    var search = Field<TextBox>(browser, "_search");
    search.Text = "不存在的内容";
    Field<Button>(browser, "_clearSearch").PerformClick();
    Check(search.TextLength == 0 && Descendants(browser).OfType<ListView>().Single().Items.Count == db.GetLogs("").Count, "清空搜索未恢复列表");
    var footer = Descendants(picker).OfType<Button>().Where(button => button.Text is "取消" or "继续这件事").ToArray();
    CheckNear(footer[0].Height, footer[1].Height, 1, "续记选择器底部按钮高度不一致");
    CheckNear(footer[0].Top, footer[1].Top, 1, "续记选择器底部按钮未对齐");

    using var access = new ReminderAccessForm("草稿", () => { }) { Opacity = 0, ShowInTaskbar = false };
    access.Show();
    var accessButtons = Descendants(access).OfType<Button>().ToArray();
    foreach (var button in accessButtons)
    {
        CheckNear(button.Top, accessButtons[0].Top, 1, "返回入口按钮未处于同一行");
        CheckNear(button.Width, accessButtons[0].Width, 2, "返回入口按钮宽度不一致");
        CheckNear(button.Height, accessButtons[0].Height, 1, "返回入口按钮高度不一致");
    }

    var config = new AppConfig();
    config.Tags.AddRange(new[] { "需要连续多天跟进的长项目标签", "另一个需要换行的项目标签", "较长的验证与排查事项" });
    var logs = Enumerable.Range(0, 12).Select(index => new WorkLogEntry
    {
        EndTime = "09:50", Tag = index % 2 == 0 ? "开发" : "较长标签",
        Content = $"第 {index + 1} 条项目记录，用于验证每行按钮不会随内容长短移动。",
    }).ToList();
    using var overlay = new OverlayForm(new OverlayManager(config, db), config, Screen.PrimaryScreen!, logs) { Opacity = 0 };
    Field<System.Windows.Forms.Timer>(overlay, "_fadeTimer").Stop();
    overlay.UpdateCountdown(300, 300);
    overlay.SetLogsVisible(true);
    overlay.Show();
    Application.DoEvents();
    var eye = Field<Button>(overlay, "_btnEye");
    var history = Descendants(overlay).OfType<Button>().Single(button => button.Text == "全部历史续记");
    CheckNear(eye.Top, history.Top, 1, "显示与历史续记按钮未对齐");
    CheckNear(eye.Height, history.Height, 1, "显示与历史续记按钮高度不一致");
    var tags = Field<FlowLayoutPanel>(overlay, "_tagPanel");
    var save = Field<Button>(overlay, "_btnSave");
    var expectedTagHeight = Math.Max(tags.GetPreferredSize(new Size(tags.Width, 0)).Height, save.Height);
    CheckNear(tags.Parent!.Height, expectedTagHeight, 2, "标签区域被自动布局撑出多余空白");
    var flow = Field<FlowLayoutPanel>(overlay, "_logFlow");
    var rows = flow.Controls.OfType<TableLayoutPanel>().ToArray();
    Check(rows.Length == logs.Count, "流水行数量错误");
    var firstButtons = rows[0].Controls.OfType<Button>().ToArray();
    foreach (var row in rows)
    {
        Check(row.Width <= flow.ClientSize.Width, "流水操作行越过可见区域");
        Check(row.Controls.OfType<Label>().Single().Width > row.Width / 3, "流水内容被按钮过度挤占");
        var buttons = row.Controls.OfType<Button>().ToArray();
        for (var i = 0; i < buttons.Length; i++)
        {
            CheckNear(buttons[i].Left, firstButtons[i].Left, 1, "各条流水操作按钮未按列对齐");
            Check(buttons[i].Right <= row.ClientSize.Width && buttons[i].Bottom <= row.ClientSize.Height, "流水按钮溢出或被裁切");
        }
    }
    var card = Field<TableLayoutPanel>(overlay, "_card");
    var expandedHeight = card.Height;
    overlay.SetLogsVisible(false);
    Check(card.Height < expandedHeight, "隐藏流水后保留了无意义空白");
    overlay.SetLogsVisible(true);
    CheckNear(card.Height, expandedHeight, 2, "反复展开流水造成卡片高度漂移");
    var actions = Field<TableLayoutPanel>(overlay, "_actionRow").Controls.OfType<Button>().ToArray();
    foreach (var action in actions) CheckNear(action.Width, actions[0].Width, 2, "提醒三个操作按钮宽度不一致");
}

static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
static void Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);

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
