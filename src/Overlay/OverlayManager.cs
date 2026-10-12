using SedentaryReminder.Data;
using SedentaryReminder.Log;

namespace SedentaryReminder.Overlay;

/// <summary>
/// 全屏提醒弹窗管理器（M2）：每屏一对窗口——DimForm 半透明背景层 + OverlayForm 不透明卡片层，
/// 共享同一份状态（输入文本/选中标签/倒计时/今日流水/小眼睛开关），
/// 任一屏操作实时同步到其他屏（M2-2），关闭即全部关闭（M2-1）。
/// 保存交互：独立“保存”按钮（3 秒冷却，可连续存多条）+ 关闭按钮兜底自动保存 + 流水可删除。
/// </summary>
public sealed class OverlayManager
{
    private const int SaveCooldownMs = 3000;
    private const int EmptyHintMs = 1500;

    private readonly AppConfig _config;
    private readonly Database _db;
    private readonly List<OverlayForm> _forms = new();
    private readonly List<DimForm> _dimForms = new();
    private System.Windows.Forms.Timer? _countdownTimer;
    private System.Windows.Forms.Timer? _saveCooldownTimer;
    private System.Windows.Forms.Timer? _saveFlashTimer;

    private DateTime _cycleStart;
    private string _inputText = "";
    private string? _selectedTag;
    private long? _continueFromId;
    private int _countdownTotalSec;
    private int _countdownRemainingSec;
    private IntPtr _prevForeground;
    private bool _logsVisible; // 小眼睛状态，所有屏一致
    private ReminderAccessForm? _accessForm;
    private Form? _auxiliaryForm;
    private string? _continuedContent;
    private string? _continuedTag;

    public bool IsTemporarilyReleased => _accessForm != null || _auxiliaryForm != null;

    public bool IsOpen { get; private set; }

    /// <summary>本次关闭时是否自动保存了一条流水（供托盘气泡反馈）。</summary>
    public bool LastCloseSavedLog { get; private set; }

    /// <summary>全部弹窗关闭后触发。参数：done / skip / postpone。</summary>
    public event Action<string>? Closed;

    public OverlayManager(AppConfig config, Database db)
    {
        _config = config;
        _db = db;
    }

    // ---------------- 显示 ----------------

    public void Show(DateTime cycleStart)
    {
        if (IsOpen) return;
        IsOpen = true;
        LastCloseSavedLog = false;
        _cycleStart = cycleStart;
        _inputText = "";
        _selectedTag = null;
        _continueFromId = null;
        _continuedContent = null;
        _continuedTag = null;
        _logsVisible = false; // 每次弹窗默认隐藏今日记录（隐私）
        _countdownTotalSec = Math.Max(1, _config.BreakMinutes) * 60;
        _countdownRemainingSec = _countdownTotalSec;
        _prevForeground = NativeMethods.GetForegroundWindow(); // N5：关窗后恢复焦点
        _db.WorkLogsChanged += OnWorkLogsChanged;

        CreateForms();
        StartCountdown();
    }

    private void CreateForms()
    {
        var logs = TodayLogs();
        foreach (var screen in Screen.AllScreens)
        {
            // 先铺半透明背景层，再放不透明卡片层（z 序：卡片在上）
            var dim = new DimForm(screen);
            if (!IsTemporarilyReleased) dim.Show();
            _dimForms.Add(dim);

            var form = new OverlayForm(this, _config, screen, logs);
            _forms.Add(form);
            dim.Click += (_, _) => form.Activate(); // 点到背景时把焦点还给卡片层（ESC 才能用）
            if (!IsTemporarilyReleased) form.Show();
            form.SetInputText(_inputText);       // 热插拔重建时保留状态（N4）
            form.SetSelectedTag(_selectedTag);
            form.SetLogsVisible(_logsVisible);
        }
        // M2-12：主屏弹窗输入框获得焦点
        var primary = _forms.FirstOrDefault(f => f.IsPrimary);
        if (!IsTemporarilyReleased)
        {
            primary?.Activate();
            primary?.FocusInput();
        }
    }

    private List<WorkLogEntry> TodayLogs() => _db.GetLogs(DateTime.Today.ToString("yyyy-MM-dd"));

    private void StartCountdown()
    {
        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (_, _) =>
        {
            _countdownRemainingSec--;
            if (_countdownRemainingSec <= 0)
            {
                _countdownRemainingSec = 0;
                _countdownTimer.Stop();
                // M2-11：倒计时走完不关窗、不自动保存，只高亮"刚活动完"
                foreach (var f in _forms) f.OnCountdownFinished();
            }
            UpdateCountdownOnForms();
        };
        _countdownTimer.Start();
        UpdateCountdownOnForms();
    }

    private void UpdateCountdownOnForms()
    {
        foreach (var f in _forms)
        {
            f.UpdateCountdown(_countdownRemainingSec, _countdownTotalSec);
            if (_countdownRemainingSec == 0) f.OnCountdownFinished();
        }
    }

    // ---------------- 状态同步（供 OverlayForm 回调） ----------------

