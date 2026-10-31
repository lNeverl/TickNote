using Microsoft.Data.Sqlite;
using SedentaryReminder;
using SedentaryReminder.Data;
using SedentaryReminder.Reminder;
using SedentaryReminder.Report;
using SedentaryReminder.Log;
using SedentaryReminder.Overlay;
using SedentaryReminder.Todo;
using SedentaryReminder.Settings;
using System.Reflection;

Application.SetHighDpiMode(args.Contains("--dpi-unaware") ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
Application.ThreadException += (_, e) => { Console.Error.WriteLine(e.Exception); Environment.Exit(1); };
Application.EnableVisualStyles();

var testDir = Path.Combine(Path.GetTempPath(), "SedentaryReminderRegression_" + Guid.NewGuid().ToString("N") + ";data");
Directory.CreateDirectory(testDir);
try
{
    if (args.Contains("--todo-only"))
    {
        CheckTodos(Path.Combine(testDir, "todos"));
        CheckLegacyTodos(Path.Combine(testDir, "legacy-todos"));
        CheckTodoUi(Path.Combine(testDir, "todo-ui"));
        Console.WriteLine("待办功能回归通过。");
        return;
    }
    if (args.Contains("--startup-only"))
    {
        Exception? startupFailure = null;
        var startupThread = new Thread(() =>
        {
            ConfigureUiTestThread();
            try { CheckTodoStartup(Path.Combine(testDir, "startup"), args.FirstOrDefault(arg => arg.StartsWith("--source-data="))?["--source-data=".Length..]); }
            catch (Exception ex) { startupFailure = ex; }
        });
        startupThread.SetApartmentState(ApartmentState.STA);
        startupThread.Start();
        startupThread.Join();
        if (startupFailure != null) throw new Exception("待办启动回归失败", startupFailure);
        Console.WriteLine("待办启动回归通过。");
        return;
    }
    if (args.Contains("--layout-only"))
    {
        Exception? layoutFailure = null;
        var layoutThread = new Thread(() =>
        {
            ConfigureUiTestThread();
            try
            {
                using var layoutDb = new Database(testDir);
                CheckListImageLifetime();
                CheckUiLayout(layoutDb);
                CheckHiddenWorkspaceLayout(Path.Combine(testDir, "workspace"));
            }
            catch (Exception ex) { layoutFailure = ex; }
        });
        layoutThread.SetApartmentState(ApartmentState.STA);
        layoutThread.Start();
        layoutThread.Join();
        if (layoutFailure != null) throw new Exception("隐藏窗口布局检查失败", layoutFailure);
        Console.WriteLine("隐藏窗口布局检查通过。");
        return;
    }
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

    Console.WriteLine("检查 DPI、数据与关联事项…");
    CheckDpiScaling();
    CheckWorkItems(Path.Combine(testDir, "linked"));
    CheckLegacyWorkItems(Path.Combine(testDir, "legacy"));
    Console.WriteLine("检查历史、提醒与待办界面…");
    CheckLogUi(Path.Combine(testDir, "ui"));
    CheckTodos(Path.Combine(testDir, "todos"));
    CheckLegacyTodos(Path.Combine(testDir, "legacy-todos"));
    CheckTodoUi(Path.Combine(testDir, "todo-ui"));
    Console.WriteLine("检查主窗口与页面路由…");
    CheckMainWindow(Path.Combine(testDir, "main-ui"), args.FirstOrDefault(arg => arg.StartsWith("--screenshots="))?["--screenshots=".Length..]);

    Console.WriteLine("回归检查通过：流水日期、WAL 导出与迁移、报告、DPI、事项关联与事务回滚、旧库升级、历史筛选与日历、提醒收起恢复、待办及标签联动、主窗口全部页面与单例路由、草稿和筛选保留、退出保存、收起恢复及窗口状态持久化。");
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(testDir, recursive: true);
}

static void ConfigureUiTestThread()
{
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
    Application.ThreadException += (_, e) => { Console.Error.WriteLine(e.Exception); Environment.Exit(1); };
}

static void CheckMainWindow(string dir, string? screenshots)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        ConfigureUiTestThread();
        try { CheckMainWindowOnSta(dir, screenshots); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) throw new Exception("主窗口回归失败", failure);
}

