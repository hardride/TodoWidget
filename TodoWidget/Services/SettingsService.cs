using System.IO;
using System.Text.Json;

namespace TodoWidget.Services;

public class SettingsService
{
    private readonly string _path;
    private AppSettings _settings = new();

    public string Theme
    {
        get => _settings.Theme;
        set { _settings.Theme = value; Save(); }
    }

    public double Opacity
    {
        get => _settings.Opacity;
        set { _settings.Opacity = value; Save(); }
    }

    public bool IsPinned
    {
        get => _settings.IsPinned;
        set { _settings.IsPinned = value; Save(); }
    }

    public double Height
    {
        get => _settings.Height;
        set { _settings.Height = value; Save(); }
    }

    public int DeadlineSlot
    {
        get => DeadlineSlots.Count > 0 ? DeadlineSlots[0] : 3;
        set
        {
            if (DeadlineSlots.Count > 0) DeadlineSlots[0] = value;
            else DeadlineSlots.Add(value);
            Save();
        }
    }

    public List<int> DeadlineSlots
    {
        get
        {
            if (_settings.DeadlineSlots == null)
            {
                _settings.DeadlineSlots = new List<int> { _settings.DeadlineSlot >= 0 ? _settings.DeadlineSlot : 3 };
            }
            return _settings.DeadlineSlots;
        }
        set
        {
            _settings.DeadlineSlots = value;
            _settings.DeadlineSlot = value.Count > 0 ? value[0] : 0;
            Save();
        }
    }

    public List<string> DeadlineLabels
    {
        get => _settings.DeadlineLabels ??= new List<string>();
        set
        {
            _settings.DeadlineLabels = value;
            Save();
        }
    }

    public string GetDeadlineLabel(int lineIndex)
    {
        if (_settings.DeadlineLabels != null && lineIndex >= 0 && lineIndex < _settings.DeadlineLabels.Count)
        {
            var lbl = _settings.DeadlineLabels[lineIndex];
            if (lbl != null)
            {
                if (string.IsNullOrEmpty(lbl) && GetDeadlineTimerEnd(lineIndex) == null)
                    return "title";
                return lbl;
            }
        }
        return "title";
    }

    public void SetDeadlineLabel(int lineIndex, string label)
    {
        _settings.DeadlineLabels ??= new List<string>();
        while (_settings.DeadlineLabels.Count <= lineIndex)
        {
            _settings.DeadlineLabels.Add("title");
        }
        _settings.DeadlineLabels[lineIndex] = label?.Trim() ?? "";
        Save();
    }

    public List<string?> DeadlineTimerEnds
    {
        get => _settings.DeadlineTimerEnds ??= new List<string?>();
        set
        {
            _settings.DeadlineTimerEnds = value;
            Save();
        }
    }

    public DateTime? GetDeadlineTimerEnd(int lineIndex)
    {
        if (_settings.DeadlineTimerEnds != null && lineIndex >= 0 && lineIndex < _settings.DeadlineTimerEnds.Count)
        {
            var str = _settings.DeadlineTimerEnds[lineIndex];
            if (!string.IsNullOrEmpty(str) && DateTime.TryParse(str, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                return dt;
            }
        }
        return null;
    }

    public void SetDeadlineTimerEnd(int lineIndex, DateTime? endUtc)
    {
        _settings.DeadlineTimerEnds ??= new List<string?>();
        while (_settings.DeadlineTimerEnds.Count <= lineIndex)
        {
            _settings.DeadlineTimerEnds.Add(null);
        }
        _settings.DeadlineTimerEnds[lineIndex] = endUtc?.ToString("o");
        Save();
    }

    public List<string> DeadlineIds
    {
        get => _settings.DeadlineIds ??= new List<string>();
        set
        {
            _settings.DeadlineIds = value;
            Save();
        }
    }

    public string GetDeadlineId(int lineIndex)
    {
        _settings.DeadlineIds ??= new List<string>();
        while (_settings.DeadlineIds.Count <= lineIndex)
        {
            _settings.DeadlineIds.Add(Guid.NewGuid().ToString());
        }
        return _settings.DeadlineIds[lineIndex];
    }

    public void SaveSettings() => Save();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "TodoWidget");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
        Load();
    }

    private void Load()
    {
        if (File.Exists(_path))
        {
            try
            {
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
            }
            catch { _settings = new(); }
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private class AppSettings
    {
        public string Theme { get; set; } = "Dark";
        public double Opacity { get; set; } = 1.0;
        public bool IsPinned { get; set; } = true;
        public double Height { get; set; } = 480;
        public int DeadlineSlot { get; set; } = 3;
        public List<int>? DeadlineSlots { get; set; }
        public List<string>? DeadlineLabels { get; set; }
        public List<string?>? DeadlineTimerEnds { get; set; }
        public List<string>? DeadlineIds { get; set; }
    }
}
