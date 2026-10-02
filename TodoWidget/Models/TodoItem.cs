using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace TodoWidget.Models;

public class TodoItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsUrgent { get; set; }
    public DateTime? UrgentStartedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? GroupDeadlineId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAboveDeadline { get; set; } = true;

    private CornerRadius _cardCornerRadius = new CornerRadius(8);
    [System.Text.Json.Serialization.JsonIgnore]
    public CornerRadius CardCornerRadius
    {
        get => _cardCornerRadius;
        set
        {
            if (_cardCornerRadius != value)
            {
                _cardCornerRadius = value;
                OnPropertyChanged();
            }
        }
    }

    private Thickness _cardMargin = new Thickness(0, 0, 0, 6);
    [System.Text.Json.Serialization.JsonIgnore]
    public Thickness CardMargin
    {
        get => _cardMargin;
        set
        {
            if (_cardMargin != value)
            {
                _cardMargin = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
