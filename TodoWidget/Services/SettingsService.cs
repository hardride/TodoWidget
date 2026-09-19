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
        File.WriteAllText(_path, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private class AppSettings
    {
        public string Theme { get; set; } = "Dark";
    }
}
