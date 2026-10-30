using System.Reflection;

namespace SedentaryReminder;

/// <summary>应用版本号（读取程序集 InformationalVersion，与项目文件里的 Version 一致）。</summary>
internal static class AppInfo
{
    public static readonly string Version =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "1.0.0";
}
