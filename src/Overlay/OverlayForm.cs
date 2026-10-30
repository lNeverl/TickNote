using SedentaryReminder.Data;

namespace SedentaryReminder.Overlay;

/// <summary>
/// 单块屏幕上的卡片层窗体（M2）。不透明卡片：提醒文案、倒计时、输入框、
/// 快捷标签（点选式）、今日流水（继续/删除，默认隐藏见小眼睛开关）、保存按钮、三按钮。
/// 卡片以外的区域用 TransparencyKey 镂空（透明且点击穿透），下层的 DimForm 提供半透明遮罩——
/// 这样背景保持半透明能看到桌面，卡片本身 100% 不透明，文字清晰。
/// 布局全部用容器（H3），禁止绝对坐标；高 DPI 走 AutoScaleMode.None + ApplyDpiScale 手动缩放。
/// </summary>
public sealed class OverlayForm : Form
{
    private const double TargetOpacity = 1.0; // 卡片层本身完全不透明；透明感由下层 DimForm 提供
    private const int LogPanelHeight = 150;

    /// <summary>镂空色：卡片以外的区域显示为该色即被 TransparencyKey 抠掉。</summary>
    private static readonly Color KeyColor = Color.Magenta;

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
    private readonly Button _btnEye;
    private readonly Label _lblToday;
    private readonly FlowLayoutPanel _tagPanel;
    private readonly TableLayoutPanel _actionRow;
    private readonly Dictionary<Control, (Padding Margin, Padding Padding)> _spacing = new();
    private int _layoutDpi = 96;
    private sealed record ButtonLayout(int Height, int MinWidth, Padding Padding, Padding Margin);
    private bool _logsVisible; // 隐私开关：每次弹窗默认隐藏今日记录，点小眼睛才显示
    private List<WorkLogEntry> _todayLogs = new();
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
        BackColor = KeyColor;           // 卡片以外的区域全部镂空
        TransparencyKey = KeyColor;
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
            Margin = Padding.Empty,
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
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = true, // 极端 DPI/小屏下内容超高时出滚动条，而不是把按钮裁没
            MinimumSize = new Size(640, 0),
            BackColor = CardBack,
            ForeColor = CardFore,
            Padding = new Padding(28, 22, 28, 22),
            ColumnCount = 1,
            RowCount = 10,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < card.RowCount; i++) card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles[3].SizeType = SizeType.Absolute;
        card.RowStyles[3].Height = 82;
        card.RowStyles[4].SizeType = SizeType.Absolute;
        card.RowStyles[4].Height = 50;
        card.RowStyles[6].SizeType = SizeType.Absolute;
        card.RowStyles[6].Height = 0;
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
        var countdownRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
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

        var lblInput = new Label { Text = "这段时间在做什么？", AutoSize = true, Margin = new Padding(0, 0, 0, 6) };

        // M3-1 随手记输入框（多行）
        _txtInput = new TextBox
        {
            Multiline = true,
            AutoSize = false,
            Height = 72,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 22, 28),
            ForeColor = CardFore,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _txtInput.Margin = new Padding(0, 0, 0, 10);
        _txtInput.TextChanged += (_, _) =>
        {
            if (!_suppressTextEvent) _manager.OnInputChanged(this, _txtInput.Text);
        };

        // 标签行：左侧点选式标签（M3-3），右侧独立的“保存”按钮
        var tagRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var tagPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = Padding.Empty,
        };
        _tagPanel = tagPanel;
        foreach (var tag in config.Tags)
        {
            var b = MakeTagButton(tag);
            b.Click += (_, _) => _manager.OnTagToggled(tag);
            tagPanel.Controls.Add(b);
            _tagButtons[tag] = b;
        }
        _btnSave = CreateButton("保存", AccentBlue, 32, 90, new Padding(12, 3, 12, 3), Padding.Empty);
        _btnSave.Font = TrackFont(new Font(Font.FontFamily, 10F, FontStyle.Bold));
        _btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _btnSave.Click += (_, _) => _manager.OnSaveRequested();
        tagRow.Controls.Add(tagPanel, 0, 0);
        tagRow.Controls.Add(_btnSave, 1, 0);
        tagRow.Margin = new Padding(0, 0, 0, 14);

        // 隐私开关（小眼睛）：默认隐藏今日记录内容，防止弹窗时被旁人看到
        var todayRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, Margin = new Padding(0, 0, 0, 8) };
        todayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        todayRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        todayRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        todayRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var lblToday = new Label
        {
            Text = "今天已记录",
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 160),
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
        };
        _lblToday = lblToday;
        _btnEye = CreateButton("显示今日", TagBack, 32, 92, new Padding(10, 3, 10, 3), Padding.Empty);
        _btnEye.Click += (_, _) => _manager.OnLogsVisibilityToggled();
        todayRow.Controls.Add(lblToday, 0, 0);
        var history = CreateButton("全部历史续记", TagBack, 32, 124, new Padding(10, 3, 10, 3), new Padding(0, 0, 8, 0));
        history.Click += (_, _) => _manager.OnHistoryRequested();
        todayRow.Controls.Add(history, 1, 0);
        var todo = CreateButton("从待办选择", TagBack, 32, 112, new Padding(10, 3, 10, 3), new Padding(0, 0, 8, 0));
        todo.Click += (_, _) => _manager.OnTodoRequested();
        todayRow.Controls.Add(todo, 2, 0);
        todayRow.Controls.Add(_btnEye, 3, 0);

        // M2-9 今日流水面板（继续 / 删除），默认折叠隐藏
        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            Height = LogPanelHeight,
            AutoScroll = true,
            BackColor = Color.FromArgb(26, 26, 32),
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 12),
            Visible = false,
        };
        _logFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        outer.Controls.Add(_logFlow);
        _logPanelOuter = outer;
        _logPanelOuter.ClientSizeChanged += (_, _) => LayoutLogRows();
        _logFlow.SizeChanged += (_, _) => LayoutLogRows();

        // 三按钮（M2-6/7/8）
        var btnRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5, RowCount = 1, Margin = Padding.Empty,
        };
        for (var i = 0; i < 5; i++) btnRow.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.Percent : SizeType.Absolute, i % 2 == 0 ? 1 : 8));
        _actionRow = btnRow;
        var actionFont = TrackFont(new Font("Microsoft YaHei UI", 10F, FontStyle.Bold));
        _btnDone = MakeActionButton("刚活动完", AccentGreen, actionFont);
        _btnDone.Click += (_, _) => _manager.OnAction("done");
        var btnSkip = MakeActionButton("跳过", Color.FromArgb(90, 90, 100), actionFont);
        btnSkip.Click += (_, _) => _manager.OnAction("skip");
        var btnPostpone = MakeActionButton($"推迟{config.PostponeMinutes}分钟", Color.FromArgb(120, 90, 30), actionFont);
        btnPostpone.Click += (_, _) => _manager.OnAction("postpone");
        foreach (var button in new[] { _btnDone, btnSkip, btnPostpone })
        {
            button.Dock = DockStyle.Fill;
            button.Tag = ((ButtonLayout)button.Tag!) with { Margin = Padding.Empty };
        }
        btnRow.Controls.Add(_btnDone, 0, 0);
        btnRow.Controls.Add(btnSkip, 2, 0);
        btnRow.Controls.Add(btnPostpone, 4, 0);

        var access = MakeRowButton("去其他应用复制 / 查看");
        access.Tag = ((ButtonLayout)access.Tag!) with { Margin = new Padding(0, 0, 0, 12) };
        access.Click += (_, _) => _manager.OnAccessRequested();

        // 避免误解：倒计时只是建议，三个按钮都立即关窗，不用等
        var lblHint = new Label
        {
            Text = "倒计时仅作建议；活动过后可直接点击「刚活动完」。",
            AutoSize = true,
            ForeColor = Color.FromArgb(140, 140, 150),
            Margin = new Padding(0, 10, 0, 0),
        };
        _lblHint = lblHint;

        var cardRows = new Control[] { lblTitle, countdownRow, lblInput, _txtInput, tagRow, todayRow, outer, access, btnRow, lblHint };
        for (var i = 0; i < cardRows.Length; i++) card.Controls.Add(cardRows[i], 0, i);

        center.Controls.Add(card, 1, 1);
        Controls.Add(center);

        foreach (var control in Descendants(card).Where(control => control is not Button))
            _spacing[control] = (control.Margin, control.Padding);
        _tagPanel.SizeChanged += (_, _) => UpdateTagRowHeight(Math.Max(1, _card.ClientSize.Width - _card.Padding.Horizontal));

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
        _todayLogs = logs;
        _lblToday.Text = $"今天已记录 · {logs.Count} 条";
        ClearLogRows();
        if (!_logsVisible) return; // 隐私模式：隐藏时不渲染任何内容
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
            var row = new TableLayoutPanel
            {
                AutoSize = false, Height = S(32), ColumnCount = 5, RowCount = 1,
                Margin = new Padding(0, 0, 0, S(6)),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 4; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var text = log.EndTime ?? log.StartTime ?? "--:--";
            if (log.Tag is { Length: > 0 }) text += $" [{log.Tag}]";
            text += " " + Truncate(log.Content ?? "", 24);
            row.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill, AutoEllipsis = true, Height = S(32),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = CardFore,
                Margin = new Padding(0, 0, S(8), 0),
            }, 0, 0);
            var captured = log;
            var btnContinue = MakeRowButton("继续");
            btnContinue.Click += (_, _) => _manager.OnContinueRequested(captured);
            var btnDelete = MakeRowButton("删除");
            btnDelete.ForeColor = Color.FromArgb(240, 160, 160);
            btnDelete.Click += (_, _) => _manager.OnDeleteRequested(captured);
            row.Controls.Add(btnContinue, 1, 0);
            var edit = MakeRowButton("编辑");
            edit.Click += (_, _) => _manager.OnEditRequested(captured);
            var copy = MakeRowButton("复制");
            copy.Click += (_, _) => _manager.OnCopyRequested(captured, this);
            btnDelete.Tag = ((ButtonLayout)btnDelete.Tag!) with { Margin = Padding.Empty };
            ApplyButtonLayout(btnDelete);
            row.Controls.Add(edit, 2, 0);
            row.Controls.Add(copy, 3, 0);
            row.Controls.Add(btnDelete, 4, 0);
            _logFlow.Controls.Add(row);
        }
        LayoutLogRows();
    }

    /// <summary>小眼睛开关：显示/隐藏今日流水（由管理器同步到所有屏）。</summary>
    public void SetLogsVisible(bool visible)
    {
        _logsVisible = visible;
        _btnEye.Text = visible ? "隐藏今日" : "显示今日";
        _logPanelOuter.Visible = visible;
        _card.RowStyles[6].Height = visible ? S(LogPanelHeight + 12) : 0;
        if (visible) RefreshLogs(_todayLogs);
        else ClearLogRows();
    }

    private void ClearLogRows()
    {
        // Controls.Clear 不会释放旧行及其事件闭包，持续刷新时必须显式 Dispose。
        foreach (var row in _logFlow.Controls.Cast<Control>().ToArray()) row.Dispose();
    }

    private Button MakeRowButton(string text) => CreateButton(text, TagBack, 32, 54, new Padding(10, 3, 10, 3), new Padding(0, 0, 6, 0));

    private Button CreateButton(string text, Color back, int height, int minWidth, Padding padding, Padding margin)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = CardFore,
            Anchor = AnchorStyles.Left, Tag = new ButtonLayout(height, minWidth, padding, margin),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(82, 82, 98);
        ApplyButtonLayout(button);
        return button;
    }

    private int S(int value) => (int)Math.Round(value * _layoutDpi / 96.0);
    private Padding ScalePadding(Padding padding) => new(S(padding.Left), S(padding.Top), S(padding.Right), S(padding.Bottom));
    private void ApplyButtonLayout(Button button)
    {
        if (button.Tag is not ButtonLayout layout) return;
        button.MinimumSize = new Size(S(layout.MinWidth), S(layout.Height));
        button.Padding = ScalePadding(layout.Padding);
        button.Margin = ScalePadding(layout.Margin);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private void LayoutLogRows()
    {
        var width = Math.Max(1, _logPanelOuter.ClientSize.Width - _logPanelOuter.Padding.Horizontal - S(20));
        foreach (var row in _logFlow.Controls.OfType<TableLayoutPanel>())
        {
            row.Margin = new Padding(0, 0, 0, S(6));
            foreach (var label in row.Controls.OfType<Label>()) label.Margin = new Padding(0, 0, S(8), 0);
            var height = row.Controls.OfType<Button>().Select(button => button.GetPreferredSize(Size.Empty).Height).DefaultIfEmpty(S(32)).Max();
            row.Size = new Size(width, height);
        }
    }

    private static string Truncate(string s, int max)
    {
        var flat = s.Replace("\r", " ").Replace("\n", " ");
        return flat.Length > max ? flat[..max] + "…" : flat;
    }

    private Button MakeTagButton(string text) => CreateButton(text, TagBack, 30, 0, new Padding(8, 3, 8, 3), new Padding(0, 0, 6, 6));

    private Button MakeActionButton(string text, Color back, Font font)
    {
        var button = CreateButton(text, back, 38, 100, new Padding(16, 4, 16, 4), new Padding(0, 0, 8, 0));
        button.Font = font;
        return button;
    }

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
        UpdateTagRowHeight(Math.Max(1, _card.ClientSize.Width - _card.Padding.Horizontal));
    }

    private void UpdateTagRowHeight(int contentWidth)
    {
        var tagWidth = Math.Max(1, contentWidth - _btnSave.GetPreferredSize(Size.Empty).Width - S(12));
        _tagPanel.MaximumSize = new Size(tagWidth, 0);
        var height = Math.Max(_tagPanel.GetPreferredSize(new Size(tagWidth, 0)).Height, _btnSave.GetPreferredSize(Size.Empty).Height);
        _card.RowStyles[4].Height = height + S(14);
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
        _layoutDpi = dpi;
        _card.SuspendLayout();
        foreach (var (control, spacing) in _spacing)
        {
            control.Margin = ScalePadding(spacing.Margin);
            control.Padding = ScalePadding(spacing.Padding);
        }
        foreach (var button in Descendants(_card).OfType<Button>()) ApplyButtonLayout(button);
        int S(int v) => (int)Math.Round(v * dpi / 96.0);
        // 高缩放小屏（如 1080p@200%，逻辑宽仅 960）下按本屏实际逻辑尺寸封顶，
        // 防止卡片越屏、底部按钮被裁掉
        var availableWidth = Math.Max(1, _screen.Bounds.Width - S(40));
        var cardWidth = Math.Min(S(720), availableWidth);
        var contentWidth = Math.Max(1, cardWidth - S(56));
        _card.MinimumSize = new Size(cardWidth, 0);
        _card.MaximumSize = new Size(cardWidth, Math.Max(1, _screen.Bounds.Height - S(40)));
        _card.Padding = new Padding(S(28), S(22), S(28), S(22));
        _lblTitle.MaximumSize = new Size(contentWidth, 0);
        _lblHint.MaximumSize = new Size(contentWidth, 0);
        UpdateTagRowHeight(contentWidth);
        foreach (var button in _tagButtons.Values)
        {
            button.MaximumSize = new Size(Math.Max(1, contentWidth - S(100)), 0);
            button.AutoEllipsis = true;
        }
        _card.RowStyles[3].Height = S(82);
        _actionRow.ColumnStyles[1].Width = _actionRow.ColumnStyles[3].Width = S(8);
        _card.RowStyles[6].Height = _logsVisible ? S(LogPanelHeight + 12) : 0;
        _txtInput.Height = S(72);
        _logPanelOuter.Height = S(LogPanelHeight);
        _progress.Height = S(14);
        _card.ResumeLayout(true);
        LayoutLogRows();
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
