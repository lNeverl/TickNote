using SedentaryReminder.Data;

namespace SedentaryReminder.Settings;

/// <summary>
/// 设置窗口（M5-5）：分五个页签——提醒 / 时间与时段 / 快捷标签 / 提示词模板 / 数据。
/// 保存后写入数据目录 config.json 并即时生效。
/// </summary>
public sealed class SettingsForm : DpiForm
{
    private readonly AppContext _app;
    private readonly AppConfig _config;

    // 提醒
    private NumericUpDown _numInterval = null!;
    private NumericUpDown _numBreak = null!;
    private NumericUpDown _numPostpone = null!;
    private NumericUpDown _numStartupDelay = null!;
    private CheckBox _chkIdle = null!;
    private NumericUpDown _numIdleThreshold = null!;
    private RadioButton _rbIdleContinue = null!;
    private RadioButton _rbIdleRestart = null!;
    private CheckBox _chkFullscreenDnd = null!;
    private CheckBox _chkEsc = null!;
    private TextBox _txtReminderText = null!;

    // 时间与时段
    private DateTimePicker _dtWorkStart = null!;
    private DateTimePicker _dtWorkEnd = null!;
    private CheckBox _chkLunch = null!;
    private DateTimePicker _dtLunchStart = null!;
    private DateTimePicker _dtLunchEnd = null!;
    private ComboBox _cmbWeekStart = null!;
    private CheckBox _chkAutoStart = null!;

    // 标签
    private ListBox _lstTags = null!;
    private TextBox _txtNewTag = null!;

    // 提示词
    private TextBox _txtDailyPrompt = null!;
    private TextBox _txtWeeklyPrompt = null!;

    // 数据
    private Label _lblDataDir = null!;