static void CheckMainWindowOnSta(string dir, string? screenshots)
{
    CheckListImageLifetime();
    CheckTodoStartup(Path.Combine(dir, "startup"));
    using (var app = new SedentaryReminder.AppContext(dir, startServices: false))
    {
        app.Db.InsertTodo(new TodoEntry { TodoDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), Content = "跟进未完成的问题调查", Tag = "故障调查" });
        app.Db.InsertTodo(new TodoEntry { TodoDate = DateTime.Today.ToString("yyyy-MM-dd"), Content = "开发主界面的页面导航", Tag = "功能开发" });
        app.Db.InsertWorkLog(new WorkLogEntry { LogDate = "2026-10-30", Content = "历史记录", Tag = "功能开发", EndTime = "16:00" });
        app.Db.InsertWorkLog(new WorkLogEntry { LogDate = "2026-10-29", Content = "另一条记录", Tag = "故障调查", EndTime = "10:00" });
        app.Reports.SaveImportedReport("daily", "2026-10-30", "已保存的日报", null);
        app.Reports.SaveImportedReport("weekly", "2026-W44", "已保存的周报", null);
        app.ShowMainWindow();
        var main = app.MainWindow!;
        main.Opacity = 0;
        main.ShowInTaskbar = false;
        Application.DoEvents();
        Check(main.SelectedPage == MainPage.Todos && Descendants(main.CurrentPage).OfType<ListView>().Single().Items.Count == 2,
            "主窗口首次打开未默认列出全部日期的未完成待办");

        foreach (var page in Enum.GetValues<MainPage>())
        {
            main.Navigate(page);
            Application.DoEvents();
            Check(!Field<FlowLayoutPanel>(main, "_navigation").HorizontalScroll.Visible, "导航栏出现横向滚动条：" + page);
            Check(main.CurrentPage is { Visible: true } && main.CurrentPage.FindForm() == main, "页面没有直接承载在主窗口：" + page);
            Check(main.CurrentPage.Bounds.Width > 0 && main.CurrentPage.Bounds.Height > 0, "主窗口页面没有可用空间：" + page);
            if (screenshots != null)
            {
                Directory.CreateDirectory(screenshots);
                using var bitmap = new Bitmap(main.Width, main.Height);
                main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(screenshots, page + ".png"));
            }
        }
        Check(main.ConfirmExit(decide: _ => throw new Exception("未编辑的页面被错误标为有草稿")),
            "未编辑的主窗口不能正常退出");
        Check(Application.OpenForms.OfType<MainForm>().Count() == 1, "切页时创建了重复主窗口");
        Check(!Application.OpenForms.Cast<Form>().Any(form => form is LogViewForm or TodoViewForm or SettingsForm or ReportViewForm or ImportReportForm or ExportForm),
            "主窗口路由仍在弹出独立业务窗口");

        var fullSize = main.ClientSize;
        var factor = main.DeviceDpi / 96.0;
        main.ClientSize = new Size((int)(760 * factor), (int)(480 * factor));
        foreach (var page in Enum.GetValues<MainPage>())
        {
            main.Navigate(page);
            Application.DoEvents();
            Check(!Field<FlowLayoutPanel>(main, "_navigation").HorizontalScroll.Visible, "窄窗口导航栏出现横向滚动条：" + page);
            foreach (var button in Descendants(main.CurrentPage).OfType<Button>().Where(button => button.Visible))
                Check(button.Right <= button.Parent!.ClientSize.Width + 2, $"窄窗口中按钮超出页面宽度：{page}/{button.Text}");
            if (page == MainPage.Todos)
            {
                Check(Descendants(main.CurrentPage).OfType<ListView>().Single().ClientSize.Height >= 60 * factor,
                    "缩小主窗口后待办列表被操作栏挤没");
            }
            if (screenshots != null)
            {
                using var bitmap = new Bitmap(main.Width, main.Height);
                main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(screenshots, page + "-narrow.png"));
            }
        }
        main.ClientSize = fullSize;

        main.Navigate(MainPage.Reports);
        var reportPage = (ReportPage)main.CurrentPage;
        var reportTypes = Descendants(reportPage).OfType<TabControl>().Single();
        reportTypes.SelectedIndex = 1;
        Check(Descendants(reportPage).OfType<TextBox>().Single().Text == "已保存的周报", "切换到周报分类没有切换正文");
        reportTypes.SelectedIndex = 0;
        Check(Descendants(reportPage).OfType<TextBox>().Single().Text == "已保存的日报", "切回日报分类丢失选择或正文");

        app.ShowLogView(DateTime.Today);
        var history = (LogPage)main.CurrentPage;
        var browser = Descendants(history).OfType<WorkLogBrowser>().Single();
        Check(browser.SelectedDate == DateTime.Today && Descendants(history).OfType<ListView>().Single().Items.Count == 0,
            "查看今日在没有记录时退回了全部日期");
        app.ShowLogView(null);
        var search = Descendants(history).OfType<TextBox>().Single(text => text.AccessibleName == "搜索工作流水内容或标签");
        search.Text = "历史记录";
        app.ShowTodoView();
        main.Navigate(MainPage.History);
        Check(ReferenceEquals(main.CurrentPage, history) && search.Text == "历史记录", "切页重建了流水或丢失搜索条件");

        app.ShowManualEntry();
        var manual = (ManualEntryPage)main.CurrentPage;
        var manualText = Descendants(manual).OfType<TextBox>().Single(text => text.Multiline);
        manualText.Text = "主窗口补记草稿";
        Descendants(manual).OfType<ComboBox>().Single().Text = "功能开发";
        app.ShowAbout();
        app.ShowManualEntry();
        Check(manualText.Text == "主窗口补记草稿" && manual.HasUnsavedChanges, "补记草稿在切页后丢失");
        Check(!main.ConfirmExit(decide: _ => DialogResult.Cancel) && main.SelectedPage == MainPage.ManualEntry,
            "取消退出后没有返回尚未保存的页面");
        Check(main.ConfirmExit(decide: _ => DialogResult.Yes) && !manual.HasUnsavedChanges
            && app.Db.GetLogs("").Any(log => log.Content == "主窗口补记草稿" && log.Tag == "功能开发"), "退出保存没有提交补记草稿");

        app.ShowReportView();
        var reports = (ReportPage)main.CurrentPage;
        var reportText = Descendants(reports).OfType<TextBox>().Single(text => text.Multiline);
        reportText.Text = "保留报告的未保存修改";
        app.ShowTodoView();
        app.ShowReportView();
        Check(reports.HasUnsavedChanges && reportText.Text == "保留报告的未保存修改", "返回报告页时刷新覆盖了草稿");
        Check(reports.SaveChanges() && app.Db.GetReports("daily", "2026-10-30").Single().Content == reportText.Text,
            "主窗口报告编辑未保存");

        app.ShowImportReport();
        var import = (ImportReportPage)main.CurrentPage;
        var importText = Descendants(import).OfType<TextBox>().Single(text => text.Multiline);
        Descendants(import).OfType<DateTimePicker>().Single().Value = new DateTime(2026, 10, 28);
        importText.Text = "从主窗口导入的新日报";
        app.ShowExport();
        app.ShowImportReport();
        Check(import.HasUnsavedChanges && importText.Text == "从主窗口导入的新日报", "导入报告切页后丢失粘贴内容");
        Check(import.SaveChanges() && !import.HasUnsavedChanges, "导入报告保存后没有清空已保存草稿");
        app.ShowReportView();
        Check(Descendants(reports).OfType<ListBox>().Sum(list => list.Items.Count) == app.Db.GetReports(null, null).Count, "导入后报告页没有刷新新报告");

        app.ShowSettings();
        var settings = (SettingsPage)main.CurrentPage;
        var interval = Field<NumericUpDown>(settings, "_numInterval");
        interval.Value = 65;
        app.ShowAbout();
        app.ShowSettings();
        Check(settings.HasUnsavedChanges && interval.Value == 65, "设置切页后丢失未保存修改");
        Check(settings.SaveChanges() && !settings.HasUnsavedChanges && AppConfig.Load(dir).IntervalMinutes == 65,
            "设置没有在主窗口保存并持久化");

        main.Navigate(MainPage.Reminder);
        Descendants(main.CurrentPage).OfType<Button>().Single(button => button.Text == "暂停 30 分钟").PerformClick();
        Check(app.Engine.IsPaused, "主窗口不能暂停提醒");
        Descendants(main.CurrentPage).OfType<Button>().Single(button => button.Text == "恢复提醒").PerformClick();
        Check(!app.Engine.IsPaused, "主窗口不能恢复提醒");
        app.ShowTodoView();
        var todos = (TodoPage)main.CurrentPage;
        var todoInput = Descendants(todos).OfType<TextBox>().Single(text => text.AccessibleName == "新待办内容");
        todoInput.Text = "收起后继续填写的待办";
        main.Close();
        Check(!main.Visible && !main.IsDisposed, "关闭主窗口没有收起到托盘");
        app.ShowMainWindow();
        Check(ReferenceEquals(main, app.MainWindow) && main.Visible && todoInput.Text == "收起后继续填写的待办",
            "重新唤起没有复用主窗口或丢失待办草稿");
        todoInput.Clear();
        main.WindowState = FormWindowState.Minimized;
        app.ShowMainWindow();
        Check(main.WindowState == FormWindowState.Normal, "再次打开主窗口没有还原最小化状态");
        app.ShowLogView(null);
        app.Engine.NotifyOverlayOpened();
        app.Overlay.Show(DateTime.Now);
        var primary = Field<List<OverlayForm>>(app.Overlay, "_forms").Single(form => form.IsPrimary);
        Invoke(app.Overlay, "OnInputChanged", primary, "数据迁移前提醒中尚未保存的草稿");
        var eventCount = app.Db.GetReminderEvents("0001-01-01", "9999-12-31").Count;
        Check(app.PrepareDataMigration() && app.Overlay.IsOpen && Field<string>(app.Overlay, "_inputText") == "",
            "迁移前没有先保存提醒草稿或意外关闭了提醒");
        var copyDir = Path.Combine(dir, "copied-data");
        Migration.MigrateTo(dir, copyDir);
        using (var migrated = new Database(copyDir))
            Check(migrated.GetLogs("").Any(log => log.Content == "数据迁移前提醒中尚未保存的草稿"), "数据迁移遗漏了提醒中未提交的草稿");
        Check(MainWindowState.Load(copyDir).LastPage == nameof(MainPage.History), "数据迁移遗漏主窗口状态");
        Check(app.Overlay.TryCloseForShutdown() && app.Db.GetReminderEvents("0001-01-01", "9999-12-31").Count == eventCount,
            "退出时保存提醒草稿错误地增加了活动统计");
        main.Close();
    }

    using (var reopened = new SedentaryReminder.AppContext(dir, startServices: false))
    {
        reopened.ShowMainWindow();
        reopened.MainWindow!.Opacity = 0;
        Check(reopened.MainWindow.SelectedPage == MainPage.History, "重启后没有恢复上次主页面");
    }
    using (var dpiApp = new SedentaryReminder.AppContext(Path.Combine(dir, "dpi"), startServices: false))
    {
        dpiApp.ShowMainWindow();
        var main = dpiApp.MainWindow!;
        main.Opacity = 0;
        var initialDpi = main.DeviceDpi;
        var targetDpi = initialDpi * 2;
        Invoke(main, "ApplyDpi", targetDpi, true);
        main.Navigate(MainPage.Export);
        var date = Descendants(main.CurrentPage).OfType<DateTimePicker>().First();
        // WinForms 对原生日期控件的边框另作调整，允许几个逻辑像素的差异。
        CheckNear(date.Width, (int)Math.Round(136 * targetDpi / 96.0), (int)Math.Ceiling(4 * targetDpi / 96.0), "高 DPI 下后来创建的页面没有按当前比例缩放");
        Invoke(main, "ApplyDpi", initialDpi, true);
        CheckNear(date.Width, (int)Math.Round(136 * initialDpi / 96.0), (int)Math.Ceiling(4 * initialDpi / 96.0), "主窗口跨 DPI 往返后页面尺寸漂移");
    }
}

