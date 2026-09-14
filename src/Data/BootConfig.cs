using System.Text.Json;
using System.Text.Json.Serialization;

namespace SedentaryReminder.Data;

/// <summary>
/// 引导配置（M0-5）：EXE 旁唯一文件 boot.config.json，只记录数据目录路径。
/// </summary>
public sealed class BootConfig
{
    public string DataDir { get; set; } = "";

    public static string BootFilePath =>
        Path.Combine(System.AppContext.BaseDirectory, "boot.config.json");

    /// <summary>读取引导文件；文件缺失、损坏或路径无效时返回 null（触发重新向导，M0-6）。</summary>
    public static BootConfig? Load()
    {
        try
        {
            if (!File.Exists(BootFilePath)) return null;
            var cfg = JsonSerializer.Deserialize<BootConfig>(File.ReadAllText(BootFilePath));
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.DataDir)) return null;
            if (!Directory.Exists(cfg.DataDir)) return null;
            return cfg;
        }
        catch
        {
            return null; // 引导文件损坏时重走向导而不是崩溃
        }
    }

    public static void Save(string dataDir)
    {
        var json = JsonSerializer.Serialize(new BootConfig { DataDir = dataDir },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(BootFilePath, json);
    }

    /// <summary>便携模式的数据目录（EXE 旁 data\）。</summary>
    public static string PortableDataDir =>
        Path.Combine(System.AppContext.BaseDirectory, "data");
}

/// <summary>
/// 全部用户设置（M6-3）：只存数据目录的 config.json，拷走数据目录 = 拷走完整环境。
/// </summary>
public sealed class AppConfig
{
    // —— M1 提醒引擎 ——
    public int IntervalMinutes { get; set; } = 50;        // 提醒周期 20-120
    public int BreakMinutes { get; set; } = 5;            // 活动时长 1-30
    public int PostponeMinutes { get; set; } = 10;        // 每次推迟时长
    public string WorkStart { get; set; } = "08:30";
    public string WorkEnd { get; set; } = "18:30";
    public bool LunchEnabled { get; set; } = true;
    public string LunchStart { get; set; } = "12:00";
    public string LunchEnd { get; set; } = "13:30";
    public int StartupDelayMinutes { get; set; } = 10;    // 首次启动延迟
    public bool IdleDetectionEnabled { get; set; } = true;
    public int IdleThresholdMinutes { get; set; } = 5;
    public bool IdleRestart { get; set; } = false;        // true=回来后重新计时；false=继续剩余时间
    public bool FullscreenDndEnabled { get; set; } = true; // 演示/会议勿扰（M1-9）

    // —— M2 弹窗 ——
    public bool EscIsPostpone { get; set; } = true;
    public string ReminderText { get; set; } = "该起来活动一下了！建议活动 {0} 分钟";

    // —— M3 标签 ——
    public List<string> Tags { get; set; } = new()
        { "故障调查", "功能开发", "软件释放", "功能评估", "杂事协助", "会议" };

    // —— M4 报告 ——
    public bool WeekStartsMonday { get; set; } = true;
    public string DailyPromptTemplate { get; set; } = DefaultDailyPrompt;
    public string WeeklyPromptTemplate { get; set; } = DefaultWeeklyPrompt;

    // —— M5 ——
    public bool AutoStart { get; set; } = false;

    [JsonIgnore]
    public string DataDir { get; set; } = "";

    private static string ConfigPath(string dir) => Path.Combine(dir, "config.json");

    public static AppConfig Load(string dataDir)
    {
        AppConfig cfg;
        try
        {
            var path = ConfigPath(dataDir);
            cfg = File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig()
                : new AppConfig();
        }
        catch
        {
            cfg = new AppConfig(); // 配置损坏时回退默认值，不崩溃
        }
        cfg.DataDir = dataDir;
        return cfg;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath(DataDir), json);
    }

    // 提示词模板占位符：{date} {week} {logs} {daily_reports} {stats}
    public const string DefaultDailyPrompt =
        "你是一位嵌入式汽车软件工程师的日报助手。请根据下面的今日工作流水，生成一份正式的中文工作日报。\n" +
        "要求：\n" +
        "1. 按以下分类归组：【功能开发】【故障调查】【软件释放】【功能评估】【协助支持】【会议及其他】\n" +
        "2. 把口语化描述改为正式书面表述，保留技术细节（问题现象、定位结论、责任人、版本号）\n" +
        "3. 同类琐碎事项合并为一条，突出\"问题-动作-结论\"结构，体现工作量\n" +
        "4. 末尾附【明日计划】（根据今天未完成事项推断）和【今日久坐统计】\n" +
        "5. 只输出日报正文，不要其他解释\n" +
        "\n" +
        "=== 今日工作流水（{date}）===\n" +
        "{logs}\n" +
        "\n" +
        "=== 久坐统计 ===\n" +
        "{stats}";

    public const string DefaultWeeklyPrompt =
        "你是一位嵌入式汽车软件工程师的周报助手。请根据下面的本周工作流水与各天日报，生成一份正式的中文工作周报。\n" +
        "要求：\n" +
        "1. 按天汇总本周工作内容（{week}），突出\"问题-动作-结论\"结构，体现工作量\n" +
        "2. 提炼【本周重点成果】\n" +
        "3. 末尾附【下周计划】和【本周久坐统计】\n" +
        "4. 只输出周报正文，不要其他解释\n" +
        "\n" +
        "=== 本周工作流水（{week}）===\n" +
        "{logs}\n" +
        "\n" +
        "=== 本周已生成日报 ===\n" +
        "{daily_reports}\n" +
        "\n" +
        "=== 久坐统计 ===\n" +
        "{stats}";
}
