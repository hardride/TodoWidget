using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;
using TodoWidget.Models;

namespace TodoWidget.Services;

public class AudioService
{
    private const string DefaultPlaylistUrl = "https://raw.githubusercontent.com/hardride/TodoWidget/master/playlist.json";

    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _timer = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _musicFolder;

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

        // Clean up any old downloaded audio files on user's disk
        SoundGenerator.EnsureDefaultTracksExist(_musicFolder);

        // Load tracks immediately from local playlist.json or built-in defaults (streaming URLs)
        LoadInitialTracks();

        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += Timer_Tick;

        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        _mediaPlayer.MediaOpened += (s, e) =>
        {
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
        };
        _mediaPlayer.Volume = _volume;

        if (_tracks.Count > 0)
        {
            PrepareTrack(0);
        }

        // Asynchronously check remote playlist from GitHub in the background
        Task.Run(async () => await SyncRemotePlaylistAsync());
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
                                Url = trackUrl
                            });
                        }
                    }
                }
            }
        }
        catch { }

        // Fallback default GitHub streaming tracks if playlist.json was missing or empty
        if (_tracks.Count == 0)
        {
            _tracks = new List<AudioTrack>
            {
                new() { Id = "track-nature", Title = "Звуки природы", Url = "https://raw.githubusercontent.com/remvze/moodist/main/public/sounds/animals/birds.mp3", DurationSeconds = 128 },
                new() { Id = "track-campfire", Title = "Костёр", Url = "https://raw.githubusercontent.com/remvze/moodist/main/public/sounds/nature/campfire.mp3", DurationSeconds = 202 },
                new() { Id = "track-river", Title = "Река", Url = "https://raw.githubusercontent.com/remvze/moodist/main/public/sounds/nature/river.mp3", DurationSeconds = 243 },
                new() { Id = "track-waterfall", Title = "Водопад", Url = "https://raw.githubusercontent.com/remvze/moodist/main/public/sounds/nature/waterfall.mp3", DurationSeconds = 34 },
                new() { Id = "track-metropolis", Title = "Мегаполис", Url = "https://raw.githubusercontent.com/remvze/moodist/main/public/sounds/urban/busy-street.mp3", DurationSeconds = 145 },
                new() { Id = "track-office", Title = "Офисный шум", Url = "https://raw.githubusercontent.com/remvze/moodist/main/public/sounds/places/office.mp3", DurationSeconds = 122 },
                new() { Id = "track-brown-noise", Title = "Коричневый шум", Url = "https://raw.githubusercontent.com/remvze/moodist/main/public/sounds/noise/brown-noise.wav", DurationSeconds = 900 }
            };
        }

        _currentIndex = 0;
    }

    public async Task SyncRemotePlaylistAsync(string? customUrl = null)
    {
        try
        {
            string url = !string.IsNullOrEmpty(customUrl) ? customUrl : DefaultPlaylistUrl;
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
                            Url = trackUrl
                        });
                    }
                }

                if (remoteTracks.Count > 0)
                {
                    _tracks = remoteTracks;
                    PlaylistUpdated?.Invoke();
                }
            }
        }
        catch
        {
            // Offline or repository raw file not yet published; initial playlist remains active
        }
    }

    private void PrepareTrack(int index)
    {
        if (index < 0 || index >= _tracks.Count) return;
        var track = _tracks[index];

        if (!string.IsNullOrEmpty(track.Url))
        {
            try
            {
                _mediaPlayer.Open(new Uri(track.Url));
            }
            catch { }
        }
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

    private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        // Loop current track seamlessly and continuously without stopping
        _virtualPosition = 0;
        _mediaPlayer.Position = TimeSpan.Zero;
        _mediaPlayer.Play();
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

        if (!string.IsNullOrEmpty(track.Url))
        {
            if (_mediaPlayer.Source == null || !_mediaPlayer.Source.AbsoluteUri.Equals(track.Url, StringComparison.OrdinalIgnoreCase))
            {
                _mediaPlayer.Open(new Uri(track.Url));
            }
        }

        _lastTickTime = DateTime.UtcNow;
        _mediaPlayer.Volume = 0.0;
        _mediaPlayer.Play();
        _timer.Start();
        StateChanged?.Invoke(_isPlaying);
        TrackChanged?.Invoke(track);

        double targetVol = _isMuted ? 0.0 : _volume;
        await FadeVolumeAsync(0.0, targetVol, 300, ct);
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
        PrepareTrack(_currentIndex);

        var track = _tracks[_currentIndex];
        TrackChanged?.Invoke(track);
        PositionChanged?.Invoke(0, track.DurationSeconds);

        if (autoPlay)
        {
            _isPlaying = true;
            _mediaPlayer.Volume = 0.0;

            if (!string.IsNullOrEmpty(track.Url))
            {
                _mediaPlayer.Open(new Uri(track.Url));
                _mediaPlayer.Play();
            }

            _timer.Start();
            StateChanged?.Invoke(_isPlaying);
            await FadeVolumeAsync(0.0, targetVol, 420, ct);
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
