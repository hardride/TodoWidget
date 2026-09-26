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
    }
}
