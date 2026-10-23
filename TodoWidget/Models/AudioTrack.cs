namespace TodoWidget.Models;

public class AudioTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Duration { get; set; } = "05:00";
    public double DurationSeconds { get; set; } = 300;
    public string? FilePath { get; set; }
    public string? Url { get; set; }
    public string? Category { get; set; }
}