static void CheckTodos(string dir)
{
    using var db = new Database(dir);
    var changed = 0;
    db.TodosChanged += () => changed++;
    var first = new TodoEntry { TodoDate = "2026-10-31", Content = "  第一项待办  ", Tag = "  功能开发  " };
    var second = new TodoEntry { TodoDate = "2026-10-31", Content = "第二项待办" };
    var other = new TodoEntry { TodoDate = "2026-11-01", Content = "另一天待办" };
    db.InsertTodo(first);
    db.InsertTodo(second);
    db.InsertTodo(other);
    Check(first.Content == "第一项待办" && !first.IsCompleted && first.CompletedAt == null && first.CreatedAt.Length > 0,
        "新建待办未清理内容或初始状态错误");
    Check(first.Tag == "功能开发" && db.GetTodo(first.Id)?.Tag == first.Tag && db.GetTodo(second.Id)?.Tag == null,
        "待办标签没有清理、保存或正确处理空标签");
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
    db.UpdateTodo(second.Id, "  修改后的第二项  ", " 故障调查 ");
    Check(db.GetTodo(second.Id) is { Content: "修改后的第二项", Tag: "故障调查", IsCompleted: false, TodoDate: "2026-10-31" },
        "编辑待办修改了无关字段或未保存内容");
    var rejected = false;
    try { db.InsertTodo(new TodoEntry { TodoDate = "2026-10-31", Content = "  " }); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "空白待办未被拒绝");
    rejected = false;
    try { db.UpdateTodo(second.Id, "  ", "不能保存的标签"); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "待办可被编辑为空白");
    Check(db.GetTodo(second.Id)?.Tag == "故障调查", "编辑失败却保存了标签");
    db.DeleteTodo(other.Id);
    Check(db.GetTodos("2026-11-01").Count == 0 && db.GetTodo(other.Id) == null, "删除待办失败或影响了日期查询");
    Check(changed == 8, "待办数据变化通知次数错误");
    db.SetTodoCompleted(second.Id, true);
    db.SetTodoCompleted(first.Id, true);
    Check(db.GetAllTodos().Select(todo => todo.Id).SequenceEqual(new[] { first.Id, second.Id }),
        "连续完成待办后，最近完成的较早创建事项没有排在最前");
    Check(db.GetLogs("").Count == 0 && db.GetReminderEvents("0001-01-01", "9999-12-31").Count == 0,
        "待办操作写入了工作流水或提醒事件");

    var copyDir = Path.Combine(dir, "migrated");
    Migration.MigrateTo(dir, copyDir);
    using (var migrated = new Database(copyDir))
        Check(migrated.GetTodos("2026-10-31").Count == 2 && migrated.GetTodo(second.Id) is { Content: "修改后的第二项", Tag: "故障调查" }
            && migrated.GetTodo(first.Id)?.Tag == "功能开发",
            "数据目录迁移后待办丢失");
    var exported = Path.Combine(dir, "todo-export.db");
    BackupService.ExportDatabase(dir, exported);
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = exported, Mode = SqliteOpenMode.ReadOnly }.ToString());
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM todo_item;";
    Check((long)cmd.ExecuteScalar()! == 2, "数据库导出遗漏待办");
    cmd.CommandText = "SELECT tag FROM todo_item WHERE id=$id;";
    cmd.Parameters.AddWithValue("$id", first.Id);
    Check((string?)cmd.ExecuteScalar() == "功能开发", "数据库导出遗漏待办标签");
}

