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

            var tagClean = tag.TrimStart('v', 'V');
            // Normalize legacy typo like "1.08" to "1.0.8" so System.Version doesn't treat 08 as minor 8
            var matchLeadingZero = System.Text.RegularExpressions.Regex.Match(tagClean, @"^(\d+)\.0(\d+)(\..*)?$");
            var versionString = matchLeadingZero.Success
                ? $"{matchLeadingZero.Groups[1].Value}.0.{matchLeadingZero.Groups[2].Value}{matchLeadingZero.Groups[3].Value}"
                : tagClean;

            var match = System.Text.RegularExpressions.Regex.Match(versionString, @"\d+(\.\d+)+");
            if (!match.Success || !Version.TryParse(match.Value, out var latest)) return;

            var current = Assembly.GetEntryAssembly()?.GetName().Version;
            if (current == null) return;

            if (latest > current)
            {
                var url = doc.RootElement.GetProperty("html_url").GetString();
                var displayTag = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag : $"v{tag}";
                var result = MessageBox.Show(
                    $"ADHD to-do обновился и теперь тебя ждёт {displayTag}!\n\nОткрыть страницу загрузки?",
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