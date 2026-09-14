using SedentaryReminder.Data;

namespace SedentaryReminder.Overlay;

/// <summary>
/// 单块屏幕上的全屏提醒窗体（M2）。深色半透明遮罩 + 居中卡片：
/// 提醒文案、倒计时、输入框、快捷标签、今日流水（含一键续记）、三按钮。
/// 布局全部用容器（H3），禁止绝对坐标。
/// </summary>
public sealed class OverlayForm : Form
{
    private const double TargetOpacity = 0.85;
    private const int LogPanelHeight = 150;

    private static readonly Color CardBack = Color.FromArgb(32, 32, 40);
    private static readonly Color CardFore = Color.FromArgb(235, 235, 240);
    private static readonly Color AccentGreen = Color.FromArgb(46, 125, 50);
    private static readonly Color TagBack = Color.FromArgb(58, 58, 72);

    private readonly OverlayManager _manager;
    private readonly AppConfig _config;
    private readonly TextBox _txtInput;
    private readonly Label _lblCountdown;
    private readonly ProgressBar _progress;
    private readonly Button _btnDone;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private bool _suppressTextEvent;

    public bool IsPrimary { get; }

    public OverlayForm(OverlayManager manager, AppConfig config, Screen screen, List<WorkLogEntry> todayLogs)
    {
        _manager = manager;
        _config = config;
        IsPrimary = screen.Primary;

        FormBorderStyle = FormBorderStyle.None;
        Bounds = screen.Bounds;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;                 // M2-12 置顶
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0;                    // M2-14 淡入
        KeyPreview = true;              // M2-13 ESC
        Font = new Font("Microsoft YaHei UI", 10F);

        // —— 居中卡片：3x3 网格，中行中列放卡片，四周百分比留白 ——
        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var card = new TableLayoutPanel
        {
            AutoSize = true,
            MinimumSize = new Size(640, 0),
            BackColor = CardBack,
            ForeColor = CardFore,
            Padding = new Padding(28, 22, 28, 22),
            ColumnCount = 1,
            Anchor = AnchorStyles.None,
        };

        // 标题（M2-4 文案可自定义）
        var lblTitle = new Label
        {
            Text = string.Format(config.ReminderText, config.BreakMinutes),
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };

        // 倒计时（M2-5）
        var countdownRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        countdownRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        countdownRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _lblCountdown = new Label
        {
            Font = new Font("Consolas", 16F, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 14, 0),
        };
        _progress = new ProgressBar { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 14 };
        countdownRow.Controls.Add(_lblCountdown, 0, 0);
        countdownRow.Controls.Add(_progress, 1, 0);
        countdownRow.Margin = new Padding(0, 0, 0, 14);

        var lblInput = new Label { Text = "这段时间在做什么？", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };

        // M3-1 随手记输入框（多行）
        _txtInput = new TextBox
        {
            Multiline = true,
            Height = 64,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 22, 28),
            ForeColor = CardFore,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
        };
        _txtInput.Margin = new Padding(0, 0, 0, 8);
        _txtInput.TextChanged += (_, _) =>
        {
            if (!_suppressTextEvent) _manager.OnInputChanged(this, _txtInput.Text);
        };

        // M3-3 快捷标签：点击插入输入框光标处
        var tagPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        foreach (var tag in config.Tags)
        {
            var b = MakeTagButton(tag);
            b.Click += (_, _) => InsertTag(tag);
            tagPanel.Controls.Add(b);
        }