static void CheckLegacyTodos(string dir)
{
    Directory.CreateDirectory(dir);
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "data.db") }.ToString()))
    {
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE todo_item (id INTEGER PRIMARY KEY AUTOINCREMENT, todo_date TEXT NOT NULL,
                content TEXT NOT NULL, is_completed INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, completed_at TEXT);
            INSERT INTO todo_item VALUES (1, '2026-10-31', '旧待办', 1, '2026-10-30 09:00:00', '2026-10-31 10:00:00');
            """;
        cmd.ExecuteNonQuery();
    }
    using (var db = new Database(dir))
    {
        Check(db.GetTodo(1) is { Tag: null, Content: "旧待办", IsCompleted: true, CompletedAt: "2026-10-31 10:00:00" },
            "旧版待办升级后内容或状态丢失");
        db.UpdateTodo(1, "旧待办增加标签", "功能开发");
    }
    using (var db = new Database(dir))
    {
        Check(db.GetTodo(1) is { Tag: "功能开发", Content: "旧待办增加标签", IsCompleted: true,
            TodoDate: "2026-10-31", CreatedAt: "2026-10-30 09:00:00", CompletedAt: "2026-10-31 10:00:00" },
            "升级后标签未持久化或编辑标签修改了其他字段");
        db.UpdateTodo(1, "清空标签", "  ");
        Check(db.GetTodo(1)?.Tag == null, "清空待办标签后未恢复无标签状态");
    }
}

static void CheckTodoUi(string dir)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        ConfigureUiTestThread();
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
    var config = new AppConfig();
    using var form = new TodoViewForm(db, config, day) { Opacity = 0, ShowInTaskbar = false };
    form.Show();
    Application.DoEvents();
    var list = Descendants(form).OfType<ListView>().Single();
    var input = Descendants(form).OfType<TextBox>().Single(text => text.AccessibleName == "新待办内容");
    var add = Descendants(form).OfType<Button>().Single(button => button.Text == "添加");
    var showCompleted = Descendants(form).OfType<CheckBox>().Single();
    var picker = Descendants(form).OfType<DateTimePicker>().Single();
    var tag = Descendants(form).OfType<ComboBox>().Single(combo => combo.AccessibleName == "待办标签");
    ListViewItem ItemFor(long id) => list.Items.Cast<ListViewItem>().Single(item => ((TodoEntry)item.Tag!).Id == id);

    input.Text = "界面第一项";
    tag.Text = "功能开发";
    Check(tag.Items.Cast<string>().Contains("功能开发"), "新建待办没有复用设置中的快捷标签");
    add.PerformClick();
    Check(tag.Text.Length == 0, "添加待办后没有清空标签");
    input.Text = "界面第二项";
    add.PerformClick();
    Application.DoEvents();
    Check(input.TextLength == 0 && list.Items.Count == 2, "待办窗口新增后输入框或列表未刷新");
    var firstId = db.GetTodos("2026-10-31")[0].Id;
    var secondId = db.GetTodos("2026-10-31")[1].Id;
    Check(db.GetTodo(firstId)?.Tag == "功能开发" && ItemFor(firstId).SubItems[1].Text == "功能开发"
        && db.GetTodo(secondId)?.Tag == null, "新建待办的标签未保存显示或污染了下一项");
    ItemFor(firstId).Checked = true;
    ItemFor(firstId).Checked = false;
    Application.DoEvents();
    Check(db.GetTodo(firstId) is { IsCompleted: false, CompletedAt: null } && !ItemFor(firstId).Checked,
        "快速勾选再取消后，异步保存覆盖了最后的未完成状态");
    ItemFor(firstId).Checked = true;
    Application.DoEvents();
    Check(db.GetTodo(firstId)?.IsCompleted == true && ((TodoEntry)list.Items[1].Tag!).Id == firstId,
        "界面勾选未完成入库或已完成置底");
    Check(ItemFor(firstId).Font.Strikeout, "已完成待办没有删除线");
    ItemFor(firstId).Checked = false;
    ItemFor(firstId).Checked = true;
    Application.DoEvents();
    Check(db.GetTodo(firstId)?.IsCompleted == true && ItemFor(firstId).Checked,
        "快速取消再勾选后，异步保存覆盖了最后的完成状态");
    showCompleted.Checked = false;
    Application.DoEvents();
    Check(list.Items.Count == 1 && db.GetTodo(firstId)?.IsCompleted == true, "隐藏已完成误删了数据或筛选项错误");
    showCompleted.Checked = true;
    Application.DoEvents();
    ItemFor(firstId).Checked = false;
    Application.DoEvents();
    Check(db.GetTodo(firstId) is { IsCompleted: false, CompletedAt: null } && ((TodoEntry)list.Items[0].Tag!).Id == firstId,
        "界面取消勾选没有恢复未完成");

    ItemFor(firstId).Selected = true;
    var editorOpened = false;
    var editorTagLoaded = false;
    using (var editOnDoubleClick = new System.Windows.Forms.Timer { Interval = 50 })
    {
        editOnDoubleClick.Tick += (_, _) =>
        {
            var editor = Application.OpenForms.OfType<TodoEditForm>().FirstOrDefault();
            if (editor == null) return;
            editOnDoubleClick.Stop();
            editorOpened = true;
            var editorTag = Descendants(editor).OfType<ComboBox>().Single();
            editorTagLoaded = editorTag.Text == "功能开发";
            editorTag.Text = "故障调查";
            var content = Descendants(editor).OfType<TextBox>().Single(text => text.Multiline);
            content.Text = "界面编辑后的第一项";
            Descendants(editor).OfType<Button>().Single(button => button.Text == "保存").PerformClick();
        };
        editOnDoubleClick.Start();
        // 走原生双击消息路径，覆盖 ListView 双击正文时默认切换勾选状态的行为。
        var bounds = ItemFor(firstId).GetBounds(ItemBoundsPortion.Label);
        var point = new Point(bounds.Left + Math.Min(30, bounds.Width / 2), bounds.Top + bounds.Height / 2);
        var previousCursor = Cursor.Position;
        try
        {
            Cursor.Position = list.PointToScreen(point);
            var position = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
            Invoke(list, "WndProc", Message.Create(list.Handle, 0x0203, (IntPtr)1, position)); // WM_LBUTTONDBLCLK
            Invoke(list, "WndProc", Message.Create(list.Handle, 0x0202, IntPtr.Zero, position)); // WM_LBUTTONUP
        }
        finally { Cursor.Position = previousCursor; }
    }
    Check(editorOpened, "双击待办没有打开编辑窗口");
    Check(editorTagLoaded, "编辑窗口没有载入待办原标签");
    Application.DoEvents();
    Check(db.GetTodo(firstId)?.Content == "界面编辑后的第一项" && ItemFor(firstId).Text == "界面编辑后的第一项",
        "编辑窗口或主列表未同步更新");
    Check(db.GetTodo(firstId) is { IsCompleted: false, CompletedAt: null, TodoDate: "2026-10-31" },
        "双击正文编辑待办意外改变了完成状态或所属日期");
    Check(db.GetTodo(firstId)?.Tag == "故障调查" && ItemFor(firstId).SubItems[1].Text == "故障调查",
        "编辑后的待办标签未保存或同步到列表");

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
    Check(list.Items.Count == 2 && list.Columns[3].Width == 0, "返回原日期后待办列表或日期列未恢复");

    var all = Descendants(form).OfType<RadioButton>().Single(button => button.Text == "全部待办");
    var byDate = Descendants(form).OfType<RadioButton>().Single(button => button.Text == "按日期查看");
    all.Checked = true;
    Application.DoEvents();
    Check(list.Items.Count == 3 && list.Columns[3].Width > 0, "全部待办没有列出其他日期或显示日期列");
    picker.Value = new DateTime(2026, 11, 2);
    input.Text = "全部视图未来待办";
    add.PerformClick();
    Application.DoEvents();
    Check(db.GetTodos("2026-11-02").Single().Content == "全部视图未来待办" && list.Items.Count == 4,
        "全部视图新增没有归入日期控件选择的日期");
    var search = Descendants(form).OfType<TextBox>().Single(text => text.AccessibleName == "搜索待办内容、标签或日期");
    search.Text = "2026-11-01";
    Check(list.Items.Count == 1 && ((TodoEntry)list.Items[0].Tag!).Content == "未来待办", "全部待办日期搜索错误");
    search.Text = "界面编辑后的第一项";
    Check(list.Items.Count == 1 && ((TodoEntry)list.Items[0].Tag!).Id == firstId, "全部待办内容搜索错误");
    search.Text = "故障调查";
    Check(list.Items.Count == 1 && ((TodoEntry)list.Items[0].Tag!).Id == firstId, "待办标签搜索错误");
    search.Clear();
    picker.Value = day;
    byDate.Checked = true;
    Application.DoEvents();
    Check(list.Items.Count == 2 && list.Columns[3].Width == 0, "切回单日视图后范围或日期列错误");

    list.Items[0].Selected = true;
    Application.DoEvents();
    var edit = Descendants(form).OfType<Button>().Single(button => button.Text == "编辑");
    var delete = Descendants(form).OfType<Button>().Single(button => button.Text == "删除");
    Check(edit.Enabled && delete.Enabled, "选中待办后编辑和删除仍不可用");
    db.DeleteTodo(firstId);
    Application.DoEvents();
    Check(list.Items.Count == 1 && db.GetTodo(secondId) != null, "外部删除后待办列表未即时刷新");

    var completedWidth = list.Columns[2].Width;
    form.Scale(new SizeF(2, 2));
    CheckNear(list.Columns[2].Width, completedWidth * 2, 1, "待办完成时间列未跟随 200% 缩放");
    form.Scale(new SizeF(0.5F, 0.5F));
    CheckNear(list.Columns[2].Width, completedWidth, 1, "待办完成时间列缩放往返后漂移");
    var factor = form.DeviceDpi / 96.0;
    form.ClientSize = new Size((int)(500 * factor), (int)(400 * factor));
    form.PerformLayout();
    Check(list.Columns.Cast<ColumnHeader>().Sum(column => column.Width) <= list.ClientSize.Width,
        "窄窗口待办列表出现横向溢出");
    all.Checked = true;
    Application.DoEvents();
    Check(list.Columns.Cast<ColumnHeader>().Sum(column => column.Width) <= list.ClientSize.Width && tag.Right <= tag.Parent!.ClientSize.Width,
        "窄窗口全部待办的标签列或标签输入溢出");
    Check(list.Height - list.ClientSize.Height < SystemInformation.HorizontalScrollBarHeight,
        "切换范围或缩窄窗口后原生列表残留横向滚动条");

    var completedTodo = new TodoEntry { TodoDate = "2026-10-30", Content = "已完成的选择器事项" };
    db.InsertTodo(completedTodo);
    db.SetTodoCompleted(completedTodo.Id, true);
    using var todoPicker = new TodoPickerForm(db) { Opacity = 0, ShowInTaskbar = false };
    todoPicker.Show();
    Application.DoEvents();
    var pickerList = Descendants(todoPicker).OfType<ListView>().Single();
    Check(pickerList.Items.Count == 3 && pickerList.Items.Cast<ListViewItem>().All(item => !((TodoEntry)item.Tag!).IsCompleted)
        && pickerList.Columns[3].Width > 0, "待办选择器未默认列出全部日期的未完成事项");
    Check(!Descendants(todoPicker).OfType<Button>().Any(button => button.Text is "添加" or "编辑" or "删除"),
        "待办选择器出现了管理操作");
    var pickerSearch = Descendants(todoPicker).OfType<TextBox>().Single(text => text.AccessibleName == "搜索待办内容、标签或日期");
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

    foreach (var dpi in new[] { 120, 144, 168, 192, 240, 96, 192, 144, 96 })
    {
        form.ApplyInitialDpi(dpi);
        CheckNear(form.ProbeButton.Width, (int)Math.Round(originalButtonBounds.Width * dpi / 96.0), 2, "多档 DPI 往返后控件宽度漂移");
    }
}

static void CheckListImageLifetime()
{
    using var host = new Form { Opacity = 0, ShowInTaskbar = false, ClientSize = new Size(400, 240) };
    using var list = new ThemedListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true };
    list.Columns.Add("内容", 280);
    list.Items.Add("验证延迟创建和重建句柄时，行高位图仍然有效");
    host.Controls.Add(list);
    host.Show();
    // ImageList 延迟创建句柄，直接访问原图不能覆盖用户遇到的错误路径。
    Check(list.SmallImageList!.Handle != IntPtr.Zero, "列表行高图像句柄未创建");
    foreach (var points in new[] { 12F, 18F, 9F })
    {
        using var font = new Font("Microsoft YaHei UI", points);
        list.Font = font;
        typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, null);
        Check(list.SmallImageList.Handle != IntPtr.Zero && list.Items.Count == 1, "字体变化或句柄重建后列表失效");
        list.Font = host.Font;
    }
    host.Close();
}

static void CheckTodoStartup(string dir, string? sourceDir = null)
{
    foreach (var count in sourceDir == null ? new[] { 0, 1, 2, 5, 60 } : new[] { -1 })
    {
        Console.WriteLine(sourceDir == null ? $"启动已有 {count} 条历史待办、今天为空的窗口…" : "验证现有数据的只读副本启动…");
        var dataDir = Path.Combine(dir, count.ToString());
        if (sourceDir != null)
        {
            Directory.CreateDirectory(dataDir);
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(sourceDir, "data.db"), Mode = SqliteOpenMode.ReadOnly }.ToString());
            using var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDir, "data.db") }.ToString());
            source.Open();
            copy.Open();
            source.BackupDatabase(copy);
            foreach (var file in new[] { "config.json", "ui-state.json" })
                if (File.Exists(Path.Combine(sourceDir, file))) File.Copy(Path.Combine(sourceDir, file), Path.Combine(dataDir, file));
        }
        using var app = new SedentaryReminder.AppContext(dataDir, startServices: false);
        for (var index = 0; index < count; index++)
        {
            var todo = new TodoEntry { TodoDate = DateTime.Today.AddDays(-1 - index % 3).ToString("yyyy-MM-dd"), Content = "启动时已保存的待办 " + index, Tag = "功能开发" };
            app.Db.InsertTodo(todo);
            if (index % 2 == 0) app.Db.SetTodoCompleted(todo.Id, true);
        }
        app.Overlay.Prewarm();
        var storedTodos = System.Text.Json.JsonSerializer.Serialize(app.Db.GetAllTodos());
        var writes = 0;
        app.Db.TodosChanged += () => writes++;
        using var bootstrap = new Form { Opacity = 0, ShowInTaskbar = false };
        Exception? failure = null;
        bootstrap.Shown += (_, _) => bootstrap.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
        {
            try
            {
                using var main = new MainForm(app) { Opacity = 0, ShowInTaskbar = false };
                main.Show();
                Application.DoEvents();
                var browser = Descendants(main.CurrentPage).OfType<TodoBrowser>().Single();
                var list = Descendants(browser).OfType<ListView>().Single();
                var search = Descendants(browser).OfType<TextBox>().Single();
                var expectedCount = app.Db.GetAllTodos().Count;
                for (var pass = 0; pass < 4; pass++)
                {
                    browser.ShowCompleted = true;
                    Check(list.Items.Count == expectedCount, "启动时已有待办未能全部加载");
                    if (expectedCount > 0) list.Items[expectedCount - 1].Selected = true;
                    browser.Reload();
                    if (list.IsHandleCreated)
                        typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, null);
                    browser.ShowCompleted = false;
                    browser.ShowCompleted = true;
                    search.Text = "不存在的搜索内容";
                    search.Clear();
                    main.Navigate(MainPage.Settings);
                    main.Navigate(MainPage.Todos);
                    main.Hide();
                    main.Show();
                    Application.DoEvents();
                    using var bitmap = new Bitmap(main.Width, main.Height);
                    main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    Check(list.Items.Count == expectedCount, "重建句柄或重开窗口后待办丢失");
                    Check(list.Items.Cast<ListViewItem>().All(item => item != null && item.Checked == ((TodoEntry)item.Tag!).IsCompleted),
                        "启动或重建后原生勾选状态与数据库不一致");
                    Check(writes == 0 && System.Text.Json.JsonSerializer.Serialize(app.Db.GetAllTodos()) == storedTodos,
                        "打开窗口、切换筛选或恢复句柄错误地写入了待办数据");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { bootstrap.Close(); }
        }));
        Application.Run(bootstrap);
        if (failure != null) throw new Exception($"{count} 条待办启动失败", failure);
    }
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
        ConfigureUiTestThread();
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
        var initialForms = Field<List<OverlayForm>>(manager, "_forms");
        var initialDims = Field<System.Collections.IEnumerable>(manager, "_dimForms").Cast<Form>().ToList();
        Check(initialForms.Count == Screen.AllScreens.Length && initialDims.Count == initialForms.Count, "提醒未覆盖当前所有显示器");
        Check(initialForms.All(form => form.IsReadyToPresent && form.Opacity == 1 && form.IsHandleCreated), "遮罩呈现时卡片还没有完成准备");
        Check(initialForms.All(form => Field<Label>(form, "_lblCountdown").Text.Length > 0), "首帧缺少倒计时文字");
        Check(initialDims.All(form => form.Opacity > 0), "提醒背景层没有呈现");
        Console.WriteLine($"提醒首帧准备：{manager.LastPreparationTime.TotalMilliseconds:F0} ms，{initialForms.Count} 个屏幕");
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
    var todo = new TodoEntry { TodoDate = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"), Content = "从提醒选择的历史待办", Tag = "功能开发" };
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
                dateShown = list.Columns[3].Width > 0;
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
            && Field<string?>(manager, "_selectedTag") == todo.Tag, "待办来源或标签没有正确替换历史续记");
        Check(forms.All(form => Field<Dictionary<string, Button>>(form, "_tagButtons")["功能开发"].BackColor
            != Field<Dictionary<string, Button>>(form, "_tagButtons")["会议"].BackColor), "待办标签没有在各屏提醒中高亮");
        Check(db.GetTodo(todo.Id) is { IsCompleted: false, CompletedAt: null } && db.GetTodo(todo.Id)!.TodoDate == todo.TodoDate,
            "选择待办改变了原事项状态或日期");

        var beforeSave = db.GetLogs("").Count;
        var primary = forms.Single(form => form.IsPrimary);
        Descendants(primary).OfType<Button>().Single(button => button.Text == "保存").PerformClick();
        Check(db.GetLogs("").Count == beforeSave + 1, "待办草稿没有通过提醒保存按钮入库");
        var saved = db.GetLogs(DateTime.Today.ToString("yyyy-MM-dd")).OrderByDescending(log => log.Id).First();
        Check(saved.Content == todo.Content && saved.Tag == "功能开发" && saved.Source == "todo" && saved.ContinueFrom == null
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
        db.UpdateTodo(todo.Id, todo.Content, "故障调查");
        Check(db.GetWorkLog(saved.Id)?.Tag == "功能开发", "后续修改待办标签改变了已保存的流水");

        var custom = new TodoEntry { TodoDate = todo.TodoDate, Content = "带自定义标签的待办", Tag = "项目专项验证" };
        db.InsertTodo(custom);
        Invoke(manager, "OnTodoSelected", custom);
        Check(forms.All(form => Field<Dictionary<string, Button>>(form, "_tagButtons").ContainsKey(custom.Tag)),
            "自定义或已移除的待办标签在提醒中不可见");
        Invoke(manager, "OnDisplaySettingsChanged");
        forms = Field<List<OverlayForm>>(manager, "_forms");
        Check(forms.All(form => Field<Dictionary<string, Button>>(form, "_tagButtons").ContainsKey(custom.Tag))
            && Field<string?>(manager, "_selectedTag") == custom.Tag, "屏幕重建后丢失待办标签");
        Invoke(manager, "OnTagToggled", "会议");
        Check(Field<string?>(manager, "_selectedTag") == "会议" && db.GetTodo(custom.Id)?.Tag == custom.Tag
            && forms.All(form => !Field<Dictionary<string, Button>>(form, "_tagButtons").ContainsKey(custom.Tag)),
            "提醒草稿不能独立更换待办标签或遗留了临时标签按钮");
        Invoke(manager, "OnTodoSelected", future);
        Check(Field<string?>(manager, "_selectedTag") == null, "选择无标签待办残留了上一个标签");
        Invoke(manager, "OnContinueRequested", continued);
        Check(Field<long?>(manager, "_selectedTodoId") == null && Field<string?>(manager, "_selectedTag") == continued.Tag,
            "转用历史续记后没有正确替换待办来源和标签");
        Invoke(manager, "OnInputChanged", forms.Single(form => form.IsPrimary), "");
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
    if (db.GetLogs("").Count == 0)
    {
        var emptyState = Field<Label>(browser, "_empty");
        Check(emptyState.Visible && emptyState.Parent!.Controls.GetChildIndex(emptyState) == 0, "空记录说明被列表遮住");
    }
    SaveUiCapture(picker, "HistoryPicker");

    using (var wizard = new WizardForm { Opacity = 0, ShowInTaskbar = false })
    {
        wizard.Show();
        Application.DoEvents();
        SaveUiCapture(wizard, "FirstRun");
    }

    using var access = new ReminderAccessForm("草稿", () => { }) { Opacity = 0, ShowInTaskbar = false };
    access.Show();
    var accessButtons = Descendants(access).OfType<Button>().ToArray();
    foreach (var button in accessButtons)
    {
        CheckNear(button.Top, accessButtons[0].Top, 1, "返回入口按钮未处于同一行");
        CheckNear(button.Width, accessButtons[0].Width, 2, "返回入口按钮宽度不一致");
        CheckNear(button.Height, accessButtons[0].Height, 1, "返回入口按钮高度不一致");
    }
    SaveUiCapture(access, "ReminderAccess");

    var config = new AppConfig();
    config.Tags.AddRange(new[] { "需要连续多天跟进的长项目标签", "另一个需要换行的项目标签", "较长的验证与排查事项" });
    var logs = Enumerable.Range(0, 12).Select(index => new WorkLogEntry
    {
        EndTime = "09:50", Tag = index % 2 == 0 ? "开发" : "较长标签",
        Content = $"第 {index + 1} 条项目记录，用于验证每行按钮不会随内容长短移动。",
    }).ToList();
    using var overlay = new OverlayForm(new OverlayManager(config, db), config, Screen.PrimaryScreen!, logs) { Opacity = 0 };
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
    var bodyViewport = Field<Panel>(overlay, "_viewport");
    Check(card.Left >= 0 && card.Right <= bodyViewport.ClientSize.Width + 1, "提醒正文横向偏移或被裁切");
    var expandedHeight = card.Height;
    overlay.SetLogsVisible(false);
    Check(card.Height < expandedHeight, "隐藏流水后保留了无意义空白");
    overlay.SetLogsVisible(true);
    CheckNear(card.Height, expandedHeight, 2, "反复展开流水造成卡片高度漂移");
    var actions = Field<TableLayoutPanel>(overlay, "_actionRow").Controls.OfType<Button>().ToArray();
    foreach (var action in actions) CheckNear(action.Width, actions[0].Width, 2, "提醒三个操作按钮宽度不一致");
    var overlayFooter = Field<TableLayoutPanel>(overlay, "_footer");
    var viewport = Field<Panel>(overlay, "_viewport");
    SaveUiCapture(Field<TableLayoutPanel>(overlay, "_frame"), "ReminderExpanded");
    Check(!viewport.Contains(overlayFooter), "提醒主要按钮被放进正文滚动区");
    overlay.ClientSize = new Size(overlay.Width, Math.Max(300, overlay.Height / 2));
    Invoke(overlay, "RelayoutCard");
    Application.DoEvents();
    var footerBounds = overlay.RectangleToClient(overlayFooter.RectangleToScreen(overlayFooter.ClientRectangle));
    Check(footerBounds.Bottom <= overlay.ClientSize.Height && footerBounds.Top >= 0, "小高度屏幕中主要活动按钮超出可视区域");
    Check(viewport.Height < card.Height && viewport.VerticalScroll.Visible, $"小高度屏幕中提醒正文没有滚动兜底：窗口 {overlay.ClientSize}，正文 {card.Size}，视口 {viewport.Size}，滚动 {viewport.VerticalScroll.Visible}");
    Check(tags.Height > 0 && tags.Width > 0, "小高度卡片中标签区被压为零尺寸");
    foreach (var button in tags.Controls.OfType<Button>())
        Check(button.Visible && button.Left >= 0 && button.Right <= tags.ClientSize.Width + 1, $"小高度卡片中标签布局失效：{button.Text} / {button.Bounds} / 标签区 {tags.Bounds}");
    SaveUiCapture(tags, "ReminderTagsSmallHeight");
    SaveUiCapture(Field<TableLayoutPanel>(overlay, "_frame"), "ReminderSmallHeight");
}

static void SaveUiCapture(Control control, string name)
{
    var output = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--screenshots="))?["--screenshots=".Length..];
    if (output == null) return;
    var dir = Path.Combine(output, "dialogs");
    Directory.CreateDirectory(dir);
    using var bitmap = new Bitmap(control.Width, control.Height);
    control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    bitmap.Save(Path.Combine(dir, name + ".png"));
}

static void CheckHiddenWorkspaceLayout(string dir)
{
    using var app = new SedentaryReminder.AppContext(dir, startServices: false);
    app.Reports.SaveImportedReport("daily", "2026-10-31", "第一段\n第二段\n\n第三段", null);
    using var main = new MainForm(app) { Opacity = 0, ShowInTaskbar = false };
    main.Show();
    Console.WriteLine($"隐藏窗口检查：实际 DPI {main.DeviceDpi}，屏幕 {Screen.FromControl(main).Bounds.Size}");
    foreach (var page in Enum.GetValues<MainPage>())
    {
        main.Navigate(page);
        Application.DoEvents();
        SaveUiCapture(main, "Workspace-" + page);
        if (page == MainPage.GenerateReport)
            Check(Descendants(main.CurrentPage).OfType<TextBox>().Single().Lines.Length > 5, "提示词预览丢失了段落换行");
        if (page == MainPage.Reports)
            Check(Descendants(main.CurrentPage).OfType<TextBox>().Single().Lines.Length == 4 && !main.CurrentPage.HasUnsavedChanges,
                "报告加载换行失败或仅查看就被标记为修改");
        if (page == MainPage.Settings)
        {
            var tabs = Descendants(main.CurrentPage).OfType<TabControl>().Single();
            for (var index = 0; index < tabs.TabCount; index++)
            {
                tabs.SelectedIndex = index;
                Application.DoEvents();
                foreach (var button in Descendants(tabs.SelectedTab!).OfType<Button>())
                    Check(button.Height <= UiLayout.Scale(main, 100), "设置按钮被布局拉伸成异常高度：" + button.Text);
                SaveUiCapture(main, "Settings-" + index);
            }
            Check(!main.CurrentPage.HasUnsavedChanges, "切换设置分类错误地改变了设置");
        }
    }
    var factor = main.DeviceDpi / 96.0;
    main.ClientSize = new Size((int)(760 * factor), (int)(480 * factor));
    foreach (var page in Enum.GetValues<MainPage>())
    {
        main.Navigate(page);
        Application.DoEvents();
        Check(!Field<FlowLayoutPanel>(main, "_navigation").HorizontalScroll.Visible, "隐藏窗口的窄导航出现横向溢出");
        foreach (var button in Descendants(main.CurrentPage).OfType<Button>().Where(button => button.Visible))
            Check(button.Right <= button.Parent!.ClientSize.Width + 2, "窄页面按钮越界：" + page + "/" + button.Text);
        SaveUiCapture(main, "Workspace-" + page + "-narrow");
    }
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
