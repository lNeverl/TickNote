using SedentaryReminder.Data;

namespace SedentaryReminder.Reminder;

/// <summary>托盘图标状态（M5-3）。</summary>
public enum EngineTrayState
{
    /// <summary>计时中（绿）。</summary>
    Counting,
    /// <summary>距弹窗 &lt; 5 分钟（黄）。</summary>
    Soon,
    /// <summary>已推迟 / 已暂停 / 非工作时段等待（灰）。</summary>
    Idle,
}

/// <summary>
/// 定时提醒引擎（M1）：1 秒一拍的状态机。
/// 周期计时 → 到点弹窗；空闲/锁屏/午休/非工作时段暂停；推迟不限次数；演示勿扰自动延后。
/// </summary>
public sealed class ReminderEngine
{
    private readonly AppConfig _config;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly DateTime _launchedAt = DateTime.Now;

    private double _remainingSec;
    private DateTime _cycleStart = DateTime.Now;
    private bool _locked;
    private bool _idlePaused;
    private DateTime? _postponeAt;
    private DateTime? _userPauseUntil;
    private bool _pendingFire; // 勿扰期间已到点，等全屏结束后立即补弹

    /// <summary>弹窗当前是否打开（由 OverlayManager 通知）。</summary>
    public bool OverlayOpen { get; private set; }

    /// <summary>到点弹窗。参数：本周期开始时间（用于流水 start_time）。</summary>
    public event Action<DateTime>? FireRequested;

    /// <summary>状态（图标/悬停文字）发生变化。</summary>
    public event Action? StatusChanged;

