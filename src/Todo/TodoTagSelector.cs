using SedentaryReminder.Data;

namespace SedentaryReminder.Todo;

/// <summary>复用快捷标签，允许留空或输入自定义标签。</summary>
internal sealed class TodoTagSelector : ComboBox
{
    private readonly AppConfig _config;

    public TodoTagSelector(AppConfig config, string? tag = null)
    {
        _config = config;
        Dock = DockStyle.Fill;
        AccessibleName = "待办标签";
        DropDownStyle = ComboBoxStyle.DropDown;
        Text = tag ?? "";
        ReloadTags();
    }

    protected override void OnDropDown(EventArgs e)
    {
        ReloadTags();
        base.OnDropDown(e);
    }

    private void ReloadTags()
    {
        var current = Text;
        Items.Clear();
        Items.Add("");
        foreach (var tag in _config.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct())
            Items.Add(tag);
        if (current.Length > 0 && !Items.Contains(current)) Items.Add(current);
        Text = current;
    }
}
