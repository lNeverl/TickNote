namespace SedentaryReminder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // PerMonitorV2 必须在创建任何窗口之前设置（N10 / M2-15）
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 单实例：避免两个进程同时写同一个 data.db
        using var mutex = new Mutex(true, @"Local\SedentaryReminder.SingleInstance", out bool created);
        if (!created) return;

        Application.Run(new AppContext());
    }
}
