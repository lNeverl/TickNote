using SedentaryReminder.Data;

namespace SedentaryReminder.Overlay;

/// <summary>
/// 单块屏幕上的全屏提醒窗体（M2）。深色半透明遮罩 + 居中卡片：
/// 提醒文案、倒计时、输入框、快捷标签（点选式）、今日流水（继续/删除）、保存按钮、三按钮。
/// 布局全部用容器（H3），禁止绝对坐标；高 DPI 走 AutoScaleMode.None + ApplyDpiScale 手动缩放。
/// </summary>
public sealed class OverlayForm : Form
{
    private const double TargetOpacity = 0.85;
    private const int LogPanelHeight = 150;

    private static readonly Color CardBack = Color.FromArgb(32, 32, 40);
    private static readonly Color CardFore = Color.FromArgb(235, 235, 240);
    private static readonly Color AccentGreen = Color.FromArgb(46, 125, 50);
    private static readonly Color AccentBlue = Color.FromArgb(40, 90, 160);
    private static readonly Color TagBack = Color.FromArgb(58, 58, 72);
    private static readonly Color TagSelected = Color.FromArgb(46, 125, 50);

    private readonly OverlayManager _manager;
    private readonly AppConfig _config;
    private readonly Screen _screen;
    private readonly TextBox _txtInput;
    private readonly Label _lblCountdown;
    private readonly ProgressBar _progress;
    private readonly Button _btnDone;
    private readonly Button _btnSave;
    private readonly TableLayoutPanel _card;
    private readonly Label _lblTitle;
    private readonly Label _lblHint;
    private readonly Panel _logPanelOuter;
    private readonly FlowLayoutPanel _logFlow;
    private readonly Dictionary<string, Button> _tagButtons = new();
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly List<Font> _fonts = new(); // 弹窗每天弹出十余次，字体必须随窗体释放（N1 内存硬指标）
    private bool _suppressTextEvent;

    public bool IsPrimary { get; }

    public OverlayForm(OverlayManager manager, AppConfig config, Screen screen, List<WorkLogEntry> todayLogs)
    {
        _manager = manager;
        _config = config;
        _screen = screen;
        IsPrimary = screen.Primary;

        // 高 DPI 关键：关闭框架自动缩放（它会把设好的 Bounds 按 DPI 比例二次放大导致越屏），
        // 改为 Bounds 永远精确等于本屏逻辑尺寸，布局尺寸在 ApplyDpiScale 里按 DeviceDpi 手动缩放。
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        Bounds = screen.Bounds;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;                 // M2-12 置顶
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0;                    // M2-14 淡入
        KeyPreview = true;              // M2-13 ESC
        Font = TrackFont(new Font("Microsoft YaHei UI", 10F));

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
            AutoScroll = true, // 极端 DPI/小屏下内容超高时出滚动条，而不是把按钮裁没
            MinimumSize = new Size(640, 0),
            BackColor = CardBack,
            ForeColor = CardFore,
            Padding = new Padding(28, 22, 28, 22),
            ColumnCount = 1,
            Anchor = AnchorStyles.None,
        };
        _card = card;

        // 标题（M2-4 文案可自定义）。用户自定义文案可能含花括号，string.Format 会抛异常，
        // 只认 {0} 占位符做简单替换
        var lblTitle = new Label
        {
            Text = config.ReminderText.Replace("{0}", config.BreakMinutes.ToString()),
            Font = TrackFont(new Font(Font.FontFamily, 15F, FontStyle.Bold)),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        _lblTitle = lblTitle;

        // 倒计时（M2-5）
        var countdownRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        countdownRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        countdownRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _lblCountdown = new Label
        {
            Font = TrackFont(new Font("Consolas", 16F, FontStyle.Bold)),
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
        };
        _txtInput.Margin = new Padding(0, 0, 0, 6);
        _txtInput.TextChanged += (_, _) =>
        {
            if (!_suppressTextEvent) _manager.OnInputChanged(this, _txtInput.Text);
        };

        // 标签行：左侧点选式标签（M3-3），右侧独立的“保存”按钮
        var tagRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var tagPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };
        foreach (var tag in config.Tags)
        {
            var b = MakeTagButton(tag);
            b.Click += (_, _) => _manager.OnTagToggled(tag);
            tagPanel.Controls.Add(b);
            _tagButtons[tag] = b;
        }
        _btnSave = new Button
        {
            Text = "保存",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentBlue,
            ForeColor = Color.White,
            Font = TrackFont(new Font(Font.FontFamily, 10F, FontStyle.Bold)),
            Margin = new Padding(4, 0, 0, 0),
            Padding = new Padding(12, 2, 12, 2),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        _btnSave.Click += (_, _) => _manager.OnSaveRequested();
        tagRow.Controls.Add(tagPanel, 0, 0);
        tagRow.Controls.Add(_btnSave, 1, 0);
        tagRow.Margin = new Padding(0, 0, 0, 10);

        var lblToday = new Label
        {
            Text = "—— 今天已记录 ——",
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 160),
            Margin = new Padding(0, 0, 0, 4),
        };

