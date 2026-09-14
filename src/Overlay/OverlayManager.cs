using SedentaryReminder.Data;

namespace SedentaryReminder.Overlay;

/// <summary>
/// 全屏提醒弹窗管理器（M2）：每屏一个 OverlayForm，共享同一份状态（输入文本/选中标签/倒计时/今日流水），
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
        _countdownTotalSec = Math.Max(1, _config.BreakMinutes) * 60;
        _countdownRemainingSec = _countdownTotalSec;
        _prevForeground = NativeMethods.GetForegroundWindow(); // N5：关窗后恢复焦点

        CreateForms();
        StartCountdown();
    }

    private void CreateForms()
    {
        var logs = TodayLogs();
        foreach (var screen in Screen.AllScreens)
        {
            var form = new OverlayForm(this, _config, screen, logs);
            _forms.Add(form);
            form.Show();
            form.SetInputText(_inputText);       // 热插拔重建时保留状态（N4）
            form.SetSelectedTag(_selectedTag);
        }
        // M2-12：主屏弹窗输入框获得焦点
        var primary = _forms.FirstOrDefault(f => f.IsPrimary);
        primary?.Activate();
        primary?.FocusInput();
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
            f.UpdateCountdown(_countdownRemainingSec, _countdownTotalSec);
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
        _selectedTag = entry.Tag;
        _inputText = entry.Content ?? "";
        foreach (var f in _forms)
        {
            f.SetInputText(_inputText);
            f.SetSelectedTag(_selectedTag);
        }
        _forms.FirstOrDefault(f => f.IsPrimary)?.FocusInput();
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
        CreateForms();
        UpdateCountdownOnForms();
    }

    private void Cleanup()
    {
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
        IsOpen = false;

        // N5 / M2-12：恢复原窗口键盘焦点
        if (_prevForeground != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(_prevForeground);
    }
}
