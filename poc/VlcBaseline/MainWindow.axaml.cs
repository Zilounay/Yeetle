using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace VlcBaseline;

// Throwaway POC code for issue #3: everything lives in code-behind on purpose.
//
// Usage: VlcBaseline.exe [folder] [--preload] [--benchmark] [:vlc-option=value ...]
//   --preload    keep the next clip loaded and paused on its first frame in a hidden second view
//   --benchmark  open every clip in turn, save the timings to a CSV next to the exe, then exit
//   :option      extra per-clip VLC options, e.g. :file-caching=50 or :no-audio
public partial class MainWindow : Window
{
    private static readonly float[] Speeds = [1f, 1.25f, 1.5f, 2f, 3f, 4f];
    private static readonly string[] Extensions = [".mp4", ".mkv", ".mov"];
    private static readonly string[] Args = Environment.GetCommandLineArgs();
    private static readonly string[] MediaOptions = Args.Where(a => a.StartsWith(':')).ToArray();
    private static readonly bool Preload = Args.Contains("--preload");

    private readonly LibVLC _vlc;
    private readonly List<Measurement> _results = [];

    // Two players in the same grid cell; only the current one is visible.
    private Slot _current;
    private Slot _next;

    // LibVLC calls must not run on the UI thread or inside LibVLC event handlers,
    // so every player command goes through this serial background chain.
    private Task _chain = Task.CompletedTask;

    private List<string> _clips = [];
    private int _index = -1;
    private float _rate = 1f;
    private Switch? _pending;
    private bool _refreshingTracks;
    private bool _refreshingDevices;
    private bool _startedOnce;
    private bool _scrubbing, _updatingSeekBar, _readingPosition;
    private long _seekTarget;
    private int _seekQueued;
    private string? _deviceId; // null until the user picks an output device

    public MainWindow()
    {
        InitializeComponent();

        Core.Initialize();
        _vlc = new LibVLC();
        _current = CreateSlot(VideoA);
        _next = CreateSlot(VideoB);
        VideoB.IsVisible = false;

        foreach (var speed in Speeds)
        {
            var button = new Button { Content = $"{speed.ToString(CultureInfo.InvariantCulture)}x" };
            button.Click += (_, _) => SetRate(speed);
            SpeedButtons.Items.Add(button);
        }

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel, handledEventsToo: true);

        // The slider's thumb handles pointer events itself, so listen to handled ones too.
        SeekBar.AddHandler(PointerPressedEvent, (_, _) => _scrubbing = true,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => _scrubbing = false,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerCaptureLostEvent, (_, _) => _scrubbing = false,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => UpdateSeekBar()).Start();
        Opened += async (_, _) =>
        {
            if (Args.Length > 1 && Directory.Exists(Args[1]))
                LoadFolder(Args[1]);
            if (Args.Contains("--benchmark"))
            {
                await Task.Delay(1500);
                await RunBenchmark();
                Close();
            }
        };
        Closing += (_, _) =>
        {
            foreach (var slot in new[] { _current, _next })
            {
                slot.Player.Stop();
                slot.Player.Dispose();
            }
            _vlc.Dispose();
        };