        // M2-9 今日流水面板（继续 / 删除）
        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            Height = LogPanelHeight,
            AutoScroll = true,
            BackColor = Color.FromArgb(26, 26, 32),
            Margin = new Padding(0, 0, 0, 12),
        };
        _logFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        outer.Controls.Add(_logFlow);
        _logPanelOuter = outer;

        // 三按钮（M2-6/7/8）
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        var actionFont = TrackFont(new Font("Microsoft YaHei UI", 10F, FontStyle.Bold));
        _btnDone = MakeActionButton("刚活动完", AccentGreen, actionFont);
        _btnDone.Click += (_, _) => _manager.OnAction("done");
        var btnSkip = MakeActionButton("跳过", Color.FromArgb(90, 90, 100), actionFont);
        btnSkip.Click += (_, _) => _manager.OnAction("skip");
        var btnPostpone = MakeActionButton($"推迟{config.PostponeMinutes}分钟", Color.FromArgb(120, 90, 30), actionFont);
        btnPostpone.Click += (_, _) => _manager.OnAction("postpone");
        btnRow.Controls.Add(_btnDone);
        btnRow.Controls.Add(btnSkip);
        btnRow.Controls.Add(btnPostpone);

        // 避免误解：倒计时只是建议，三个按钮都立即关窗，不用等
        var lblHint = new Label
        {
            Text = "倒计时只是建议，不用等它走完——刚活动过就点「刚活动完」立即关窗，开始下一周期。",
            AutoSize = true,
            ForeColor = Color.FromArgb(140, 140, 150),
            Margin = new Padding(0, 8, 0, 0),
        };
        _lblHint = lblHint;

        card.Controls.Add(lblTitle);
        card.Controls.Add(countdownRow);
        card.Controls.Add(lblInput);
        card.Controls.Add(_txtInput);
        card.Controls.Add(tagRow);
        card.Controls.Add(lblToday);
        card.Controls.Add(outer);
        card.Controls.Add(btnRow);
        card.Controls.Add(lblHint);

        center.Controls.Add(card, 1, 1);
        Controls.Add(center);

        RefreshLogs(todayLogs);

        // M2-14 淡入 200ms
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _fadeTimer.Tick += (_, _) =>
        {
            Opacity += TargetOpacity / 10.0;
            if (Opacity >= TargetOpacity) { Opacity = TargetOpacity; _fadeTimer.Stop(); }
        };
        _fadeTimer.Start();
    }

    /// <summary>重建今日流水列表（保存/删除后由管理器调用，所有屏同步刷新）。</summary>
    public void RefreshLogs(List<WorkLogEntry> logs)
    {
        _logFlow.Controls.Clear();
        if (logs.Count == 0)
        {
            _logFlow.Controls.Add(new Label
            {
                Text = "（今天还没有记录）",
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 120, 130),
                Margin = new Padding(6),
            });
            return;
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
            var text = log.EndTime ?? log.StartTime ?? "--:--";
            if (log.Tag is { Length: > 0 }) text += $" [{log.Tag}]";
            text += " " + Truncate(log.Content ?? "", 24);
            row.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = CardFore,
                Margin = new Padding(6, 7, 6, 3),
            });
            var captured = log;
            var btnContinue = MakeRowButton("继续");
            btnContinue.Click += (_, _) => _manager.OnContinueRequested(captured);
            var btnDelete = MakeRowButton("删除");
            btnDelete.ForeColor = Color.FromArgb(240, 160, 160);
            btnDelete.Click += (_, _) => _manager.OnDeleteRequested(captured);
            row.Controls.Add(btnContinue);
            row.Controls.Add(btnDelete);
            _logFlow.Controls.Add(row);
        }
    }

    private static Button MakeRowButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = TagBack,
        ForeColor = CardFore,
        Margin = new Padding(2),
        Padding = new Padding(4, 0, 4, 0),
    };

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

    private static Button MakeActionButton(string text, Color back, Font font) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = back,
        ForeColor = Color.White,
        Font = font,
        Margin = new Padding(0, 0, 10, 0),
        Padding = new Padding(12, 4, 12, 4),
    };

    /// <summary>登记本窗体创建的字体，Dispose 时统一释放。</summary>
    private Font TrackFont(Font font)
    {
        _fonts.Add(font);
        return font;
    }

    // ---------------- 外部同步接口 ----------------

    public void SetInputText(string text)
    {
        if (_txtInput.Text == text) return;
        _suppressTextEvent = true;
        _txtInput.Text = text;
        _txtInput.SelectionStart = _txtInput.TextLength;
        _suppressTextEvent = false;
    }

    /// <summary>点选式标签：更新本屏所有标签按钮的选中高亮。</summary>
    public void SetSelectedTag(string? selectedTag)
    {
        foreach (var (tag, btn) in _tagButtons)
            btn.BackColor = tag == selectedTag ? TagSelected : TagBack;
    }

    /// <summary>保存按钮状态（冷却倒计时由管理器统一驱动，各屏一致）。</summary>
    public void SetSaveButton(bool enabled, string text)
    {
        _btnSave.Enabled = enabled;
        _btnSave.Text = text;
    }

    public void UpdateCountdown(int remainingSec, int totalSec)
    {
        _lblCountdown.Text = $"{remainingSec / 60:D2}:{remainingSec % 60:D2}";
        _progress.Maximum = Math.Max(1, totalSec);
        _progress.Value = Math.Clamp(totalSec - remainingSec, 0, _progress.Maximum);
    }

    /// <summary>M2-11：倒计时结束不关窗、不自动保存，只高亮“刚活动完”。</summary>
    public void OnCountdownFinished()
    {
        _btnDone.BackColor = Color.FromArgb(76, 175, 80);
        _btnDone.Text = "刚活动完 ✓";
    }

    public void FocusInput()
    {
        BeginInvoke(() => { Activate(); _txtInput.Focus(); });
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

    // PerMonitorV2 高 DPI 适配（N10/M2-15）：
    // AutoScaleMode.None 下框架不再改动窗体尺寸，Bounds 恒等于本屏逻辑边界；
    // 结构性像素尺寸（卡片宽、内边距、输入框/流水面板/进度条高）按本屏 DPI 手动等比放大，
    // 字体用"磅"为单位，本身随 DPI 物理缩放，无需处理。100%/125%/150%/200% 行为一致。
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiScale();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _screen.Bounds;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiScale(e.DeviceDpiNew);
        Bounds = _screen.Bounds;
    }

    private void ApplyDpiScale() => ApplyDpiScale(DeviceDpi);

    private void ApplyDpiScale(int dpi)
    {
        int S(int v) => (int)Math.Round(v * dpi / 96.0);
        // 高缩放小屏（如 1080p@200%，逻辑宽仅 960）下按本屏实际逻辑尺寸封顶，
        // 防止卡片越屏、底部按钮被裁掉
        var availableWidth = Math.Max(1, _screen.Bounds.Width - S(40));
        var cardWidth = Math.Min(S(760), availableWidth);
        var contentWidth = Math.Max(1, cardWidth - S(56));
        _card.MinimumSize = new Size(Math.Min(S(640), cardWidth), 0);
        _card.MaximumSize = new Size(cardWidth, Math.Max(1, _screen.Bounds.Height - S(40)));
        _card.Padding = new Padding(S(28), S(22), S(28), S(22));
        _lblTitle.MaximumSize = new Size(contentWidth, 0);
        _lblHint.MaximumSize = new Size(contentWidth, 0);
        foreach (var button in _tagButtons.Values)
        {
            button.MaximumSize = new Size(Math.Max(1, contentWidth - S(100)), 0);
            button.AutoEllipsis = true;
        }
        _txtInput.Height = S(64);
        _logPanelOuter.Height = S(LogPanelHeight);
        _progress.Height = S(14);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeTimer.Dispose();
            foreach (var f in _fonts) f.Dispose();
        }
        base.Dispose(disposing);
    }
}
