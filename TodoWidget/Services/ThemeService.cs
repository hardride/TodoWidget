using System.IO;
using System.Text.Json;

namespace TodoWidget.Services;

public class ThemeService
{
    private readonly string _settingsPath;
    private static readonly string[] AvailableThemes = ["Default", "Windows 98"];

    public string CurrentTheme { get; private set; } = "Default";

    public ThemeService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "TodoWidget");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
        Load();
    }

    public string[] GetThemes() => AvailableThemes;

    public string NextTheme()
    {
        var idx = Array.IndexOf(AvailableThemes, CurrentTheme);
        CurrentTheme = AvailableThemes[(idx + 1) % AvailableThemes.Length];
        Save();
        return CurrentTheme;
    }

    private void Load()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings != null && AvailableThemes.Contains(settings.Theme))
                    CurrentTheme = settings.Theme;
            }
            catch { }
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(new Settings { Theme = CurrentTheme });
        File.WriteAllText(_settingsPath, json);
    }

    private class Settings
    {
        public string Theme { get; set; } = "Default";
    }
}
