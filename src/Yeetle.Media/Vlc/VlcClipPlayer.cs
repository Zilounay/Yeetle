using System.Diagnostics;
using LibVLCSharp.Shared;
using Yeetle.Application.Playback;
using AudioTrack = Yeetle.Application.Playback.AudioTrack;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace Yeetle.Media.Vlc;

// IClipPlayer on LibVLC, drawing into native child windows (player option A, POC #3).
// Three screens stacked in the same place: the active one plays in front, the standby ones hold
// the next and previous clips paused on their first frame. Open brings the matching one forward.
public sealed class VlcClipPlayer : IClipPlayer, IDisposable
{
    // How often the position is read from VLC, for a smooth seek bar.
    private static readonly TimeSpan PositionInterval = TimeSpan.FromMilliseconds(100);
    // Stopping normally takes tens of milliseconds; past this the app closes anyway.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    // A preloaded clip starts within a few milliseconds (POC #3); a clip loaded from scratch
    // took about half a second. Past these the screen is shown anyway.
    private static readonly TimeSpan PreloadedStartTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ColdStartTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PicturePollInterval = TimeSpan.FromMilliseconds(1);

    // VLC media options. VLC caps repeats at 65535, which loops any clip for hours (spec §7: clips
    // loop, there is no auto-advance).
    private const string LoopOption = ":input-repeat=65535";
    private const string StartPausedOption = ":start-paused";

    // VLC's track id for "no track", and the "Disable" entry it lists among audio tracks.
    private const int NoTrack = -1;
    private const long StartOfClip = 0;

    // Screens behind the current one: one for the next clip, one for the previous (spec §7).
    private const int StandbyScreens = 2;

    // Values of the int flags below, set and read with Interlocked.
    private const int Clear = 0;
    private const int Set = 1;

    private readonly LibVLC _vlc;
    private readonly VlcScreen[] _screens;
    private readonly CommandQueue _queue = new();
    private readonly Timer _positionTimer;
    private readonly Lock _gate = new();

    // Changed under _gate only.
    private readonly List<VlcScreen> _standbys;
    private volatile VlcScreen _active;
    private volatile string? _currentClip;
    private volatile PlaybackState _state;
    private long _positionMs;
    private long _durationMs;
    private int _readingPosition;
    private long _seekTargetMs;
    private int _seekQueued;
    private double _speed = PlaybackSpeeds.Normal;
    private volatile int _volume = VolumeLevels.Full;
    private volatile bool _muted;
    private volatile IReadOnlyList<AudioTrack> _tracks = [];
    private volatile int _trackId = NoTrack;
    private volatile IReadOnlyList<AudioDevice> _devices = [];
    private volatile string? _deviceId; // null until the user picks one: VLC uses the default device
    private volatile string? _playingDeviceId; // the device VLC reports, shown until the user picks one
    private bool _disposed;

    public VlcClipPlayer()
    {
        Core.Initialize();
        _vlc = new LibVLC();
        _active = CreateScreen();
        _standbys = [.. Enumerable.Range(0, StandbyScreens).Select(_ => CreateScreen())];
        _screens = [_active, .. _standbys];
        _active.IsActive = true;
        _positionTimer = new Timer(_ => ReadPosition(), null, PositionInterval, PositionInterval);
    }

