using System.Runtime.InteropServices;
using System.Text;

namespace SedentaryReminder;

/// <summary>Win32 P/Invoke：空闲检测、前台窗口焦点、全屏应用检测。</summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>销毁 Icon.FromHandle 创建的图标句柄（托盘进度环每秒重绘，不销毁会泄漏 GDI 句柄）。</summary>
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>键鼠无操作的秒数（M1-5 空闲检测）。</summary>
    public static int GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        var tick = unchecked((uint)Environment.TickCount);
        return (int)((tick - info.dwTime) / 1000);
    }

    /// <summary>
    /// 前台是否有"真正的全屏应用"（M1-9 演示/会议勿扰）：
    /// 窗口覆盖其所在屏幕完整区域，且不是桌面/Shell，也不是本进程自己的窗口。
    /// </summary>
    public static bool IsFullscreenAppInForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return false; // 自己的弹窗不算

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!GetWindowRect(hwnd, out var rc)) return false;
        var bounds = Screen.FromHandle(hwnd).Bounds;
        // 允许少量误差（有些全屏窗口略微出血）
        return rc.Left <= bounds.Left + 4 && rc.Top <= bounds.Top + 4
            && rc.Right >= bounds.Right - 4 && rc.Bottom >= bounds.Bottom - 4;
    }
}
