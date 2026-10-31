namespace SedentaryReminder;

public sealed class ReminderControlPage : WorkspacePage
{
    private readonly AppContext _app;
    private readonly Label _status;
    private readonly Label _countdown;
    private readonly Panel _progress;
    private readonly Label _rules;

    public ReminderControlPage(AppContext app)
    {
        _app = app;
        Text = "提醒控制";
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "下一次活动提醒", AutoSize = true, ForeColor = UiLayout.Muted, Margin = new Padding(0, 0, 0, 8) });
        _countdown = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 32F, FontStyle.Bold), ForeColor = UiLayout.Accent, Margin = new Padding(0, 0, 0, 10), AccessibleName = "距下次提醒的剩余时间" };
        root.Controls.Add(_countdown);
        _progress = new Panel { Dock = DockStyle.Fill, Height = 6, BackColor = UiLayout.Sidebar, Margin = new Padding(0, 0, 0, 16) };
        _progress.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(UiLayout.Accent);
            e.Graphics.FillRectangle(brush, 0, 0, (int)(_progress.ClientSize.Width * (_app.Engine.GetCountdownProgress() ?? 0)), _progress.ClientSize.Height);
        };
        root.Controls.Add(_progress);
        _status = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 20), AccessibleName = "当前提醒状态" };
        root.Controls.Add(_status);
        _rules = new Label { AutoSize = true, ForeColor = UiLayout.Muted, Margin = new Padding(0, 0, 0, 22) };
        root.Controls.Add(_rules);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty };
        var pause30 = UiLayout.Button("暂停 30 分钟");
        pause30.Click += (_, _) => _app.PauseReminder(TimeSpan.FromMinutes(30));
        var pause60 = UiLayout.Button("暂停 1 小时");
        pause60.Click += (_, _) => _app.PauseReminder(TimeSpan.FromHours(1));
        var pauseToday = UiLayout.Button("暂停到今天下班");
        pauseToday.Click += (_, _) => _app.PauseReminderToday();
        var resume = UiLayout.Button("恢复提醒", primary: true);
        resume.Click += (_, _) => _app.ResumeReminder();
        foreach (var button in new[] { pause30, pause60, pauseToday, resume })
        {
            button.Margin = new Padding(0, 0, 10, 10);
            actions.Controls.Add(button);
        }
        root.Controls.Add(actions);
        var now = UiLayout.Button("现在休息一下");
        now.Click += (_, _) => _app.OpenReminder(DateTime.Now);
        var settings = UiLayout.Button("调整提醒规则");
        settings.Click += (_, _) => _app.ShowSettings();
        root.Controls.Add(UiLayout.ButtonRow(false, now, settings));
        root.Controls.Add(new Label { Text = "收起主窗口后，久坐提醒仍在托盘运行。", AutoSize = true, ForeColor = UiLayout.Muted, Margin = new Padding(0, 22, 0, 0) });
        root.SizeChanged += (_, _) =>
        {
            foreach (var label in root.Controls.OfType<Label>()) label.MaximumSize = new Size(Math.Max(1, root.ClientSize.Width), 0);
        };
        viewport.Controls.Add(root);
        Controls.Add(viewport);
        OnActivated();
    }

    public override void OnActivated()
    {
        _status.Text = _app.Engine.GetStatus().Text;
        var remaining = _app.Engine.GetCountdownRemaining();
        _countdown.Text = remaining is TimeSpan time ? $"{(int)time.TotalMinutes:00}:{time.Seconds:00}" : "— : —";
        _rules.Text = $"每工作 {_app.Config.IntervalMinutes} 分钟，活动 {_app.Config.BreakMinutes} 分钟；每次可推迟 {_app.Config.PostponeMinutes} 分钟。";
        _progress.Invalidate();
    }
}
