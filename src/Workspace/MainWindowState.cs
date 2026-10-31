using System.Text.Json;

namespace SedentaryReminder;

internal sealed class MainWindowState
{
    public string LastPage { get; set; } = nameof(MainPage.Todos);
    public int ClientWidth { get; set; } = 1040;
    public int ClientHeight { get; set; } = 700;
    public int? Left { get; set; }
    public int? Top { get; set; }
    public bool Maximized { get; set; }

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "ui-state.json");

    public static MainWindowState Load(string dataDir)
    {
        try { return JsonSerializer.Deserialize<MainWindowState>(File.ReadAllText(PathFor(dataDir))) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string dataDir)
    {
        try
        {
            var path = PathFor(dataDir);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
