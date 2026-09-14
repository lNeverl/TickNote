using SedentaryReminder.Data;

namespace SedentaryReminder.Overlay;

/// <summary>
/// 全屏提醒弹窗管理器（M2）：每屏一个 OverlayForm，共享同一份状态（输入文本/倒计时/今日流水），
/// 任一屏操作实时同步到其他屏（M2-2），关闭即全部关闭（M2-1）。
/// </summary>
public sealed class OverlayManager
{
    private const double TargetOpacity = 0.85; // M2-3 深色遮罩不透明度

    private readonly AppConfig _config;
    private readonly Database _db;
    private readonly List<OverlayForm> _forms = new();
    private System.Windows.Forms.Timer? _countdownTimer;

    private DateTime _cycleStart;
    private string _inputText = "";
    private long? _continueFromId;
    private int _countdownTotalSec;
    private int _countdownRemainingSec;
    private IntPtr _prevForeground;

    public bool IsOpen { get; private set; }

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
        _cycleStart = cycleStart;
        _inputText = "";
        _continueFromId = null;
        _countdownTotalSec = Math.Max(1, _config.BreakMinutes) * 60;
        _countdownRemainingSec = _countdownTotalSec;
        _prevForeground = NativeMethods.GetForegroundWindow(); // N5：关窗后恢复焦点

        CreateForms();
        StartCountdown();
    }

    private void CreateForms()
    {
        var logs = _db.GetLogs(DateTime.Today.ToString("yyyy-MM-dd"));
        foreach (var screen in Screen.AllScreens)
        {
            var form = new OverlayForm(this, _config, screen, logs);
            _forms.Add(form);
            form.Show();
        }
        // M2-12：主屏弹窗输入框获得焦点
        var primary = _forms.FirstOrDefault(f => f.IsPrimary);
        primary?.Activate();
        primary?.FocusInput();
    }

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
                // M2-11：倒计时走完不关窗，高亮"刚活动完"
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

    /// <summary>M2-10 一键续记：把被点击条目的内容填入所有屏幕的输入框。</summary>
    internal void OnContinueRequested(WorkLogEntry entry)
    {
        _continueFromId = entry.Id;
        var text = entry.Tag is { Length: > 0 } tag ? $"[{tag}] {entry.Content}" : entry.Content ?? "";
        _inputText = text;
        foreach (var f in _forms) f.SetInputText(text);
        _forms.FirstOrDefault(f => f.IsPrimary)?.FocusInput();
    }

    // ---------------- 关闭 ----------------

    internal void OnAction(string action)
    {
        if (!IsOpen) return;
        SaveWorkLogIfAny();
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

    /// <summary>M3-1/M3-5：输入非空即存一条流水；空记录允许，只记提醒事件。</summary>
    private void SaveWorkLogIfAny()
    {
        var text = _inputText.Trim();
        if (text.Length == 0) return;

        string? tag = null;
        var content = text;
        // 输入以 [标签] 开头时拆出标签列，便于统计与提示词格式化
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']');
            if (end > 1)
            {
                tag = text[1..end].Trim();
                content = text[(end + 1)..].Trim();
            }
        }

        _db.InsertWorkLog(new WorkLogEntry
        {
            LogDate = DateTime.Today.ToString("yyyy-MM-dd"),
            StartTime = _cycleStart.ToString("HH:mm"),
            EndTime = DateTime.Now.ToString("HH:mm"),
            Tag = tag,
            Content = content,
            Source = _continueFromId != null ? "continue" : "popup", // M3-4 续记生成新条目
            ContinueFrom = _continueFromId,
        });
    }

    private void Cleanup()
    {
        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;
        foreach (var f in _forms) { f.Hide(); f.Dispose(); }
        _forms.Clear();
        IsOpen = false;

        // N5 / M2-12：恢复原窗口键盘焦点
        if (_prevForeground != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(_prevForeground);
    }
}
