namespace TodoWidget.Models;

public class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsUrgent { get; set; }
    public DateTime? UrgentStartedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
