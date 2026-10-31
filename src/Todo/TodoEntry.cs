namespace SedentaryReminder.Todo;

/// <summary>每日待办条目（表 todo_item），与工作流水独立保存。</summary>
public sealed class TodoEntry
{
    public long Id { get; set; }
    public string TodoDate { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Tag { get; set; }
    public bool IsCompleted { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
}
