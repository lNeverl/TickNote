using SedentaryReminder.Log;
using SedentaryReminder.Report;
using SedentaryReminder.Settings;
using SedentaryReminder.Todo;

namespace SedentaryReminder;

public enum MainPage { Todos, History, ManualEntry, Reports, GenerateReport, ImportReport, Export, Reminder, Settings, About }

/// <summary>托盘应用的唯一主窗口。页面按需创建，切换和收起均保留原有控件与输入。</summary>
public sealed class MainForm : DpiForm
{
    private readonly AppContext _app;
    private readonly MainWindowState _state;
    private readonly Dictionary<MainPage, WorkspacePage> _pages = new();
    private readonly Dictionary<MainPage, Button> _navigationButtons = new();
    private readonly FlowLayoutPanel _navigation;
    private readonly Panel _content;
    private readonly Label _title;
    private readonly Label _description;
    private readonly ToolStripStatusLabel _notice;
    private readonly ToolStripStatusLabel _reminderStatus;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private bool _allowClose;
    private bool _restoredPosition;

    private static readonly (MainPage Page, string Title, string Description)[] Sections =
    {
        (MainPage.Todos, "每日待办", "把要做的事记下来，一件一件完成。"),
        (MainPage.History, "历史流水", "每一段工作都有记录，回顾时有据可查。"),
        (MainPage.ManualEntry, "补记一条", "补充刚才的工作，也可以记录过去的某一段时间。"),
        (MainPage.Reports, "我的报告", "查看、编辑和复制已保存的日报与周报"),
        (MainPage.GenerateReport, "生成报告", "复制提示词和工作数据，交给 AI 生成日报或周报"),
        (MainPage.ImportReport, "导入报告", "粘贴 AI 生成的成稿，保存到本地报告库"),
        (MainPage.Export, "导出流水", "按日期范围导出 Markdown 或 CSV 文件"),
        (MainPage.Reminder, "提醒控制", "查看当前状态，暂停或恢复久坐提醒"),
        (MainPage.Settings, "设置", "提醒规则、工作时段、快捷标签、提示词与数据管理"),
        (MainPage.About, "关于", "应用版本和数据保存位置"),
    };

    public MainPage SelectedPage { get; private set; }
    public WorkspacePage CurrentPage => _pages[SelectedPage];

