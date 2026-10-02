using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace TodoWidget.Models;

public class DeadlineItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public int Slot { get; set; }
    public int LineIndex { get; set; }

    private string _labelText = "deadline";
    public string LabelText
    {
        get => _labelText;
        set
        {
            if (_labelText != value)
            {
                _labelText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLabelText));
                OnPropertyChanged(nameof(LabelVisibility));
                OnPropertyChanged(nameof(ShowSeparator));
                OnPropertyChanged(nameof(SeparatorVisibility));
                OnPropertyChanged(nameof(FullDisplayText));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool HasLabelText => !string.IsNullOrEmpty(_labelText);
    public Visibility LabelVisibility => HasLabelText ? Visibility.Visible : Visibility.Collapsed;

    private bool _isTimer;
    public bool IsTimer
    {
        get => _isTimer;
        set
        {
            if (_isTimer != value)
            {
                _isTimer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTimerText));
                OnPropertyChanged(nameof(TimerVisibility));
                OnPropertyChanged(nameof(ShowSeparator));
                OnPropertyChanged(nameof(SeparatorVisibility));
                OnPropertyChanged(nameof(FullDisplayText));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    private string _timerText = "";
    public string TimerText
    {
        get => _timerText;
        set
        {
            if (_timerText != value)
            {
                _timerText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTimerText));
                OnPropertyChanged(nameof(TimerVisibility));
                OnPropertyChanged(nameof(ShowSeparator));
                OnPropertyChanged(nameof(SeparatorVisibility));
                OnPropertyChanged(nameof(FullDisplayText));
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool HasTimerText => IsTimer && !string.IsNullOrEmpty(_timerText);
    public Visibility TimerVisibility => HasTimerText ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowSeparator => HasLabelText && HasTimerText;
    public Visibility SeparatorVisibility => ShowSeparator ? Visibility.Visible : Visibility.Collapsed;

    private bool _isExpired;
    public bool IsExpired
    {
        get => _isExpired;
        set
        {
            if (_isExpired != value)
            {
                _isExpired = value;
                OnPropertyChanged();
            }
        }
    }

    public string FullDisplayText
    {
        get
        {
            if (HasLabelText && HasTimerText) return $"{LabelText} · {TimerText}";
            if (HasTimerText) return TimerText;
            return LabelText;
        }
    }

    public string Title
    {
        get => FullDisplayText;
        set => LabelText = value;
    }

    private bool _hasGroupedTasks;
    public bool HasGroupedTasks
    {
        get => _hasGroupedTasks;
        set
        {
            if (_hasGroupedTasks != value)
            {
                _hasGroupedTasks = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContainerMargin));
            }
        }
    }

    public Thickness ContainerMargin => HasGroupedTasks ? new Thickness(0, 5, 0, 2) : new Thickness(0, 5, 0, 7);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
