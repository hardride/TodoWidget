using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TodoWidget.Models;

namespace TodoWidget.Services;

public class AudioService
{
    private const string DefaultPlaylistUrl = "https://cdn.jsdelivr.net/gh/hardride/TodoWidget@master/playlist.json";

    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _timer = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _musicFolder;
    private readonly string _cacheFolder;
    private readonly ConcurrentDictionary<string, Task<string?>> _activeDownloads = new();

    private List<AudioTrack> _tracks = new();
    private int _currentIndex = 0;
    private bool _isPlaying = false;
    private double _volume = 0.7;
    private bool _isMuted = false;
    private CancellationTokenSource? _fadeCts;
    private double _virtualPosition = 0;
    private DateTime _lastTickTime = DateTime.UtcNow;

    public event Action<bool>? StateChanged;
    public event Action<AudioTrack>? TrackChanged;
    public event Action<double, double>? PositionChanged;
    public event Action? PlaylistUpdated;

    public IReadOnlyList<AudioTrack> Tracks => _tracks;
    public AudioTrack? CurrentTrack => _tracks.Count > 0 && _currentIndex >= 0 && _currentIndex < _tracks.Count ? _tracks[_currentIndex] : null;
    public bool IsPlaying => _isPlaying;
    public double Volume => _volume;
    public bool IsMuted => _isMuted;

    public AudioService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _musicFolder = Path.Combine(appData, "TodoWidget", "Music");
        _cacheFolder = Path.Combine(appData, "TodoWidget", "Cache");

        try
        {
            Directory.CreateDirectory(_cacheFolder);
        }
        catch { }

        // Clean up any old downloaded audio files from legacy generator
        SoundGenerator.EnsureDefaultTracksExist(_musicFolder);

        // Load tracks immediately from local playlist.json or built-in defaults (streaming/CDN URLs)
        LoadInitialTracks();

        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += Timer_Tick;

        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
        _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        _mediaPlayer.Volume = _volume;

        if (_tracks.Count > 0)
        {
            PrepareTrack(0);
        }