    public SettingsForm(AppContext app)
    {
        _app = app;
        _config = app.Config;

        Text = "设置 - TickNote";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 560);
        Font = new Font("Microsoft YaHei UI", 9F);
        MinimizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildReminderTab());
        tabs.TabPages.Add(BuildTimeTab());
        tabs.TabPages.Add(BuildTagsTab());
        tabs.TabPages.Add(BuildPromptTab());
        tabs.TabPages.Add(BuildDataTab());
        root.Controls.Add(tabs, 0, 0);

        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        var btnSave = new Button { Text = "保存", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        btnSave.Click += OnSave;
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        btnRow.Controls.Add(btnSave);
        btnRow.Controls.Add(btnCancel);
        root.Controls.Add(btnRow, 0, 1);

        Controls.Add(root);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        LoadFromConfig();
    }

    // ---------------- 页签构建 ----------------

    private TabPage BuildReminderTab()
    {
        var page = new TabPage("提醒") { AutoScroll = true };
        var grid = MakeGrid();
        _numInterval = AddNumRow(grid, "提醒周期（分钟，5-480）", 5, 480);
        _numBreak = AddNumRow(grid, "活动时长（分钟，1-120）", 1, 120);
        _numPostpone = AddNumRow(grid, "每次推迟时长（分钟）", 1, 60);
        _numStartupDelay = AddNumRow(grid, "启动后首次延迟（分钟）", 0, 120);

        _chkIdle = new CheckBox { Text = "启用空闲检测（键鼠无操作判定离开）", AutoSize = true, Checked = true };
        grid.Controls.Add(_chkIdle);
        _numIdleThreshold = AddNumRow(grid, "空闲阈值（分钟）", 1, 60);

        var idleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        idleRow.Controls.Add(new Label { Text = "回来后：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) });
        _rbIdleContinue = new RadioButton { Text = "继续剩余时间", AutoSize = true, Checked = true };
        _rbIdleRestart = new RadioButton { Text = "重新计时", AutoSize = true };
        idleRow.Controls.Add(_rbIdleContinue);
        idleRow.Controls.Add(_rbIdleRestart);
        grid.Controls.Add(MakeRow("空闲恢复行为", idleRow));

        _chkFullscreenDnd = new CheckBox { Text = "演示/会议勿扰（检测到有应用全屏时自动延后弹窗）", AutoSize = true };
        grid.Controls.Add(_chkFullscreenDnd);
        _chkEsc = new CheckBox { Text = "弹窗中按 ESC = 推迟", AutoSize = true };
        grid.Controls.Add(_chkEsc);

        _txtReminderText = new TextBox { Width = 340 }; // MakeRow 会强制 Dock=Left，必须给足宽度
        grid.Controls.Add(MakeRow("提醒文案（{0}=活动时长）", _txtReminderText));

        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildTimeTab()
    {
        var page = new TabPage("时间与时段") { AutoScroll = true };
        var grid = MakeGrid();

        _dtWorkStart = MakeTimePicker();
        grid.Controls.Add(MakeRow("工作开始", _dtWorkStart));
        _dtWorkEnd = MakeTimePicker();
        grid.Controls.Add(MakeRow("工作结束", _dtWorkEnd));

        _chkLunch = new CheckBox { Text = "启用午休免打扰", AutoSize = true };
        grid.Controls.Add(_chkLunch);
        _dtLunchStart = MakeTimePicker();
        grid.Controls.Add(MakeRow("午休开始", _dtLunchStart));
        _dtLunchEnd = MakeTimePicker();
        grid.Controls.Add(MakeRow("午休结束", _dtLunchEnd));

        _cmbWeekStart = new ComboBox { Dock = DockStyle.Left, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbWeekStart.Items.AddRange(new object[] { "周一", "周日" });
        grid.Controls.Add(MakeRow("一周起始日（周报）", _cmbWeekStart));

        _chkAutoStart = new CheckBox { Text = "开机自动启动（写入注册表 Run 键）", AutoSize = true };
        grid.Controls.Add(_chkAutoStart);

        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildTagsTab()
    {
        var page = new TabPage("快捷标签");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lstTags = new ListBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_lstTags, 0, 0);

        var addRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _txtNewTag = new TextBox { Width = 200 };
        var btnAdd = new Button { Text = "添加", AutoSize = true };
        btnAdd.Click += (_, _) =>
        {
            var t = _txtNewTag.Text.Trim();
            if (t.Length > 0 && !_lstTags.Items.Contains(t))
            {
                _lstTags.Items.Add(t);
                _txtNewTag.Clear();
            }
        };
        var btnRemove = new Button { Text = "删除选中", AutoSize = true };
        btnRemove.Click += (_, _) =>
        {
            if (_lstTags.SelectedItem != null) _lstTags.Items.Remove(_lstTags.SelectedItem);
        };
        addRow.Controls.Add(_txtNewTag);
        addRow.Controls.Add(btnAdd);
        addRow.Controls.Add(btnRemove);
        layout.Controls.Add(addRow, 0, 1);

        layout.Controls.Add(new Label
        {
            Text = "标签显示在提醒弹窗中，点击选中作为该条记录的标签（再点一次取消）。",
            AutoSize = true,
            ForeColor = Color.Gray,
        }, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPromptTab()
    {
        var page = new TabPage("提示词模板");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "日报提示词（占位符：{date} {logs} {stats}）", AutoSize = true }, 0, 0);
        _txtDailyPrompt = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
        layout.Controls.Add(_txtDailyPrompt, 0, 1);
        layout.Controls.Add(new Label { Text = "周报提示词（占位符：{week} {logs} {daily_reports} {stats}）", AutoSize = true }, 0, 2);
        _txtWeeklyPrompt = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
        layout.Controls.Add(_txtWeeklyPrompt, 0, 3);

        var btnDefault = new Button { Text = "恢复默认模板", AutoSize = true };
        btnDefault.Click += (_, _) =>
        {
            _txtDailyPrompt.Text = AppConfig.DefaultDailyPrompt;
            _txtWeeklyPrompt.Text = AppConfig.DefaultWeeklyPrompt;
        };
        layout.Controls.Add(btnDefault, 0, 4);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildDataTab()
    {
        var page = new TabPage("数据") { AutoScroll = true };
        var grid = MakeGrid();

        _lblDataDir = new Label { Text = "", AutoSize = true, MaximumSize = new Size(380, 0) }; // 长路径换行，不被裁
        grid.Controls.Add(MakeRow("当前数据目录", _lblDataDir));

        var btnChange = new Button { Text = "更换数据位置…", AutoSize = true };
        btnChange.Click += OnChangeDataDir;
        grid.Controls.Add(MakeRow("迁移（M0-7）", btnChange));

        var btnBackup = new Button { Text = "立即备份", AutoSize = true };
        btnBackup.Click += (_, _) =>
        {
            try
            {
                var path = BackupService.DoBackup(_config.DataDir);
                MessageBox.Show(this, "已备份到：\n" + path, "备份成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "备份失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        grid.Controls.Add(MakeRow("手动备份（M6-5）", btnBackup));

        var btnExportDb = new Button { Text = "导出数据库副本…", AutoSize = true };
        btnExportDb.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { Filter = "SQLite 数据库|*.db", FileName = "data_backup.db" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                BackupService.ExportDatabase(_config.DataDir, dlg.FileName);
                MessageBox.Show(this, "已导出到：\n" + dlg.FileName, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        grid.Controls.Add(MakeRow("导出（M6-5）", btnExportDb));

        var btnOpen = new Button { Text = "打开数据目录", AutoSize = true };
        btnOpen.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{_config.DataDir}\""); } catch { }
        };
        grid.Controls.Add(MakeRow("浏览", btnOpen));

        page.Controls.Add(grid);
        return page;
    }

    // ---------------- 布局辅助 ----------------

    private static TableLayoutPanel MakeGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12),
            ColumnCount = 1,
        };
        return grid;
    }

    private static Panel MakeRow(string label, Control control)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 3, 0, 3) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
        control.Dock = DockStyle.Left;
        row.Controls.Add(control, 1, 0);
        var wrapper = new Panel { AutoSize = true, Dock = DockStyle.Top };
        wrapper.Controls.Add(row);
        return wrapper;
    }

    private NumericUpDown AddNumRow(TableLayoutPanel grid, string label, int min, int max)
    {
        var num = new NumericUpDown { Minimum = min, Maximum = max, Width = 90 };
        grid.Controls.Add(MakeRow(label, num));
        return num;
    }

    private static DateTimePicker MakeTimePicker() => new()
    {
        Format = DateTimePickerFormat.Time,
        ShowUpDown = true,
        Width = 100,
    };

    private static DateTime ParseTimeOr(string hhmm, int hour, int minute)
        => TimeSpan.TryParse(hhmm, out var t) ? DateTime.Today + t : DateTime.Today.AddHours(hour).AddMinutes(minute);

    // ---------------- 读写配置 ----------------

    private void LoadFromConfig()
    {
        var c = _config;
        _numInterval.Value = Math.Clamp(c.IntervalMinutes, 5, 480);
        _numBreak.Value = Math.Clamp(c.BreakMinutes, 1, 120);
        _numPostpone.Value = Math.Clamp(c.PostponeMinutes, 1, 60);
        _numStartupDelay.Value = Math.Clamp(c.StartupDelayMinutes, 0, 120);
        _chkIdle.Checked = c.IdleDetectionEnabled;
        _numIdleThreshold.Value = Math.Clamp(c.IdleThresholdMinutes, 1, 60);
        _rbIdleRestart.Checked = c.IdleRestart;
        _rbIdleContinue.Checked = !c.IdleRestart;
        _chkFullscreenDnd.Checked = c.FullscreenDndEnabled;
        _chkEsc.Checked = c.EscIsPostpone;
        _txtReminderText.Text = c.ReminderText;

        _dtWorkStart.Value = ParseTimeOr(c.WorkStart, 8, 30);
        _dtWorkEnd.Value = ParseTimeOr(c.WorkEnd, 18, 30);
        _chkLunch.Checked = c.LunchEnabled;
        _dtLunchStart.Value = ParseTimeOr(c.LunchStart, 12, 0);
        _dtLunchEnd.Value = ParseTimeOr(c.LunchEnd, 13, 30);
        _cmbWeekStart.SelectedIndex = c.WeekStartsMonday ? 0 : 1;
        _chkAutoStart.Checked = c.AutoStart;

        _lstTags.Items.Clear();
        foreach (var t in c.Tags) _lstTags.Items.Add(t);

        _txtDailyPrompt.Text = c.DailyPromptTemplate;
        _txtWeeklyPrompt.Text = c.WeeklyPromptTemplate;
        _lblDataDir.Text = c.DataDir;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var c = _config;
        c.IntervalMinutes = (int)_numInterval.Value;
        c.BreakMinutes = (int)_numBreak.Value;
        c.PostponeMinutes = (int)_numPostpone.Value;
        c.StartupDelayMinutes = (int)_numStartupDelay.Value;
        c.IdleDetectionEnabled = _chkIdle.Checked;
        c.IdleThresholdMinutes = (int)_numIdleThreshold.Value;
        c.IdleRestart = _rbIdleRestart.Checked;
        c.FullscreenDndEnabled = _chkFullscreenDnd.Checked;
        c.EscIsPostpone = _chkEsc.Checked;
        c.ReminderText = _txtReminderText.Text.Trim().Length > 0 ? _txtReminderText.Text.Trim() : c.ReminderText;

        c.WorkStart = _dtWorkStart.Value.ToString("HH:mm");
        c.WorkEnd = _dtWorkEnd.Value.ToString("HH:mm");
        c.LunchEnabled = _chkLunch.Checked;
        c.LunchStart = _dtLunchStart.Value.ToString("HH:mm");
        c.LunchEnd = _dtLunchEnd.Value.ToString("HH:mm");
        c.WeekStartsMonday = _cmbWeekStart.SelectedIndex == 0;

        c.Tags = _lstTags.Items.Cast<string>().ToList();
        c.DailyPromptTemplate = _txtDailyPrompt.Text;
        c.WeeklyPromptTemplate = _txtWeeklyPrompt.Text;

        // M5-4 开机自启
        c.AutoStart = _chkAutoStart.Checked;
        AutoStart.Apply(c.AutoStart);

        c.Save();                 // M6-3 设置只写数据目录 config.json
        _app.Engine.ReloadConfig();
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>M0-7：更换数据位置——先自动备份，再完整迁移，最后更新引导文件并重启。</summary>
    private void OnChangeDataDir(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择新的数据保存目录" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var newDir = dlg.SelectedPath;
        if (string.Equals(newDir.TrimEnd('\\'), _config.DataDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return;

        var hasData = File.Exists(Path.Combine(newDir, "data.db"));
        var msg = hasData
            ? "新目录中已存在本软件的数据，将直接接管使用（当前数据不再迁移）。\n是否继续？"
            : "将把当前数据完整迁移到新目录（迁移前自动备份，旧目录保留作为兜底），随后软件自动重启。\n是否继续？";
        if (MessageBox.Show(this, msg, "更换数据位置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            OnSave(sender, e); // 先落盘当前设置
            BackupService.DoBackup(_config.DataDir);
            if (!hasData) Migration.MigrateTo(_config.DataDir, newDir);
            BootConfig.Save(newDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "迁移失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(this, "数据位置已更换，软件将重启。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _app.RestartApplication();
    }
}
