namespace SedentaryReminder;

/// <summary>
/// 高 DPI 窗体基类。不依赖框架自动缩放——在"系统 DPI 与显示器 DPI 不一致"等场景下
/// （如缩放后未重新登录、远程/虚拟显示器），框架的初始缩放会静默失效导致布局被裁。
/// 改为：句柄创建后按窗口实际 DeviceDpi 手动等比缩放一次，DPI 变化时按比例再缩放。
/// 字体随 Scale 一并缩放，AutoSize 控件按缩放后字体重新测量，行为与正确自动缩放一致。
/// </summary>
public abstract class DpiForm : Form
{
    private int _appliedDpi = 96;

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
        ApplyDpi(DeviceDpi);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpi(e.DeviceDpiNew);
    }

    // 高 DPI 小屏（如 1080p@200%，逻辑高仅 540）下窗口可能比屏幕还高/宽：
    // 尺寸封顶到工作区，并保证整个窗口落在工作区内——否则居中后上下超出屏幕，
    // 底部按钮行直接被挤出可视区（"导入报告按钮在框外"、"我的报告大到要拖"的根因）。
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var work = Screen.FromControl(this).WorkingArea;
        if (Width > work.Width || Height > work.Height)
            Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        var x = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width));
        var y = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height));
        Location = new Point(x, y);
    }

    private void ApplyDpi(int dpi)
    {
        if (dpi <= 0 || dpi == _appliedDpi)
        {
            if (dpi > 0) _appliedDpi = dpi;
            return;
        }
        var factor = dpi / (float)_appliedDpi;
        _appliedDpi = dpi;
        Scale(new SizeF(factor, factor));
        ScaleSizeConstraints(this, factor);
    }

    // Control.Scale 不缩放 MaximumSize/MinimumSize，不补齐的话换行宽度等约束会和
    // 缩放后的字体不成比例（文本多折行把固定高度窗口的内容行撑超，挤没末尾的按钮）。
    private static void ScaleSizeConstraints(Control c, float factor)
    {
        if (c.MaximumSize.Width > 0 || c.MaximumSize.Height > 0)
        {
            c.MaximumSize = new Size(
                c.MaximumSize.Width > 0 ? (int)Math.Round(c.MaximumSize.Width * factor) : 0,
                c.MaximumSize.Height > 0 ? (int)Math.Round(c.MaximumSize.Height * factor) : 0);
        }
        if (c.MinimumSize.Width > 0 || c.MinimumSize.Height > 0)
        {
            c.MinimumSize = new Size(
                c.MinimumSize.Width > 0 ? (int)Math.Round(c.MinimumSize.Width * factor) : 0,
                c.MinimumSize.Height > 0 ? (int)Math.Round(c.MinimumSize.Height * factor) : 0);
        }
        foreach (Control child in c.Controls) ScaleSizeConstraints(child, factor);
    }
}
