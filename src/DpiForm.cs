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

    protected DpiForm()
    {
        AutoScaleMode = AutoScaleMode.None;
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