    public ReminderEngine(AppConfig config)
    {
        _config = config;
        _remainingSec = config.IntervalMinutes * 60.0;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    // ---------------- 外部事件 ----------------

    public void SetLocked(bool locked)
    {
        _locked = locked;
        RaiseStatus();
    }

    public void NotifyOverlayOpened() => OverlayOpen = true;

    /// <summary>弹窗关闭：done/skip → 清零开始下一周期；postpone → 推迟后再弹（M1-7 不限次数）。</summary>
    public void NotifyOverlayClosed(string action)
    {
        OverlayOpen = false;
        _pendingFire = false;
        if (action == "postpone")
        {
            _postponeAt = DateTime.Now.AddMinutes(_config.PostponeMinutes);
        }
        else
        {
            _postponeAt = null;
            ResetCycle();
        }
        RaiseStatus();
    }

    /// <summary>暂停提醒（M5-6）：到点自动恢复并开始新周期。</summary>
    public void PauseUntil(DateTime until)
    {
        _userPauseUntil = until;
        RaiseStatus();
    }

    public void PauseRestOfToday()
    {
        var end = ParseTime(_config.WorkEnd, new TimeSpan(23, 59, 59));
        var today = DateTime.Today + end;
        if (today <= DateTime.Now) today = DateTime.Today.AddDays(1) + end;
        PauseUntil(today);
    }

    public void ResumeFromPause()
    {
        _userPauseUntil = null;
        _postponeAt = null;
        ResetCycle();
        RaiseStatus();
    }

    public bool IsPaused => _userPauseUntil != null;

    public void ReloadConfig()
    {
        // 周期缩短到剩余时间以下时立即生效新周期
        if (_postponeAt == null && _remainingSec > _config.IntervalMinutes * 60.0)
            _remainingSec = _config.IntervalMinutes * 60.0;
        RaiseStatus();
    }

    // ---------------- 状态查询（托盘用） ----------------

    public (EngineTrayState State, string Text) GetStatus()
    {
        if (OverlayOpen) return (EngineTrayState.Idle, "提醒弹窗显示中");
        if (_userPauseUntil is DateTime pu)
            return (EngineTrayState.Idle, $"已暂停提醒，{pu:HH:mm} 自动恢复");
        if (_postponeAt is DateTime pa)
            return (EngineTrayState.Idle, $"已推迟，{pa:HH:mm} 再次提醒");
        if (_locked) return (EngineTrayState.Idle, "已锁屏，计时暂停");
        if (_idlePaused) return (EngineTrayState.Idle, "检测到离开，计时暂停");

        var now = DateTime.Now;
        if (!InWorkHours(now)) return (EngineTrayState.Idle, "非工作时段，不提醒");
        if (InLunch(now)) return (EngineTrayState.Idle, "午休免打扰中");
        var delayEnd = _launchedAt.AddMinutes(_config.StartupDelayMinutes);
        if (now < delayEnd)
            return (EngineTrayState.Idle, $"启动缓冲中，{delayEnd:HH:mm} 开始计时");

        var min = (int)Math.Ceiling(_remainingSec / 60.0);
        return min < 5
            ? (EngineTrayState.Soon, $"距下次提醒约 {min} 分钟")
            : (EngineTrayState.Counting, $"距下次提醒约 {min} 分钟");
    }

    // ---------------- 计时主循环 ----------------

    private void OnTick(object? sender, EventArgs e)
    {
        if (OverlayOpen) return; // 弹窗期间由弹窗自己倒计时

        var now = DateTime.Now;

        // 用户主动暂停（M5-6）
        if (_userPauseUntil is DateTime pu)
        {
            if (now >= pu)
            {
                _userPauseUntil = null;
                ResetCycle();
                RaiseStatus();
            }
            return;
        }

        // 锁屏暂停（M1-6）：唤醒后继续剩余时间
        if (_locked) return;

        // 空闲检测（M1-5）
        if (_config.IdleDetectionEnabled)
        {
            bool idle = NativeMethods.GetIdleSeconds() > _config.IdleThresholdMinutes * 60;
            if (idle)
            {
                if (!_idlePaused) { _idlePaused = true; RaiseStatus(); }
                return;
            }
            if (_idlePaused)
            {
                _idlePaused = false;
                if (_config.IdleRestart) ResetCycle(); // 配置：回来后重新计时 / 继续剩余时间
                RaiseStatus();
            }
        }

        // 非工作时段（M1-3）：不计时，回到工作时段后重新开始整个周期
        if (!InWorkHours(now))
        {
            if (_remainingSec != _config.IntervalMinutes * 60.0) ResetCycle();
            return;
        }

        // 午休免打扰（M1-4）：冻结剩余时间，午休后继续
        if (InLunch(now)) return;

        // 首次启动延迟（M1-8）
        if (now < _launchedAt.AddMinutes(_config.StartupDelayMinutes)) return;

        // 推迟中（M1-7）：到点后再判断是否能弹
        if (_postponeAt is DateTime pa)
        {
            if (now < pa) return;
            _postponeAt = null;
            TryFire();
            return;
        }

        _remainingSec -= 1.0;
        if (_remainingSec <= 0) TryFire();
        else if ((int)_remainingSec % 30 == 0) RaiseStatus(); // 半分钟刷一次悬停文字即可
    }

    private void TryFire()
    {
        // M1-9 演示/会议勿扰：全屏应用期间自动延后，结束后立即补弹
        if (_config.FullscreenDndEnabled && NativeMethods.IsFullscreenAppInForeground())
        {
            if (!_pendingFire) { _pendingFire = true; RaiseStatus(); }
            _remainingSec = 5; // 每 5 秒复查一次全屏是否结束
            return;
        }
        _pendingFire = false;
        FireRequested?.Invoke(_cycleStart);
    }

    private void ResetCycle()
    {
        _remainingSec = _config.IntervalMinutes * 60.0;
        _cycleStart = DateTime.Now;
    }

    private void RaiseStatus() => StatusChanged?.Invoke();

    // ---------------- 时段判断 ----------------

    private static TimeSpan ParseTime(string hhmm, TimeSpan fallback)
        => TimeSpan.TryParse(hhmm, out var t) ? t : fallback;

    private bool InWorkHours(DateTime now)
    {
        var start = ParseTime(_config.WorkStart, new TimeSpan(8, 30, 0));
        var end = ParseTime(_config.WorkEnd, new TimeSpan(18, 30, 0));
        if (end <= start) return true; // 配置异常时不锁死，按全天工作处理
        var t = now.TimeOfDay;
        return t >= start && t < end;
    }

    private bool InLunch(DateTime now)
    {
        if (!_config.LunchEnabled) return false;
        var start = ParseTime(_config.LunchStart, new TimeSpan(12, 0, 0));
        var end = ParseTime(_config.LunchEnd, new TimeSpan(13, 30, 0));
        if (end <= start) return false;
        var t = now.TimeOfDay;
        return t >= start && t < end;
    }
}