        Mode.Text = Preload ? "Mode: preload next" : "Mode: single player";
        Status.Text = $"LibVLC {_vlc.Version} {string.Join(' ', MediaOptions)}";
    }

    private Slot CreateSlot(NativeVideoView view)
    {
        var slot = new Slot(new MediaPlayer(_vlc), view);
        view.MediaPlayer = slot.Player;
        slot.Player.Playing += (_, _) => { if (slot == _current) OnPlaying(); };
        slot.Player.Paused += (_, _) => { if (slot == _next) slot.Ready.TrySetResult(); };
        slot.Player.Vout += (_, e) =>
        {
            if (e.Count > 0 && slot == _current && _pending is { } pending)
                pending.Mark(ref pending.VoutMs);
        };
        slot.Player.ESAdded += (_, e) =>
        {
            if (e.Type == TrackType.Audio && slot == _current)
                RefreshAudioTracks();
        };
        slot.Player.EncounteredError += (_, _) => Post(() => Status.Text = "LibVLC reported an error for a clip.");
        return slot;
    }

    private void Run(Action action) =>
        _chain = _chain.ContinueWith(_ => action(), TaskScheduler.Default);

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    private Media CreateMedia(string path, bool startPaused)
    {
        var media = new Media(_vlc, new Uri(path));
        media.AddOption(":input-repeat=65535"); // spec §7: clips loop, no auto-advance
        if (startPaused)
            media.AddOption(":start-paused");
        foreach (var option in MediaOptions)
            media.AddOption(option);
        return media;
    }

    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Folder with test clips",
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            LoadFolder(path);
    }

    private void LoadFolder(string path)
    {
        _clips = Directory.EnumerateFiles(path)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_clips.Count == 0)
        {
            NowPlaying.Text = $"No .mp4, .mkv or .mov files in {path}";
            return;
        }
        Open(0);
    }

    private int Wrap(int index) => (index + _clips.Count) % _clips.Count;

    private void Open(int index)
    {
        if (_clips.Count == 0)
            return;
        _index = Wrap(index);
        var path = _clips[_index];
        NowPlaying.Text = $"[{_index + 1}/{_clips.Count}] {Path.GetFileName(path)}";

        if (Preload && _next.Path == path && _next.Ready.Task.IsCompleted)
            SwapToNext(path);
        else
            OpenInCurrent(path);

        if (Preload)
            PrepareNext(_clips[Wrap(_index + 1)]);
    }

    // Baseline: load the clip into the visible player.
    private void OpenInCurrent(string path)
    {
        var slot = _current;
        var pending = new Switch(path, preloaded: false, cold: _results.Count == 0);
        _pending = pending;
        slot.Path = path;
        Run(() =>
        {
            using var media = CreateMedia(path, startPaused: false);
            slot.Player.Play(media);
            pending.Mark(ref pending.PlayCallMs);
            _ = Task.Factory.StartNew(() => PollClock(slot, pending, startTime: 0), TaskCreationOptions.LongRunning);
        });
    }

    // Preload: the hidden player already shows the clip's first frame, paused. Show it and resume.
    private void SwapToNext(string path)
    {
        var shown = _next;
        var hidden = _current;
        var pending = new Switch(path, preloaded: true, cold: false);
        _pending = pending;
        (_current, _next) = (shown, hidden);

        shown.View.IsVisible = true;
        hidden.View.IsVisible = false;
        pending.Mark(ref pending.VoutMs); // the picture is on screen once the view is shown

        var rate = _rate;
        Run(() =>
        {
            var startTime = shown.Player.Time;
            shown.Player.SetRate(rate);
            shown.Player.Play(); // resumes the paused first frame
            pending.Mark(ref pending.PlayCallMs);
            _ = Task.Factory.StartNew(() => PollClock(shown, pending, startTime), TaskCreationOptions.LongRunning);
        });
        RefreshAudioTracks();
    }

    private void PrepareNext(string path)
    {
        var slot = _next;
        if (slot.Path == path)
            return;
        slot.Path = path;
        slot.Ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // No Mute here: a paused player makes no sound, and on Windows VLC's mute is the whole
        // app's audio session (shared by both players), which Windows remembers between runs.
        Run(() =>
        {
            using var media = CreateMedia(path, startPaused: true);
            slot.Player.Play(media);
        });
    }

    // TimeChanged only fires every few hundred ms, so poll the clock: the moment it moves past
    // where it started is when playback is really running. Spins on its own thread because
    // Task.Delay is ~15 ms coarse on Windows; it gives up after 5 s.
    private void PollClock(Slot slot, Switch pending, long startTime)
    {
        var deadline = Stopwatch.StartNew();
        while (_pending == pending && deadline.ElapsedMilliseconds < 5000)
        {
            if (slot.Player.Time > Math.Max(0, startTime))
            {
                pending.Mark(ref pending.ClockMs);
                OnSwitched(pending);
                return;
            }
            Thread.Sleep(0);
        }
    }

    private void OnPlaying()
    {
        if (_pending is { } pending)
            pending.Mark(ref pending.PlayingMs);
        var rate = _rate;
        var player = _current.Player;
        var device = _deviceId;
        var firstClip = !_startedOnce;
        _startedOnce = true;
        Run(() =>
        {
            player.SetRate(rate); // re-apply so speed and output device survive clip switches
            if (device is not null && player.OutputDevice != device)
                player.SetOutputDevice(device);
            if (firstClip && player.Mute)
                player.Mute = false; // clear a mute Windows kept from an earlier run
        });
        RefreshAudioTracks();
        RefreshAudioDevices();
    }

    // VLC only lists output devices once its audio output exists, i.e. after a clip started.
    private void RefreshAudioDevices()
    {
        var player = _current.Player;
        Run(() =>
        {
            var devices = player.AudioOutputDeviceEnum
                .Select(d => new DeviceItem(d.DeviceIdentifier, d.Description)).ToList();
            var current = _deviceId ?? player.OutputDevice;
            Post(() =>
            {
                _refreshingDevices = true;
                AudioDevices.ItemsSource = devices;
                AudioDevices.SelectedIndex = devices.FindIndex(d => d.Id == current);
                _refreshingDevices = false;
            });
        });
    }

    private void OnAudioDeviceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDevices || AudioDevices.SelectedItem is not DeviceItem device)
            return;
        _deviceId = device.Id;
        var players = new[] { _current.Player, _next.Player };
        Run(() =>
        {
            foreach (var player in players)
                player.SetOutputDevice(device.Id);
        });
        Status.Text = $"Audio output → {device.Name}";
        ShowAudioState();
    }

    private void ToggleMute()
    {
        var player = _current.Player;
        Run(() => player.ToggleMute());
        ShowAudioState();
    }

    // Shows what VLC is actually doing with audio, to diagnose a silent clip.
    private void ShowAudioState()
    {
        var player = _current.Player;
        Run(() =>
        {
            Thread.Sleep(100); // let a just-sent command land
            var track = player.AudioTrackDescription.FirstOrDefault(t => t.Id == player.AudioTrack);
            var text = $"Audio: track {(player.AudioTrack < 0 ? "none (disabled)" : track.Name)}, " +
                       $"{player.AudioTrackDescription.Count(t => t.Id >= 0)} track(s), volume {player.Volume}, " +
                       $"{(player.Mute ? "MUTED" : "not muted")}, device {player.OutputDevice ?? "default"}";
            Post(() => AudioState.Text = text);
        });
    }

    private void OnSwitched(Switch pending)
    {
        var m = pending.ToMeasurement();
        Post(() =>
        {
            _results.Add(m);
            Status.Text = $"{m.Describe()}\n{Summary(_results)}";
        });
        pending.Done.TrySetResult();
    }

    private void SetRate(float rate)
    {
        _rate = rate;
        var player = _current.Player;
        Run(() => player.SetRate(rate));
        Status.Text = $"Speed {rate.ToString(CultureInfo.InvariantCulture)}x";
    }

    private void RefreshAudioTracks()
    {
        var player = _current.Player;
        Run(() =>
        {
            var tracks = player.AudioTrackDescription.Where(t => t.Id >= 0).ToArray();
            var current = player.AudioTrack;
            Post(() =>
            {
                _refreshingTracks = true;
                AudioTracks.ItemsSource = tracks.Select(t => new TrackItem(t.Id, t.Name)).ToList();
                AudioTracks.SelectedIndex = Array.FindIndex(tracks, t => t.Id == current);
                _refreshingTracks = false;
            });
        });
        ShowAudioState();
    }

    private void OnAudioTrackChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingTracks || AudioTracks.SelectedItem is not TrackItem track)
            return;
        var player = _current.Player;
        var watch = Stopwatch.StartNew();
        Run(() =>
        {
            var ok = player.SetAudioTrack(track.Id);
            var ms = watch.Elapsed.TotalMilliseconds;
            Post(() => Status.Text = $"Audio track → {track.Name}: {(ok ? "ok" : "FAILED")} ({ms:F0} ms call)");
        });
        ShowAudioState();
    }

    private void CycleAudioTrack()
    {
        var count = AudioTracks.ItemCount;
        if (count > 1)
            AudioTracks.SelectedIndex = (AudioTracks.SelectedIndex + 1) % count;
    }

    // Reads the clock off the UI thread and moves the bar, unless the user is dragging it.
    private void UpdateSeekBar()
    {
        if (_scrubbing || _readingPosition)
            return;
        _readingPosition = true;
        var player = _current.Player;
        Task.Run(() =>
        {
            var (time, length) = (player.Time, player.Length);
            Post(() =>
            {
                _readingPosition = false;
                if (_scrubbing || length <= 0)
                    return;
                _updatingSeekBar = true;
                SeekBar.Maximum = length;
                SeekBar.Value = Math.Clamp(time, 0, length);
                _updatingSeekBar = false;
                ClipTime.Text = FormatTime(time);
                ClipLength.Text = FormatTime(length);
            });
        });
    }

    private void OnSeekBarChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeekBar)
            return;
        ClipTime.Text = FormatTime((long)e.NewValue);
        SeekTo((long)e.NewValue);
    }

    // Dragging fires many changes; only the latest target is sent, one seek at a time.
    private void SeekTo(long ms)
    {
        Interlocked.Exchange(ref _seekTarget, ms);
        if (Interlocked.Exchange(ref _seekQueued, 1) == 1)
            return;
        var player = _current.Player;
        Run(() =>
        {
            Interlocked.Exchange(ref _seekQueued, 0);
            player.Time = Interlocked.Read(ref _seekTarget);
        });
    }

    private static string FormatTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private void TogglePause()
    {
        var player = _current.Player;
        Run(() => player.Pause());
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right: Open(_index + 1); break;
            case Key.Left: Open(_index - 1); break;
            case Key.Space: TogglePause(); break;
            case Key.B: CycleAudioTrack(); break;
            case Key.M: ToggleMute(); break;
            case >= Key.D1 and <= Key.D6: SetRate(Speeds[e.Key - Key.D1]); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnPrev(object? sender, RoutedEventArgs e) => Open(_index - 1);
    private void OnNext(object? sender, RoutedEventArgs e) => Open(_index + 1);
    private void OnPause(object? sender, RoutedEventArgs e) => TogglePause();

    private async void OnBenchmark(object? sender, RoutedEventArgs e) => await RunBenchmark();

    // Opens every clip in turn, two or three laps so most switches are warm,
    // and writes the timings to a CSV next to the executable.
    private async Task RunBenchmark()
    {
        if (_clips.Count == 0)
            return;
        var startCount = _results.Count;
        var laps = _clips.Count < 10 ? 3 : 2;
        for (var i = 1; i <= _clips.Count * laps; i++)
        {
            if (Preload)
                await Task.WhenAny(_next.Ready.Task, Task.Delay(3000));
            var rate = Speeds[i % Speeds.Length];
            SetRate(rate); // set before the switch, to check the speed carries over to the new clip
            Open(i);
            var pending = _pending!;
            await Task.WhenAny(pending.Done.Task, Task.Delay(5000));
            await Task.Delay(400);
            if (!pending.Done.Task.IsCompleted)
                _results.Add(pending.ToMeasurement());
            var probe = await ProbeClip();
            _results[^1] = _results[^1] with
            {
                AudioTracks = probe.Tracks,
                RateWanted = rate,
                RateActual = probe.Rate,
                TrackSwitches = probe.TrackSwitches,
            };
        }

        var path = Path.Combine(AppContext.BaseDirectory,
            $"benchmark-{(Preload ? "preload" : "single")}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var csv = new StringBuilder(Measurement.CsvHeader).AppendLine();
        foreach (var m in _results.Skip(startCount))
            csv.AppendLine(m.ToCsv());
        await File.WriteAllTextAsync(path, csv.ToString());
        Status.Text = $"{Summary(_results.Skip(startCount))}\nSaved {path}";
        Console.WriteLine($"{Mode.Text} {string.Join(' ', MediaOptions)}\n{Status.Text}");
    }

    // Reads the clip's audio tracks and actual speed, then switches to every audio track and back
    // to the first, checking each switch took and how long the call blocked.
    private Task<(string Tracks, float Rate, string TrackSwitches)> ProbeClip()
    {
        var player = _current.Player;
        var result = new TaskCompletionSource<(string, float, string)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Run(() =>
        {
            var tracks = player.AudioTrackDescription.Where(t => t.Id >= 0).ToArray();
            var rate = player.Rate;
            var switches = "";
            if (tracks.Length > 1)
            {
                var ok = 0;
                var slowest = 0.0;
                foreach (var track in tracks.Skip(1).Append(tracks[0]))
                {
                    var watch = Stopwatch.StartNew();
                    player.SetAudioTrack(track.Id);
                    slowest = Math.Max(slowest, watch.Elapsed.TotalMilliseconds);
                    Thread.Sleep(150);
                    if (player.AudioTrack == track.Id)
                        ok++;
                }
                switches = $"{ok}/{tracks.Length} ok, slowest call {slowest:F0} ms";
            }
            result.SetResult((string.Join(" | ", tracks.Select(t => t.Name)), rate, switches));
        });
        return result.Task;
    }

    private static string Summary(IEnumerable<Measurement> measurements)
    {
        // Without preload the vout event only means VLC created its window; the traced screen
        // captures showed the new picture appears with the clock, so the clock stands in for it.
        var warm = measurements.Where(m => !m.Cold).ToArray();
        return $"{Stats("Picture shown  ", warm.Select(m => m.Preloaded ? m.VoutMs : m.ClockMs))}\n" +
               $"{Stats("Playing (clock)", warm.Select(m => m.ClockMs))}\n" +
               $"Preloaded: {warm.Count(m => m.Preloaded)}/{warm.Length}";
    }

    private static string Stats(string label, IEnumerable<double?> values)
    {
        var v = values.OfType<double>().Order().ToArray();
        if (v.Length == 0)
            return $"{label}: nothing measured yet.";
        double P(double q) => v[(int)Math.Min(v.Length - 1, Math.Ceiling(q * v.Length) - 1)];
        return $"{label} (n={v.Length}): min {v[0]:F0}, median {P(0.5):F0}, p95 {P(0.95):F0}, " +
               $"max {v[^1]:F0} ms; {v.Count(x => x < 150)}/{v.Length} under 150 ms";
    }

    private sealed class Slot(MediaPlayer player, NativeVideoView view)
    {
        public MediaPlayer Player { get; } = player;
        public NativeVideoView View { get; } = view;
        public string? Path { get; set; }
        public TaskCompletionSource Ready { get; set; } = new();
    }

    private sealed record DeviceItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record TrackItem(int Id, string Name)
    {
        public override string ToString() => $"{Id}: {Name}";
    }

    private sealed class Switch(string path, bool preloaded, bool cold)
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        public double? PlayCallMs, PlayingMs, VoutMs, ClockMs;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Records the elapsed time into the field the first time only.
        public void Mark(ref double? field) => field ??= _watch.Elapsed.TotalMilliseconds;

        public Measurement ToMeasurement() =>
            new(System.IO.Path.GetFileName(path), preloaded, cold, PlayCallMs, PlayingMs, VoutMs, ClockMs);
    }

    private sealed record Measurement(
        string File, bool Preloaded, bool Cold, double? PlayCallMs, double? PlayingMs, double? VoutMs,
        double? ClockMs)
    {
        public const string CsvHeader =
            "file,preloaded,cold,play_call_ms,playing_ms,picture_ms,clock_ms,rate_wanted,rate_actual,audio_tracks,track_switches";

        public string AudioTracks { get; init; } = "";
        public float RateWanted { get; init; }
        public float RateActual { get; init; }
        public string TrackSwitches { get; init; } = "";

        public string ToCsv() =>
            string.Join(',', $"\"{File}\"", Preloaded, Cold, F(PlayCallMs), F(PlayingMs), F(VoutMs), F(ClockMs),
                RateWanted.ToString(CultureInfo.InvariantCulture), RateActual.ToString(CultureInfo.InvariantCulture),
                $"\"{AudioTracks}\"", $"\"{TrackSwitches}\"");

        public string Describe() =>
            $"{File}{(Preloaded ? " (preloaded)" : "")}{(Cold ? " (cold)" : "")}: Play() returned {F(PlayCallMs)}, " +
            $"Playing {F(PlayingMs)}, picture {F(VoutMs)}, clock {F(ClockMs)} ms";

        private static string F(double? v) => v?.ToString("F0", CultureInfo.InvariantCulture) ?? "-";
    }
}