        // Asynchronously check remote playlist and pre-cache audio files in the background
        Task.Run(async () =>
        {
            await SyncRemotePlaylistAsync();
            await PrecacheAllTracksAsync();
        });
    }

    public static string NormalizeAudioUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url ?? string.Empty;

        // Convert raw.githubusercontent.com to global CDN cdn.jsdelivr.net (faster, not blocked by RKN/ISPs)
        // Format: https://raw.githubusercontent.com/{user}/{repo}/{branch}/{path...}
        // Becomes: https://cdn.jsdelivr.net/gh/{user}/{repo}@{branch}/{path...}
        const string rawPrefix = "https://raw.githubusercontent.com/";
        if (url.StartsWith(rawPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string path = url.Substring(rawPrefix.Length);
            string[] parts = path.Split('/', 3);
            if (parts.Length == 3)
            {
                string user = parts[0];
                string repo = parts[1];
                string rest = parts[2];
                int branchSlash = rest.IndexOf('/');
                if (branchSlash > 0)
                {
                    string branch = rest.Substring(0, branchSlash);
                    string file = rest.Substring(branchSlash + 1);
                    return $"https://cdn.jsdelivr.net/gh/{user}/{repo}@{branch}/{file}";
                }
            }
        }

        return url;
    }

    private string GetTrackFileName(AudioTrack track)
    {
        string ext = ".mp3";
        try
        {
            if (!string.IsNullOrEmpty(track.Url))
            {
                var uri = new Uri(NormalizeAudioUrl(track.Url));
                string candidateExt = Path.GetExtension(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(candidateExt) && candidateExt.Length <= 5)
                {
                    ext = candidateExt.ToLowerInvariant();
                }
            }
        }
        catch { }

        string safeId = string.Concat(track.Id.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeId)) safeId = Guid.NewGuid().ToString("N");
        return $"{safeId}{ext}";
    }

    public bool IsTrackCached(AudioTrack track, out string cachedPath)
    {
        cachedPath = Path.Combine(_cacheFolder, GetTrackFileName(track));
        try
        {
            if (File.Exists(cachedPath))
            {
                var fi = new FileInfo(cachedPath);
                if (fi.Length > 4096)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    public Task<string?> EnsureTrackCachedAsync(AudioTrack track, CancellationToken ct = default)
    {
        if (IsTrackCached(track, out string existingPath))
        {
            return Task.FromResult<string?>(existingPath);
        }

        if (string.IsNullOrWhiteSpace(track.Url)) return Task.FromResult<string?>(null);

        string targetPath = Path.Combine(_cacheFolder, GetTrackFileName(track));
        string url = NormalizeAudioUrl(track.Url);

        return _activeDownloads.GetOrAdd(track.Id, _ => DownloadTrackAsync(track, targetPath, url, ct));
    }

    private async Task<string?> DownloadTrackAsync(AudioTrack track, string targetPath, string url, CancellationToken ct)
    {
        string tempPath = targetPath + ".tmp";
        try
        {
            Directory.CreateDirectory(_cacheFolder);
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.CopyToAsync(fileStream, ct);
            }

            if (File.Exists(tempPath) && new FileInfo(tempPath).Length > 4096)
            {
                if (File.Exists(targetPath))
                {
                    try { File.Delete(targetPath); } catch { }
                }
                File.Move(tempPath, targetPath);
                return targetPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to cache track {track.Title}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
            _activeDownloads.TryRemove(track.Id, out _);
        }

        return null;
    }

    private async Task PrecacheAllTracksAsync()
    {
        var list = _tracks.ToList();
        foreach (var track in list)
        {
            try
            {
                if (!IsTrackCached(track, out _))
                {
                    await EnsureTrackCachedAsync(track);
                    await Task.Delay(1000);
                }
            }
            catch { }
        }
    }

    private void OpenTrack(AudioTrack track)
    {
        if (IsTrackCached(track, out string localPath))
        {
            try
            {
                _mediaPlayer.Open(new Uri(localPath, UriKind.Absolute));
                return;
            }
            catch { }
        }

        string normalizedUrl = NormalizeAudioUrl(track.Url);
        if (!string.IsNullOrEmpty(normalizedUrl))
        {
            try
            {
                _mediaPlayer.Open(new Uri(normalizedUrl));
            }
            catch { }

            _ = EnsureTrackCachedAsync(track);
        }
    }

    private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
    {
        if (_isPlaying && !_isMuted && _mediaPlayer.Volume < 0.05)
        {
            _mediaPlayer.Volume = _volume;
        }

        if (_mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            var dur = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (dur > 0 && CurrentTrack != null)
            {
                CurrentTrack.DurationSeconds = dur;
                int m = (int)(dur / 60);
                int sec = (int)(dur % 60);
                CurrentTrack.Duration = $"{m:D2}:{sec:D2}";
                PositionChanged?.Invoke(_virtualPosition, dur);
            }
        }
    }

    private async void MediaPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"MediaFailed: {e.ErrorException?.Message}");
        var track = CurrentTrack;
        if (track == null) return;

        try
        {
            string? cachedPath = await EnsureTrackCachedAsync(track);
            if (!string.IsNullOrEmpty(cachedPath) && File.Exists(cachedPath))
            {
                var app = Application.Current;
                if (app != null)
                {
                    await app.Dispatcher.InvokeAsync(() =>
                    {
                        if (CurrentTrack?.Id == track.Id && _isPlaying)
                        {
                            _mediaPlayer.Open(new Uri(cachedPath, UriKind.Absolute));
                            _mediaPlayer.Volume = _isMuted ? 0.0 : _volume;
                            _mediaPlayer.Play();
                        }
                    });
                }
            }
        }
        catch { }
    }

    private void LoadInitialTracks()
    {
        _tracks.Clear();

        try
        {
            // Check if playlist.json exists in app directory or parent project directory
            string localJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playlist.json");
            if (!File.Exists(localJsonPath))
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null && !File.Exists(localJsonPath))
                {
                    localJsonPath = Path.Combine(dir.FullName, "playlist.json");
                    dir = dir.Parent;
                }
            }

            if (File.Exists(localJsonPath))
            {
                string json = File.ReadAllText(localJsonPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tracks", out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in array.EnumerateArray())
                    {
                        string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        string subtitle = item.TryGetProperty("subtitle", out var s) ? s.GetString() ?? "" : "";
                        string duration = item.TryGetProperty("duration", out var d) ? d.GetString() ?? "05:00" : "05:00";
                        double durationSeconds = item.TryGetProperty("durationSeconds", out var ds) ? ds.GetDouble() : 300;
                        string trackUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                        string id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();

                        if (!string.IsNullOrEmpty(title))
                        {
                            _tracks.Add(new AudioTrack
                            {
                                Id = id,
                                Title = title,
                                Subtitle = subtitle,
                                Duration = duration,
                                DurationSeconds = durationSeconds,
                                Url = NormalizeAudioUrl(trackUrl)
                            });
                        }
                    }
                }
            }
        }
        catch { }

        // Fallback default CDN streaming tracks if playlist.json was missing or empty
        if (_tracks.Count == 0)
        {
            _tracks = new List<AudioTrack>
            {
                new() { Id = "track-nature", Title = "Звуки природы", Url = "https://cdn.jsdelivr.net/gh/remvze/moodist@main/public/sounds/animals/birds.mp3", DurationSeconds = 128 },
                new() { Id = "track-campfire", Title = "Костёр", Url = "https://cdn.jsdelivr.net/gh/remvze/moodist@main/public/sounds/nature/campfire.mp3", DurationSeconds = 202 },
                new() { Id = "track-river", Title = "Река", Url = "https://cdn.jsdelivr.net/gh/remvze/moodist@main/public/sounds/nature/river.mp3", DurationSeconds = 243 },
                new() { Id = "track-waterfall", Title = "Водопад", Url = "https://cdn.jsdelivr.net/gh/remvze/moodist@main/public/sounds/nature/waterfall.mp3", DurationSeconds = 34 },
                new() { Id = "track-metropolis", Title = "Мегаполис", Url = "https://cdn.jsdelivr.net/gh/remvze/moodist@main/public/sounds/urban/busy-street.mp3", DurationSeconds = 145 },
                new() { Id = "track-office", Title = "Офисный шум", Url = "https://cdn.jsdelivr.net/gh/siwalikm/coffitivity-offline@master/assets/murmur.mp3", DurationSeconds = 1048 },
                new() { Id = "track-brown-noise", Title = "Коричневый шум", Url = "https://cdn.jsdelivr.net/gh/remvze/moodist@main/public/sounds/noise/brown-noise.wav", DurationSeconds = 900 }
            };
        }

        _currentIndex = 0;
    }

    public async Task SyncRemotePlaylistAsync(string? customUrl = null)
    {
        try
        {
            string url = !string.IsNullOrEmpty(customUrl) ? NormalizeAudioUrl(customUrl) : DefaultPlaylistUrl;
            string json = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tracks", out var tracksArray) && tracksArray.ValueKind == JsonValueKind.Array)
            {
                var remoteTracks = new List<AudioTrack>();
                foreach (var item in tracksArray.EnumerateArray())
                {
                    string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    string subtitle = item.TryGetProperty("subtitle", out var s) ? s.GetString() ?? "" : "";
                    string duration = item.TryGetProperty("duration", out var d) ? d.GetString() ?? "05:00" : "05:00";
                    double durationSeconds = item.TryGetProperty("durationSeconds", out var ds) ? ds.GetDouble() : 300;
                    string trackUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    string id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();

                    if (!string.IsNullOrEmpty(title))
                    {
                        remoteTracks.Add(new AudioTrack
                        {
                            Id = id,
                            Title = title,
                            Subtitle = subtitle,
                            Duration = duration,
                            DurationSeconds = durationSeconds,
                            Url = NormalizeAudioUrl(trackUrl)
                        });
                    }
                }

                if (remoteTracks.Count > 0)
                {
                    _tracks = remoteTracks;
                    PlaylistUpdated?.Invoke();
                    _ = PrecacheAllTracksAsync();
                }
            }
        }
        catch
        {
            // Offline or repository raw file not reachable; local playlist remains active
        }
    }

    private void PrepareTrack(int index)
    {
        if (index < 0 || index >= _tracks.Count) return;
        var track = _tracks[index];
        OpenTrack(track);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying) return;

        var track = CurrentTrack;
        if (track == null) return;

        var now = DateTime.UtcNow;
        double elapsed = (now - _lastTickTime).TotalSeconds;
        _lastTickTime = now;

        if (elapsed < 0 || elapsed > 1.5) elapsed = 0.25;

        try
        {
            // Loop current stream seamlessly if reaching file boundary
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                double dur = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                if (dur > 2.0 && _mediaPlayer.Position.TotalSeconds >= dur - 0.3)
                {
                    _virtualPosition = 0;
                    _mediaPlayer.Position = TimeSpan.Zero;
                    _mediaPlayer.Play();
                }
            }

            _virtualPosition = _mediaPlayer.Position.TotalSeconds;
            PositionChanged?.Invoke(_virtualPosition, track.DurationSeconds);
        }
        catch { }
    }

    private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        try
        {
            _virtualPosition = 0;
            var track = CurrentTrack;
            if (track != null && IsTrackCached(track, out string localPath))
            {
                if (_mediaPlayer.Source == null || !_mediaPlayer.Source.IsFile)
                {
                    _mediaPlayer.Open(new Uri(localPath, UriKind.Absolute));
                }
            }
            _mediaPlayer.Position = TimeSpan.Zero;
            _mediaPlayer.Play();
        }
        catch { }
    }

    private async Task FadeVolumeAsync(double fromVol, double toVol, int durationMs, CancellationToken ct)
    {
        if (durationMs <= 0 || Math.Abs(toVol - fromVol) < 0.005)
        {
            _mediaPlayer.Volume = Math.Clamp(toVol, 0.0, 1.0);
            return;
        }

        const int intervalMs = 25;
        int steps = Math.Max(1, durationMs / intervalMs);
        double diff = toVol - fromVol;

        for (int i = 1; i <= steps; i++)
        {
            if (ct.IsCancellationRequested) return;

            double t = (double)i / steps;
            double curve = t * t * (3.0 - 2.0 * t);
            double current = fromVol + diff * curve;
            _mediaPlayer.Volume = Math.Clamp(current, 0.0, 1.0);

            try
            {
                await Task.Delay(intervalMs, ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        if (!ct.IsCancellationRequested)
        {
            _mediaPlayer.Volume = Math.Clamp(toVol, 0.0, 1.0);
        }
    }

    public async Task PlayAsync()
    {
        var track = CurrentTrack;
        if (track == null) return;

        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        var ct = cts.Token;

        _isPlaying = true;

        if (_mediaPlayer.Source == null)
        {
            OpenTrack(track);
        }

        _lastTickTime = DateTime.UtcNow;
        _mediaPlayer.Volume = 0.0;
        _mediaPlayer.Play();
        _timer.Start();
        StateChanged?.Invoke(_isPlaying);
        TrackChanged?.Invoke(track);

        double targetVol = _isMuted ? 0.0 : _volume;
        await FadeVolumeAsync(0.0, targetVol, 300, ct);

        if (!ct.IsCancellationRequested && !_isMuted && _mediaPlayer.Volume < targetVol * 0.9)
        {
            _mediaPlayer.Volume = targetVol;
        }
    }

    public void Play() => _ = PlayAsync();

    public async Task PauseAsync()
    {
        if (!_isPlaying) return;

        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        var ct = cts.Token;

        _isPlaying = false;
        StateChanged?.Invoke(_isPlaying);
        _timer.Stop();

        if (_mediaPlayer.Volume > 0.01)
        {
            await FadeVolumeAsync(_mediaPlayer.Volume, 0.0, 250, ct);
        }

        if (!ct.IsCancellationRequested)
        {
            _mediaPlayer.Pause();
            _mediaPlayer.Volume = _isMuted ? 0.0 : _volume;
        }
    }

    public void Pause() => _ = PauseAsync();

    public void TogglePlay()
    {
        if (_isPlaying) Pause();
        else Play();
    }

    public void SelectTrack(AudioTrack track)
    {
        int idx = _tracks.IndexOf(track);
        if (idx >= 0)
        {
            SelectIndex(idx, autoPlay: true);
        }
    }

    public async Task SelectIndexAsync(int index, bool autoPlay = true)
    {
        if (index < 0 || index >= _tracks.Count) return;

        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        var cts = new CancellationTokenSource();
        _fadeCts = cts;
        var ct = cts.Token;

        double targetVol = _isMuted ? 0.0 : _volume;

        if (_isPlaying && _mediaPlayer.Volume > 0.01)
        {
            int fadeOutMs = (int)Math.Max(120, 320 * (_mediaPlayer.Volume / Math.Max(0.01, _volume)));
            await FadeVolumeAsync(_mediaPlayer.Volume, 0.0, fadeOutMs, ct);
            if (ct.IsCancellationRequested) return;
        }

        _mediaPlayer.Stop();
        _currentIndex = index;
        _virtualPosition = 0;
        _lastTickTime = DateTime.UtcNow;

        var track = _tracks[_currentIndex];
        OpenTrack(track);

        TrackChanged?.Invoke(track);
        PositionChanged?.Invoke(0, track.DurationSeconds);

        if (autoPlay)
        {
            _isPlaying = true;
            _mediaPlayer.Volume = 0.0;
            _mediaPlayer.Play();
            _timer.Start();
            StateChanged?.Invoke(_isPlaying);
            await FadeVolumeAsync(0.0, targetVol, 420, ct);

            if (!ct.IsCancellationRequested && !_isMuted && _mediaPlayer.Volume < targetVol * 0.9)
            {
                _mediaPlayer.Volume = targetVol;
            }
        }
        else
        {
            _isPlaying = false;
            _mediaPlayer.Volume = targetVol;
            StateChanged?.Invoke(false);
        }
    }

    public void SelectIndex(int index, bool autoPlay = true)
    {
        _ = SelectIndexAsync(index, autoPlay);
    }

    public void Next()
    {
        if (_tracks.Count == 0) return;
        int next = (_currentIndex + 1) % _tracks.Count;
        SelectIndex(next, autoPlay: _isPlaying);
    }

    public void Prev()
    {
        if (_tracks.Count == 0) return;
        int prev = (_currentIndex - 1 + _tracks.Count) % _tracks.Count;
        SelectIndex(prev, autoPlay: _isPlaying);
    }

    public void Seek(double seconds)
    {
        var track = CurrentTrack;
        if (track == null) return;

        _virtualPosition = Math.Max(0, seconds);
        _lastTickTime = DateTime.UtcNow;
        _mediaPlayer.Position = TimeSpan.FromSeconds(_virtualPosition);
        PositionChanged?.Invoke(_virtualPosition, track.DurationSeconds);
    }

    public void SetVolume(double vol)
    {
        _volume = Math.Clamp(vol, 0.0, 1.0);
        if (_isPlaying)
        {
            _fadeCts?.Cancel();
        }
        if (!_isMuted)
        {
            _mediaPlayer.Volume = _volume;
        }
    }

    public void SetMuted(bool muted)
    {
        _isMuted = muted;
        _mediaPlayer.Volume = _isMuted ? 0.0 : _volume;
    }

    public void ToggleMute()
    {
        SetMuted(!_isMuted);
    }
}