    internal void OnInputChanged(OverlayForm source, string text)
    {
        _inputText = text;
        foreach (var f in _forms)
            if (!ReferenceEquals(f, source)) f.SetInputText(text);
    }

    /// <summary>M3-3 点选式标签：再点一次取消；单选，同步到所有屏。</summary>
    internal void OnTagToggled(string tag)
    {
        _selectedTag = _selectedTag == tag ? null : tag;
        foreach (var f in _forms) f.SetSelectedTag(_selectedTag);
    }

    /// <summary>M2-10 一键续记：内容填入输入框、标签同步选中，保存时标记为续记。</summary>
    internal void OnContinueRequested(WorkLogEntry entry)
    {
        _continueFromId = entry.Id;
        _continuedContent = entry.Content;
        _continuedTag = entry.Tag;
        _selectedTag = entry.Tag;
        _inputText = entry.Content ?? "";
        foreach (var f in _forms)
        {
            f.SetInputText(_inputText);
            f.SetSelectedTag(_selectedTag);
        }
        _accessForm?.SetDraft(_inputText);
        if (!IsTemporarilyReleased) _forms.FirstOrDefault(f => f.IsPrimary)?.FocusInput();
    }

    internal void OnHistoryRequested()
    {
        if (_auxiliaryForm != null) { _auxiliaryForm.Activate(); return; }
        using var picker = new WorkLogPickerForm(_db);
        RunAuxiliaryDialog(picker, () =>
        {
            if (picker.SelectedEntry != null) OnContinueRequested(picker.SelectedEntry);
        });
    }

    internal void OnEditRequested(WorkLogEntry entry)
    {
        if (_auxiliaryForm != null) { _auxiliaryForm.Activate(); return; }
        using var editor = new ManualEntryForm(_db, _config, entry);
        RunAuxiliaryDialog(editor, () => { });
    }

    private void RunAuxiliaryDialog(Form dialog, Action accepted)
    {
        if (!IsOpen) return;
        var restore = _accessForm == null;
        _auxiliaryForm = dialog;
        HideFullScreen();
        try
        {
            var result = _accessForm == null ? dialog.ShowDialog() : dialog.ShowDialog(_accessForm);
            if (result == DialogResult.OK && IsOpen) accepted();
        }
        finally
        {
            _auxiliaryForm = null;
            if (restore && IsOpen) RestoreFullScreen();
        }
    }

