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

        // M0 引导在消息循环启动之前完成：用户点叉取消向导 = 直接干净退出，
        // 不会留下无窗口空转的僵尸进程
        var dataDir = AppContext.ResolveDataDir();
        if (dataDir == null) return;

        Application.Run(new AppContext(dataDir));
    }
}