    public IReadOnlyList<IVideoScreen> Screens => _screens;
    public string? CurrentClip => _currentClip;
    public PlaybackState State => _state;
    public TimeSpan Position => TimeSpan.FromMilliseconds(Interlocked.Read(ref _positionMs));
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Interlocked.Read(ref _durationMs));
    public double Speed => Volatile.Read(ref _speed);
    public int Volume => _volume;
    public bool IsMuted => _muted;
    public IReadOnlyList<AudioTrack> AudioTracks => _tracks;
    public int? AudioTrackId => _trackId != NoTrack ? _trackId : null;
    public IReadOnlyList<AudioDevice> AudioDevices => _devices;
    public string? AudioDeviceId => _deviceId ?? _playingDeviceId;

    public event EventHandler? StateChanged;
    public event EventHandler? AudioChanged;

    // Make before break: the clip starts in the screen behind, which comes to the front only once
    // its picture moves; until then the previous clip stays on screen, so a switch never shows
    // black. With the clip preloaded this takes a few milliseconds; otherwise the previous clip
    // stays up while the new one loads.
    public void Open(string path)
    {
        VlcScreen shown, previous;
        bool preloaded;
        lock (_gate)
        {
            // The screen that prepared this clip, else one holding nothing, else any.
            shown = _standbys.FirstOrDefault(s => s.Clip == path)
                    ?? _standbys.FirstOrDefault(s => s.Clip is null)
                    ?? _standbys[^1];
            previous = _active;
            preloaded = shown.Clip == path && shown.IsReady;
            _standbys.Remove(shown);
            _standbys.Add(previous);
            _active = shown;
            shown.IsActive = true;
            previous.IsActive = false;
            shown.Clip = path;
            previous.Clip = null;
            previous.IsReady = false;
        }
        _currentClip = path;
        Interlocked.Exchange(ref _positionMs, StartOfClip);
        Interlocked.Exchange(ref _durationMs, StartOfClip);

        var speed = Speed;
        _queue.Run(() =>
        {
            var player = shown.Player;
            player.SetRate((float)speed);
            var startTime = preloaded ? player.Time : StartOfClip;
            if (preloaded)
            {
                player.Play(); // resumes the first frame it is paused on
            }
            else
            {
                using var media = CreateMedia(path, startPaused: false);
                player.Play(media);
            }
            var moving = WaitForPicture(player, startTime, preloaded ? PreloadedStartTimeout : ColdStartTimeout);
            // A file VLC cannot read never moves and never gets a length, and looping would retry it
            // without end: it is stopped and reported instead.
            if (!moving && player.Length <= 0 && player.VideoTrackCount <= 0)
            {
                player.Stop();
                if (shown.IsActive)
                    SetState(PlaybackState.Error);
            }
            if (shown.IsActive)
                shown.BringToFront();
            if (!previous.IsActive) // unless a later switch is already bringing it back
                previous.Player.Stop();
        });
    }

    // Clips already held by a standby screen stay there; the others go to screens holding nothing
    // wanted, in the order given.
    public void Prepare(IReadOnlyList<string> paths)
    {
        var loads = new List<(VlcScreen Screen, string Path)>();
        lock (_gate)
        {
            var free = new Queue<VlcScreen>(_standbys.Where(s => s.Clip is null || !paths.Contains(s.Clip)));
            foreach (var path in paths.Where(p => p != _active.Clip && _standbys.All(s => s.Clip != p)))
            {
                if (!free.TryDequeue(out var screen))
                    break;
                screen.Clip = path;
                screen.IsReady = false;
                loads.Add((screen, path));
            }
        }
        // No mute here: a paused player makes no sound, and on Windows VLC's mute applies to the
        // whole app's audio session, which Windows remembers between runs.
        foreach (var (screen, path) in loads)
        {
            _queue.Run(() =>
            {
                using var media = CreateMedia(path, startPaused: true);
                screen.Player.Play(media);
            });
        }
    }

    public void NextFrame()
    {
        var screen = _active;
        _queue.Run(() => screen.Player.NextFrame());
    }

    public void TogglePause()
    {
        var screen = _active;
        _queue.Run(() => screen.Player.Pause());
    }

    // Dragging the seek bar sends many targets; only the latest one is applied, one at a time.
    public void Seek(TimeSpan position)
    {
        var ms = Math.Max(StartOfClip, (long)position.TotalMilliseconds);
        Interlocked.Exchange(ref _seekTargetMs, ms);
        Interlocked.Exchange(ref _positionMs, ms);
        if (Interlocked.Exchange(ref _seekQueued, Set) == Set)
            return;
        var screen = _active;
        _queue.Run(() =>
        {
            Interlocked.Exchange(ref _seekQueued, Clear);
            screen.Player.Time = Interlocked.Read(ref _seekTargetMs);
        });
    }

    public void SetSpeed(double speed)
    {
        Volatile.Write(ref _speed, speed);
        var screen = _active;
        _queue.Run(() => screen.Player.SetRate((float)speed));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, VolumeLevels.Silent, VolumeLevels.Full);
        _volume = volume;
        var screen = _active;
        _queue.Run(() => screen.Player.Volume = volume);
        AudioChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleMute()
    {
        var muted = _muted = !_muted;
        var screen = _active;
        _queue.Run(() => screen.Player.Mute = muted);
        AudioChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAudioTrack(int id)
    {
        _trackId = id;
        var screen = _active;
        _queue.Run(() => screen.Player.SetAudioTrack(id));
        AudioChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAudioDevice(string id)
    {
        _deviceId = id;
        _queue.Run(() =>
        {
            foreach (var screen in _screens)
                screen.Player.SetOutputDevice(id);
        });
        AudioChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop() => _queue.RunAndWait(() =>
    {
        foreach (var screen in _screens)
            screen.Player.Stop();
    }, StopTimeout);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _positionTimer.Dispose();
        Stop();
        _queue.Close(StopTimeout);
        foreach (var screen in _screens)
            screen.Player.Dispose();
        _vlc.Dispose();
    }

    private VlcScreen CreateScreen()
    {
        // VLC must not react to the mouse or keys itself: Yeetle's hotkeys drive the player.
        var screen = new VlcScreen(new MediaPlayer(_vlc) { EnableMouseInput = false, EnableKeyInput = false });
        var player = screen.Player;
        player.Playing += (_, _) =>
        {
            if (!screen.IsActive)
                return;
            SetState(PlaybackState.Playing);
            _queue.Run(() =>
            {
                ApplySettings(screen);
                RefreshAudio(screen);
            });
        };
        player.Paused += (_, _) =>
        {
            if (screen.IsActive)
                SetState(PlaybackState.Paused);
            else
                screen.IsReady = true; // the preloaded clip reached its first frame
        };
        player.EncounteredError += (_, _) =>
        {
            if (screen.IsActive)
                SetState(PlaybackState.Error);
        };
        player.ESAdded += (_, e) =>
        {
            if (e.Type == TrackType.Audio && screen.IsActive)
                _queue.Run(() => RefreshAudio(screen));
        };
        return screen;
    }

    private VlcMedia CreateMedia(string path, bool startPaused)
    {
        var media = new VlcMedia(_vlc, new Uri(path));
        media.AddOption(LoopOption);
        if (startPaused)
            media.AddOption(StartPausedOption);
        return media;
    }

    // Speed, device, volume and mute belong to the player, not to one clip, so they are applied
    // again whenever a clip starts. Applying the mute state also clears a mute Windows kept
    // from an earlier run.
    private void ApplySettings(VlcScreen screen)
    {
        var player = screen.Player;
        player.SetRate((float)Speed);
        if (_deviceId is { } device && player.OutputDevice != device)
            player.SetOutputDevice(device);
        player.Volume = _volume;
        if (player.Mute != _muted)
            player.Mute = _muted;
    }

    // VLC lists output devices only once its audio output exists, i.e. after a clip started.
    private void RefreshAudio(VlcScreen screen)
    {
        var player = screen.Player;
        var tracks = player.AudioTrackDescription
            .Where(t => t.Id != NoTrack) // drops VLC's "Disable" entry
            .Select(t => new AudioTrack(t.Id, t.Name))
            .ToList();
        var trackId = player.AudioTrack;
        var devices = player.AudioOutputDeviceEnum
            .Select(d => new AudioDevice(d.DeviceIdentifier, d.Description))
            .ToList();
        var device = player.OutputDevice;
        if (!screen.IsActive)
            return;
        _tracks = tracks;
        _trackId = trackId;
        _devices = devices;
        _playingDeviceId = device;
        AudioChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReadPosition()
    {
        if (Interlocked.Exchange(ref _readingPosition, Set) == Set)
            return;
        try
        {
            var screen = _active;
            if (screen.Clip is null || Volatile.Read(ref _seekQueued) == Set)
                return;
            var (time, length) = (screen.Player.Time, screen.Player.Length);
            if (!screen.IsActive || time < StartOfClip) // VLC gives -1 while nothing is loaded
                return;
            Interlocked.Exchange(ref _positionMs, time);
            Interlocked.Exchange(ref _durationMs, Math.Max(StartOfClip, length));
        }
        finally
        {
            Interlocked.Exchange(ref _readingPosition, Clear);
        }
    }

    // VLC's clock moving past where it started is the moment the new picture is on screen: the
    // POC's traced screen captures showed both happen together (#3). Its Vout event comes earlier,
    // when the window exists but may still be black. Polls because TimeChanged only fires every
    // few hundred milliseconds. True once the picture moves; false when the timeout passed first.
    private static bool WaitForPicture(MediaPlayer player, long startTime, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (player.Time <= Math.Max(StartOfClip, startTime))
        {
            if (watch.Elapsed >= timeout)
                return false;
            Thread.Sleep(PicturePollInterval);
        }
        return true;
    }

    private void SetState(PlaybackState state)
    {
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