    internal void OnCopyRequested(WorkLogEntry entry, Form owner)
    {
        if (string.IsNullOrEmpty(entry.Content)) return;
        try { Clipboard.SetText(entry.Content); }
        catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(owner, "剪贴板正被占用，请稍后重试。", "复制失败"); }
    }

    internal void OnAccessRequested()
    {
        if (!IsOpen) return;
        if (_accessForm != null) { _accessForm.Activate(); return; }
        HideFullScreen();
        var access = new ReminderAccessForm(_inputText, OnHistoryRequested);
        _accessForm = access;
        access.FormClosed += (_, _) =>
        {
            if (!ReferenceEquals(_accessForm, access)) return;
            _accessForm = null;
            if (IsOpen) RestoreFullScreen();
        };
        access.Show();
    }

    private void HideFullScreen()
    {
        foreach (var form in _forms) form.Hide();
        foreach (var dim in _dimForms) dim.Hide();
    }

    private void RestoreFullScreen()
    {
        foreach (var dim in _dimForms) dim.Show();
        foreach (var form in _forms) form.Show();
        UpdateCountdownOnForms();
        _forms.FirstOrDefault(form => form.IsPrimary)?.FocusInput();
    }

    private void OnWorkLogsChanged()
    {
        if (_continueFromId is long id)
        {
            var latest = _db.GetWorkLog(id);
            if (latest == null) _continueFromId = null;
            else
            {
                // 只更新仍与所选事项相同的草稿，保留用户已经自行改写的内容。
                if (_inputText == _continuedContent) _inputText = latest.Content ?? "";
                if (_selectedTag == _continuedTag) _selectedTag = latest.Tag;
                _continuedContent = latest.Content;
                _continuedTag = latest.Tag;
                foreach (var form in _forms)
                {
                    form.SetInputText(_inputText);
                    form.SetSelectedTag(_selectedTag);
                }
                _accessForm?.SetDraft(_inputText);
            }
        }
        RefreshLogsAll();
    }

    /// <summary>小眼睛：显示/隐藏今日流水，任一屏点击所有屏同步。</summary>
    internal void OnLogsVisibilityToggled()
    {
        _logsVisible = !_logsVisible;
        foreach (var f in _forms) f.SetLogsVisible(_logsVisible);
    }

    // ---------------- 保存 / 删除 ----------------

    /// <summary>“保存”按钮：立即存一条；成功则清空输入、刷新列表并冷却 3 秒（可连续存多条）。</summary>
    internal void OnSaveRequested()
    {
        if (!TrySaveInput())
        {
            FlashSaveHint();
            return;
        }
        _inputText = "";
        _continueFromId = null;
        _selectedTag = null;
        foreach (var f in _forms)
        {
            f.SetInputText("");
            f.SetSelectedTag(null);
        }
        RefreshLogsAll();
        StartSaveCooldown();
    }

    /// <summary>保存错了当场删：任一屏删除，所有屏列表同步刷新。</summary>
    internal void OnDeleteRequested(WorkLogEntry entry)
    {
        _db.DeleteWorkLog(entry.Id);
        RefreshLogsAll();
    }

    private void RefreshLogsAll()
    {
        var logs = TodayLogs();
        foreach (var f in _forms) f.RefreshLogs(logs);
    }

    /// <summary>把当前输入存为一条流水（M3-1 即写即存）；空输入返回 false（M3-5 空记录允许）。</summary>
    private bool TrySaveInput()
    {
        var text = _inputText.Trim();
        if (text.Length == 0) return false;

        _db.InsertWorkLog(new WorkLogEntry
        {
            LogDate = DateTime.Today.ToString("yyyy-MM-dd"),
            StartTime = _cycleStart.ToString("HH:mm"),
            EndTime = DateTime.Now.ToString("HH:mm"),
            Tag = _selectedTag,
            Content = text,
            Source = _continueFromId != null ? "continue" : "popup", // M3-4 续记生成新条目
            ContinueFrom = _continueFromId,
        });
        return true;
    }

    private void StartSaveCooldown()
    {
        _saveFlashTimer?.Stop();
        foreach (var f in _forms) f.SetSaveButton(false, "已保存 ✓");
        _saveCooldownTimer?.Stop();
        _saveCooldownTimer?.Dispose();
        _saveCooldownTimer = new System.Windows.Forms.Timer { Interval = SaveCooldownMs };
        _saveCooldownTimer.Tick += (_, _) =>
        {
            _saveCooldownTimer.Stop();
            foreach (var f in _forms) f.SetSaveButton(true, "保存");
        };
        _saveCooldownTimer.Start();
    }

    private void FlashSaveHint()
    {
        foreach (var f in _forms) f.SetSaveButton(true, "先输入内容");
        _saveFlashTimer?.Stop();
        _saveFlashTimer?.Dispose();
        _saveFlashTimer = new System.Windows.Forms.Timer { Interval = EmptyHintMs };
        _saveFlashTimer.Tick += (_, _) =>
        {
            _saveFlashTimer.Stop();
            foreach (var f in _forms) f.SetSaveButton(true, "保存");
        };
        _saveFlashTimer.Start();
    }

    // ---------------- 关闭 ----------------

    internal void OnAction(string action)
    {
        if (!IsOpen) return;
        LastCloseSavedLog = TrySaveInput(); // 兜底：关闭时未保存的文字自动入库，不丢字
        _db.InsertReminderEvent(action, _config.BreakMinutes); // M3-7 提醒事件入库
        Cleanup();
        Closed?.Invoke(action);
    }

    /// <summary>显示器热插拔（N4）：弹窗打开时按当前屏幕集合重建，状态保留。</summary>
    internal void OnDisplaySettingsChanged()
    {
        if (!IsOpen) return;
        foreach (var f in _forms) { f.Hide(); f.Dispose(); }
        _forms.Clear();
        foreach (var d in _dimForms) { d.Hide(); d.Dispose(); }
        _dimForms.Clear();
        CreateForms();
        UpdateCountdownOnForms();
    }

    private void Cleanup()
    {
        IsOpen = false;
        _db.WorkLogsChanged -= OnWorkLogsChanged;
        var access = _accessForm;
        _accessForm = null;
        access?.Dispose();
        _auxiliaryForm?.Close();
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;
        _saveCooldownTimer?.Stop();
        _saveCooldownTimer?.Dispose();
        _saveCooldownTimer = null;
        _saveFlashTimer?.Stop();
        _saveFlashTimer?.Dispose();
        _saveFlashTimer = null;
        foreach (var f in _forms) { f.Hide(); f.Dispose(); }
        _forms.Clear();
        foreach (var d in _dimForms) { d.Hide(); d.Dispose(); }
        _dimForms.Clear();

        // N5 / M2-12：恢复原窗口键盘焦点
        if (_prevForeground != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(_prevForeground);
    }

    /// <summary>
    /// 半透明背景层：铺满整屏，负责遮罩和吃掉背景点击，不抢焦点。
    /// 卡片层（OverlayForm）用 TransparencyKey 镂空背景区域叠在它上面，
    /// 效果 = 背景半透明能看到桌面，卡片 100% 不透明文字清晰。
    /// </summary>
    private sealed class DimForm : Form
    {
        private const double DimOpacity = 0.60; // 比整窗 0.85 略透，桌面内容隐约可见

        private readonly Screen _screen;

        public DimForm(Screen screen)
        {
            _screen = screen;
            AutoScaleMode = AutoScaleMode.None; // 与 OverlayForm 同理：Bounds 手动精确等于本屏，防框架二次缩放越屏
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screen.Bounds;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = DimOpacity;
        }

        protected override bool ShowWithoutActivation => true; // 不抢卡片层输入框的焦点

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _screen.Bounds;
        }
    }
}
