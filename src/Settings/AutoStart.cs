using Microsoft.Win32;

namespace SedentaryReminder.Settings;

/// <summary>
/// 开机自启（M5-4）：写 HKCU Run 键；EXE 路径变化后启动时自动修正。
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SedentaryReminder";

    private static string ExePath => Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? System.AppContext.BaseDirectory;

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null) return;
        if (enabled)
            key.SetValue(ValueName, $"\"{ExePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>启动时调用：若已开启自启但注册表里的路径不是当前 EXE，重写为当前路径。</summary>
    public static void EnsurePathCurrent(bool enabled)
    {
        if (!enabled) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        var current = key?.GetValue(ValueName) as string;
        var expected = $"\"{ExePath}\"";
        if (current != expected) Apply(true);
    }
}
