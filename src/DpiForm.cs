namespace SedentaryReminder;

/// <summary>
/// 高 DPI 窗体基类。不依赖框架自动缩放——在"系统 DPI 与显示器 DPI 不一致"等场景下
/// （如缩放后未重新登录、远程/虚拟显示器），框架的初始缩放会静默失效导致布局被裁。
/// 改为：句柄创建后按窗口实际 DeviceDpi 手动等比缩放一次，DPI 变化时按比例再缩放。
/// 字体使用磅值，在新的设备 DPI 下自然按物理尺寸渲染；控件边界由 Scale 等比调整。
/// </summary>
public abstract class DpiForm : Form
{
    private int _appliedDpi = 96;
    private bool _initialPositionApplied;

    private static Icon? _appIcon;

    /// <summary>嵌入资源的应用图标（Assets/TickNote.ico），所有窗口共用同一个实例。</summary>
    internal static Icon AppIcon => _appIcon ??= LoadAppIcon();

    private static Icon LoadAppIcon()
    {
        var s = typeof(DpiForm).Assembly.GetManifestResourceStream("SedentaryReminder.Assets.TickNote.ico");
        return s != null ? new Icon(s) : SystemIcons.Application;
    }

    protected DpiForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Icon = AppIcon; // 标题栏 / Alt+Tab / 任务栏统一用软件图标
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpi(DeviceDpi, scaleForm: true);
        // CenterScreen 的居中在句柄创建时按缩放前的小尺寸计算，ApplyDpi 放大窗口后会偏右下；
        // 按缩放后的实际尺寸重新居中。句柄可能重建，因此用标记确保只在首次创建时执行，
        // 避免用户移动窗口后因句柄重建又被拉回屏幕中央。
        if (!_initialPositionApplied)
        {
            _initialPositionApplied = true;
            if (StartPosition == FormStartPosition.CenterScreen)
            {
                var work = Screen.FromControl(this).WorkingArea;
                Location = new Point(
                    work.Left + Math.Max(0, (work.Width - Width) / 2),
                    work.Top + Math.Max(0, (work.Height - Height) / 2));
            }
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // AutoScaleMode.None 只关闭子控件的自动布局缩放；Form.OnDpiChanged 仍会应用系统
        // 建议的外层窗口 Bounds、字体和 MinimumSize/MaximumSize。让框架负责这些顶层属性，
        // 本类随后只缩放子控件；若再对 Form 调用 Scale，外层窗口和尺寸约束就会被处理两次。
        base.OnDpiChanged(e);
        ApplyDpi(e.DeviceDpiNew, scaleForm: false);
        KeepInsideWorkingArea();
    }

    // 高 DPI 小屏（如 1080p@200%，逻辑高仅 540）下窗口可能比屏幕还高/宽：
    // 尺寸封顶到工作区，并保证整个窗口落在工作区内——否则居中后上下超出屏幕，
    // 底部按钮行直接被挤出可视区（"导入报告按钮在框外"、"我的报告大到要拖"的根因）。
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        KeepInsideWorkingArea();
    }

    private void KeepInsideWorkingArea()
    {
        var work = Screen.FromControl(this).WorkingArea;
        if (Width > work.Width || Height > work.Height)
            Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        var x = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
        var y = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        Location = new Point(x, y);
    }

    /// <summary>
    /// 按当前已应用的 DPI 到目标 DPI 的比例缩放一次。初次创建缩放整个 Form；跨屏时
    /// Form.OnDpiChanged 已处理顶层属性，因此只缩放子控件。protected 便于派生窗体回归验证。
    /// </summary>
    protected void ApplyDpi(int dpi, bool scaleForm)
    {
        if (dpi <= 0 || dpi == _appliedDpi)
        {
            if (dpi > 0) _appliedDpi = dpi;
            return;
        }
        var factor = dpi / (float)_appliedDpi;
        _appliedDpi = dpi;
        // 外层挂起布局：Scale 内部的 ResumeLayout 只会递减计数不会真正重排，
        // 等 Scale 全部完成后统一一次重排，避免 AutoSize 控件按中间态测量残留。
        // 注意不要再手动缩放 MinimumSize/MaximumSize：.NET 8 的 Control.Scale 已经会
        // 缩放它们，再缩一次会把最小尺寸放大两倍，整个窗口被撑成巨窗
        SuspendLayout();
        try
        {
            if (scaleForm)
            {
                Scale(new SizeF(factor, factor));
            }
            else
            {
                foreach (Control child in Controls)
                    child.Scale(new SizeF(factor, factor));
            }
        }
        finally
        {
            ResumeLayout(true);
        }
        ForceRelayout(this);
        PerformLayout();
    }

    // Scale 只按旧坐标缩放子控件 Bounds，嵌套容器（如 FlowLayoutPanel）不会自动按新
    // 宽度重新排布子项。自底向上逐层强制布局一次，让每个容器用缩放后的尺寸重新测量。
    private static void ForceRelayout(Control c)
    {
        foreach (Control child in c.Controls) ForceRelayout(child);
        c.PerformLayout(c, "Bounds");
    }
}