        var lblToday = new Label
        {
            Text = "—— 今天已记录 ——",
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 160),
            Margin = new Padding(0, 0, 0, 4),
        };

        // M2-9 今日流水面板 + M2-10 一键续记
        var logPanel = BuildLogPanel(todayLogs);
        logPanel.Margin = new Padding(0, 0, 0, 12);

        // 三按钮（M2-6/7/8）
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        _btnDone = MakeActionButton($"刚活动完", AccentGreen);
        _btnDone.Click += (_, _) => _manager.OnAction("done");
        var btnSkip = MakeActionButton("跳过", Color.FromArgb(90, 90, 100));
        btnSkip.Click += (_, _) => _manager.OnAction("skip");
        var btnPostpone = MakeActionButton($"推迟{config.PostponeMinutes}分钟", Color.FromArgb(120, 90, 30));
        btnPostpone.Click += (_, _) => _manager.OnAction("postpone");
        btnRow.Controls.Add(_btnDone);
        btnRow.Controls.Add(btnSkip);
        btnRow.Controls.Add(btnPostpone);

        card.Controls.Add(lblTitle);
        card.Controls.Add(countdownRow);
        card.Controls.Add(lblInput);
        card.Controls.Add(_txtInput);
        card.Controls.Add(tagPanel);
        card.Controls.Add(lblToday);
        card.Controls.Add(logPanel);
        card.Controls.Add(btnRow);

        center.Controls.Add(card, 1, 1);
        Controls.Add(center);

        // M2-14 淡入 200ms
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _fadeTimer.Tick += (_, _) =>
        {
            Opacity += TargetOpacity / 10.0;
            if (Opacity >= TargetOpacity) { Opacity = TargetOpacity; _fadeTimer.Stop(); }
        };
        _fadeTimer.Start();
    }

    private Panel BuildLogPanel(List<WorkLogEntry> logs)
    {
        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            Height = LogPanelHeight,
            AutoScroll = true,
            BackColor = Color.FromArgb(26, 26, 32),
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        if (logs.Count == 0)
        {
            flow.Controls.Add(new Label
            {
                Text = "（今天还没有记录）",
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 120, 130),
                Margin = new Padding(6),
            });
        }
        foreach (var log in logs)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(2),
            };
            var text = $"{log.EndTime ?? log.StartTime ?? "--:--"}";
            if (log.Tag is { Length: > 0 }) text += $" [{log.Tag}]";
            text += " " + Truncate(log.Content ?? "", 28);
            row.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = CardFore,
                Margin = new Padding(6, 7, 6, 3),
            });
            var btnContinue = new Button
            {
                Text = "继续",
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = TagBack,
                ForeColor = CardFore,
                Margin = new Padding(2),
                Padding = new Padding(4, 0, 4, 0),
            };
            var captured = log;
            btnContinue.Click += (_, _) => _manager.OnContinueRequested(captured);
            row.Controls.Add(btnContinue);
            flow.Controls.Add(row);
        }
        outer.Controls.Add(flow);
        return outer;
    }

    private static string Truncate(string s, int max)
    {
        var flat = s.Replace("\r", " ").Replace("\n", " ");
        return flat.Length > max ? flat[..max] + "…" : flat;
    }

    private static Button MakeTagButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = TagBack,
        ForeColor = CardFore,
        Margin = new Padding(0, 0, 6, 4),
        Padding = new Padding(6, 1, 6, 1),
    };

    private static Button MakeActionButton(string text, Color back) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = back,
        ForeColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
        Margin = new Padding(0, 0, 10, 0),
        Padding = new Padding(12, 4, 12, 4),
    };

    // ---------------- 外部同步接口 ----------------

    public void SetInputText(string text)
    {
        if (_txtInput.Text == text) return;
        _suppressTextEvent = true;
        _txtInput.Text = text;
        _txtInput.SelectionStart = _txtInput.TextLength;
        _suppressTextEvent = false;
    }

    public void UpdateCountdown(int remainingSec, int totalSec)
    {
        _lblCountdown.Text = $"{remainingSec / 60:D2}:{remainingSec % 60:D2}";
        _progress.Maximum = Math.Max(1, totalSec);
        _progress.Value = Math.Clamp(totalSec - remainingSec, 0, _progress.Maximum);
    }

    /// <summary>M2-11：倒计时结束高亮"刚活动完"。</summary>
    public void OnCountdownFinished()
    {
        _btnDone.BackColor = Color.FromArgb(76, 175, 80);
        _btnDone.Text = "刚活动完 ✓";
    }

    public void FocusInput()
    {
        BeginInvoke(() => { Activate(); _txtInput.Focus(); });
    }

    private void InsertTag(string tag)
    {
        var insert = $"[{tag}] ";
        var pos = _txtInput.SelectionStart;
        _txtInput.Text = _txtInput.Text.Insert(pos, insert);
        _txtInput.SelectionStart = pos + insert.Length;
        _txtInput.Focus();
        // Text 赋值已触发 TextChanged → 同步到其他屏
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // M2-13：ESC 默认 = 推迟（可在设置中关闭）
        if (e.KeyCode == Keys.Escape && _config.EscIsPostpone)
        {
            e.Handled = true;
            _manager.OnAction("postpone");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fadeTimer.Dispose();
        base.Dispose(disposing);
    }
}