    public MainForm(AppContext app)
    {
        _app = app;
        _state = MainWindowState.Load(app.Config.DataDir);
        Text = $"TickNote v{AppInfo.Version}";
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiLayout.PageBack;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Math.Clamp(_state.ClientWidth, 760, 1800), Math.Clamp(_state.ClientHeight, 480, 1200));
        MinimumSize = new Size(760, 500);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(10, 20, 10, 10), Margin = Padding.Empty, BackColor = UiLayout.Sidebar,
        };
        var brand = new Label { Text = "TickNote", AutoSize = false, Height = 44, Font = new Font(Font.FontFamily, 15F, FontStyle.Bold), Margin = new Padding(10, 0, 0, 12) };
        _navigation.Controls.Add(brand);
        foreach (var section in Sections)
        {
            var page = section.Page;
            var group = page switch { MainPage.Todos => "工作", MainPage.Reports => "报告", MainPage.Export => "工具", MainPage.Settings => "偏好", _ => null };
            if (group != null)
                _navigation.Controls.Add(new Label { Text = group, AutoSize = false, Height = 27, ForeColor = UiLayout.Muted, Margin = new Padding(12, 10, 0, 0) });
            var button = UiLayout.Button(section.Title);
            button.AutoSize = false;
            button.Height = 36;
            button.MinimumSize = new Size(0, 36);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Margin = new Padding(0, 0, 0, 4);
            button.Padding = new Padding(10, 4, 4, 4);
            button.FlatAppearance.BorderSize = 0;
            button.AccessibleName = "切换到" + section.Title;
            button.Click += (_, _) => Navigate(page);
            _navigationButtons.Add(page, button);
            _navigation.Controls.Add(button);
        }
        var exit = UiLayout.Button("退出应用");
        exit.AutoSize = false;
        exit.Height = 36;
        exit.TextAlign = ContentAlignment.MiddleLeft;
        exit.Margin = new Padding(0, 16, 0, 0);
        exit.Click += (_, _) => _app.ExitApplication();
        _navigation.Controls.Add(exit);
        _navigation.SizeChanged += (_, _) => ResizeNavigation();
        _navigation.Layout += (_, _) => ResizeNavigation();
        root.Controls.Add(_navigation, 0, 0);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(24, 22, 24, 14) };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 20) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _title = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 15F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
        _description = new Label { AutoSize = true, ForeColor = UiLayout.Muted, Margin = Padding.Empty };
        heading.Controls.Add(_title, 0, 0);
        heading.Controls.Add(_description, 0, 1);
        heading.SizeChanged += (_, _) =>
        {
            _title.MaximumSize = new Size(Math.Max(1, heading.ClientSize.Width), 0);
            _description.MaximumSize = new Size(Math.Max(1, heading.ClientSize.Width), 0);
        };
        workspace.Controls.Add(heading, 0, 0);
        _content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        workspace.Controls.Add(_content, 0, 1);
        root.Controls.Add(workspace, 1, 0);

        var status = new StatusStrip { SizingGrip = true, Dock = DockStyle.Fill, BackColor = Color.White, Margin = Padding.Empty };
        _notice = new ToolStripStatusLabel("数据保存在本地") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _reminderStatus = new ToolStripStatusLabel();
        status.Items.Add(_notice);
        status.Items.Add(_reminderStatus);
        root.SetColumnSpan(status, 2);
        root.Controls.Add(status, 0, 1);
        Controls.Add(root);

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _app.Engine.StatusChanged += RefreshStatus;
        Navigate(Enum.TryParse<MainPage>(_state.LastPage, out var initial) && Enum.IsDefined(initial) ? initial : MainPage.Todos);
        RefreshStatus();
    }

    public void Navigate(MainPage page)
    {
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        if (_pages.TryGetValue(SelectedPage, out var previous)) previous.Hide();
        if (!_pages.TryGetValue(page, out var content))
        {
            content = CreatePage(page);
            content.Visible = false;
            _content.Controls.Add(content);
            content.Bounds = _content.ClientRectangle;
            if (IsHandleCreated && AppliedDpi != 96)
            {
                content.CreateControl();
                content.SuspendLayout();
                try
                {
                    var factor = new SizeF(AppliedDpi / 96F, AppliedDpi / 96F);
                    foreach (Control child in content.Controls) child.Scale(factor);
                }
                finally { content.ResumeLayout(true); }
            }
            _pages.Add(page, content);
            UiLayout.ApplyTheme(content);
        }
        SelectedPage = page;
        content.Show();
        content.BringToFront();
        content.OnActivated();
        var section = Sections.Single(section => section.Page == page);
        _title.Text = section.Title;
        _description.Text = section.Description;
        foreach (var (key, button) in _navigationButtons)
        {
            button.BackColor = key == page ? UiLayout.Surface : UiLayout.Sidebar;
            button.ForeColor = key == page ? UiLayout.Accent : UiLayout.Text;
            button.AccessibleDescription = key == page ? "当前页面" : "";
        }
        _state.LastPage = page.ToString();
    }

    private WorkspacePage CreatePage(MainPage page)
    {
        switch (page)
        {
            case MainPage.Todos:
                var todos = new TodoPage(_app.Db, _app.Config);
                todos.ShowAllPending();
                return todos;
            case MainPage.History: return new LogPage(_app, null);
            case MainPage.ManualEntry: return new ManualEntryPage(_app.Db, _app.Config);
            case MainPage.Reports: return new ReportPage(_app);
            case MainPage.GenerateReport: return new GenerateReportPage(_app);
            case MainPage.ImportReport: return new ImportReportPage(_app);
            case MainPage.Export: return new ExportPage(_app);
            case MainPage.Reminder: return new ReminderControlPage(_app);
            case MainPage.Settings: return new SettingsPage(_app);
            case MainPage.About: return new AboutPage(_app.Config.DataDir);
            default: throw new ArgumentOutOfRangeException(nameof(page));
        }
    }

    private void ResizeNavigation()
    {
        var width = Math.Max(1, _navigation.ClientSize.Width - _navigation.Padding.Horizontal);
        foreach (Control control in _navigation.Controls)
        {
            var available = Math.Max(1, width - control.Margin.Horizontal);
            if (control.Width != available) control.Width = available;
        }
    }

    private void RefreshStatus()
    {
        _reminderStatus.Text = _app.Engine.GetStatus().Text;
        if (_pages.TryGetValue(MainPage.Reminder, out var page) && page.Visible) page.OnActivated();
    }

    public void ShowNotification(string message) => _notice.Text = message;

    internal bool ConfirmExit(WorkspacePage? except = null, Func<WorkspacePage, DialogResult>? decide = null)
    {
        foreach (var page in _pages.Values)
            if (!ReferenceEquals(page, except) && page.HasUnsavedChanges && !page.ConfirmExit(decide?.Invoke(page)))
            {
                Navigate(_pages.Single(pair => ReferenceEquals(pair.Value, page)).Key);
                Show();
                Activate();
                return false;
            }
        return true;
    }

    internal void SaveWindowState()
    {
        CaptureNormalBounds();
        if (WindowState != FormWindowState.Minimized) _state.Maximized = WindowState == FormWindowState.Maximized;
        _state.Save(_app.Config.DataDir);
    }

    private void CaptureNormalBounds()
    {
        if (WindowState == FormWindowState.Normal)
        {
            _state.ClientWidth = (int)Math.Round(ClientSize.Width * 96.0 / DeviceDpi);
            _state.ClientHeight = (int)Math.Round(ClientSize.Height * 96.0 / DeviceDpi);
            _state.Left = Left;
            _state.Top = Top;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_restoredPosition && IsHandleCreated) CaptureNormalBounds();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_restoredPosition && IsHandleCreated) CaptureNormalBounds();
    }

    internal void CloseForExit()
    {
        SaveWindowState();
        _allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWindowState();
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        if (!_restoredPosition)
        {
            _restoredPosition = true;
            if (_state.Left is int left && _state.Top is int top) Location = new Point(left, top);
            if (_state.Maximized) WindowState = FormWindowState.Maximized;
        }
        base.OnShown(e);
        ResizeNavigation();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_statusTimer != null) _statusTimer.Enabled = Visible;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SaveWindowState();
            _app.Engine.StatusChanged -= RefreshStatus;
            _statusTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
