using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace TodoWidget.Services;

public class UpdateService
{
    private const string Repo = "hardride/TodoWidget";
    private static readonly HttpClient Http = new();
    private readonly DispatcherTimer _timer;

    public UpdateService()
    {
        if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
            Http.DefaultRequestHeaders.Add("User-Agent", "TodoWidget");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _timer.Tick += async (s, e) => await CheckForUpdate();
    }

    public void Start()
    {
        // Check once after 30s delay, then every 24h
        Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
        {
            Application.Current?.Dispatcher?.Invoke(async () => await CheckForUpdate());
        });
        _timer.Start();
    }

    private async Task CheckForUpdate()
    {
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tag)) return;

            var match = System.Text.RegularExpressions.Regex.Match(tag, @"\d+(\.\d+)+");
            if (!match.Success || !Version.TryParse(match.Value, out var latest)) return;

            var current = Assembly.GetEntryAssembly()?.GetName().Version;
            if (current == null) return;

            if (latest > current)
            {
                var url = doc.RootElement.GetProperty("html_url").GetString();
                var result = MessageBox.Show(
                    $"ADHD to-do обновился и теперь тебя ждёт v{latest}!\n\nОткрыть страницу загрузки?",
                    "Обновление доступно",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes && url != null)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch { /* Silently ignore network errors */ }
    }
}