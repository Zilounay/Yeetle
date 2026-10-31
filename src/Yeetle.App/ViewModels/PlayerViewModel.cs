using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yeetle.App.Input;
using Yeetle.App.Resources;
using Yeetle.App.Services;
using Yeetle.Application.Clips;
using Yeetle.Application.Playback;
using Yeetle.Application.Settings;

namespace Yeetle.App.ViewModels;

// The player: the clips of one folder, one at a time, with playback and audio controls.
public sealed partial class PlayerViewModel : ViewModelBase, IDisposable
{
    // How often the seek bar and time follow playback: often enough to look smooth.
    private static readonly TimeSpan PositionRefresh = TimeSpan.FromMilliseconds(100);
    private const int MinTracksToCycle = 2;
    private const string ProblemSeparator = " · ";

    private readonly IClipPlayer _player;
    private readonly ClipNavigator _navigator;
    private readonly IClipFolderReader _folderReader;
    private readonly IFolderPicker _folderPicker;
    private readonly DispatcherTimer _positionTimer;
    private readonly PlayerSettings _settings;
    private string? _settingsProblem; // stays visible until the settings file is fixed

    // True while values are copied from the player, so their change handlers don't send them back.
    private bool _syncing;

    [ObservableProperty]
    private string _clipName = string.Empty;

    [ObservableProperty]
    private string _clipCounter = string.Empty;

    [ObservableProperty]
    private string _message = Strings.NoFolderOpen;

    // What the last scan found, e.g. files still recording.
    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private string _positionText = TimeFormat.Clip(TimeSpan.Zero);

    [ObservableProperty]
    private string _durationText = TimeFormat.Clip(TimeSpan.Zero);

    [ObservableProperty]
    private int _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteLabel))]
    private bool _isMuted;

    [ObservableProperty]
    private IReadOnlyList<AudioTrack> _audioTracks = [];

    [ObservableProperty]
    private AudioTrack? _selectedAudioTrack;

    [ObservableProperty]
    private IReadOnlyList<AudioDevice> _audioDevices = [];

    [ObservableProperty]
    private AudioDevice? _selectedAudioDevice;

    public PlayerViewModel(IClipPlayer player, ClipNavigator navigator, IClipFolderReader folderReader,
        IFolderPicker folderPicker, LoadedSettings settings)
    {
        _player = player;
        _navigator = navigator;
        _folderReader = folderReader;
        _folderPicker = folderPicker;
        _settings = settings.Values.Player;
        Speeds = PlaybackSpeeds.All.Select(s => new SpeedOptionViewModel(s)).ToList();
        Actions = CreateActions();

        _player.StateChanged += OnPlayerStateChanged;
        _player.AudioChanged += OnPlayerAudioChanged;
        _navigator.CurrentChanged += OnCurrentClipChanged;
        _player.SetSpeed(PlaybackSpeeds.All.Contains(_settings.DefaultSpeed) ? _settings.DefaultSpeed : PlaybackSpeeds.Normal);
        _player.SetVolume(_settings.DefaultVolume);
        _positionTimer = new DispatcherTimer(PositionRefresh, DispatcherPriority.Background, (_, _) => SyncPosition());
        _positionTimer.Start();
        SyncState();
        SyncAudio();
    }

    public IReadOnlyList<IVideoScreen> Screens => _player.Screens;

    // What each player hotkey action does, by its name in the settings file.
    public IReadOnlyDictionary<string, ICommand> Actions { get; }

    public IReadOnlyList<SpeedOptionViewModel> Speeds { get; }

    public string PlayPauseLabel => IsPlaying ? Strings.Pause : Strings.Play;

    public string MuteLabel => IsMuted ? Strings.Unmute : Strings.Mute;

    // Set by the view while the user drags the seek bar, so playback doesn't move it meanwhile.
    public bool IsScrubbing { get; set; }

    public async Task OpenFolderAsync(string folder)
    {
        IReadOnlyList<string> clips;
        try
        {
            clips = await _folderReader.ListClipsAsync(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = string.Format(Strings.FolderUnreadable, folder, e.Message);
            return;
        }
        Message = clips.Count == 0 ? string.Format(Strings.NoClipsInFolder, folder) : _settingsProblem ?? string.Empty;
        _navigator.Load(clips);
    }

    public void Stop()
    {
        _positionTimer.Stop();
        _player.Stop();
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _player.StateChanged -= OnPlayerStateChanged;
        _player.AudioChanged -= OnPlayerAudioChanged;
        _navigator.CurrentChanged -= OnCurrentClipChanged;
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        if (await _folderPicker.PickFolderAsync(Strings.PickFolderTitle) is { } folder)
            await OpenFolderAsync(folder);
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => _navigator.Next();

    private bool CanGoNext() => _navigator.CanGoNext;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => _navigator.Previous();

    private bool CanGoPrevious() => _navigator.CanGoPrevious;

    // Keep and Yeet move on to the next clip (spec §5). The decision itself is recorded once
    // review sessions exist (#14); until then they only move on, like Skip.
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Keep() => _navigator.Next();

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Yeet() => _navigator.Next();

    [RelayCommand]
    private void TogglePause() => _player.TogglePause();

    [RelayCommand]
    private void ToggleMute() => _player.ToggleMute();

    [RelayCommand]
    private void SetSpeed(double speed) => _player.SetSpeed(speed);

    [RelayCommand]
    private void Faster() => _player.SetSpeed(PlaybackSpeeds.Faster(_player.Speed));

    [RelayCommand]
    private void Slower() => _player.SetSpeed(PlaybackSpeeds.Slower(_player.Speed));

    [RelayCommand]
    private void NormalSpeed() => _player.SetSpeed(PlaybackSpeeds.Normal);

    private Dictionary<string, ICommand> CreateActions()
    {
        var actions = new Dictionary<string, ICommand>
        {
            [HotkeyActions.Keep] = KeepCommand,
            [HotkeyActions.Yeet] = YeetCommand,
            [HotkeyActions.TogglePause] = TogglePauseCommand,
            [HotkeyActions.NextClip] = NextCommand,
            [HotkeyActions.PreviousClip] = PreviousCommand,
            [HotkeyActions.SeekForward] = new RelayCommand(() => SeekBy(_settings.SeekSeconds)),
            [HotkeyActions.SeekBack] = new RelayCommand(() => SeekBy(-_settings.SeekSeconds)),
            [HotkeyActions.LongSeekForward] = new RelayCommand(() => SeekBy(_settings.LongSeekSeconds)),
            [HotkeyActions.LongSeekBack] = new RelayCommand(() => SeekBy(-_settings.LongSeekSeconds)),
            [HotkeyActions.VolumeUp] = new RelayCommand(() => _player.SetVolume(_player.Volume + _settings.VolumeStep)),
            [HotkeyActions.VolumeDown] = new RelayCommand(() => _player.SetVolume(_player.Volume - _settings.VolumeStep)),
            [HotkeyActions.Mute] = ToggleMuteCommand,
            [HotkeyActions.NextAudioTrack] = CycleAudioTrackCommand,
            [HotkeyActions.Slower] = SlowerCommand,
            [HotkeyActions.Faster] = FasterCommand,
            [HotkeyActions.NormalSpeed] = NormalSpeedCommand,
        };
        foreach (var (speed, number) in PlaybackSpeeds.Numbered)
            actions[HotkeyActions.Speed(number)] = new RelayCommand(() => _player.SetSpeed(speed));
        return actions;
    }

    private void SeekBy(double seconds)
    {
        if (_navigator.Current is null)
            return;
        var target = _player.Position + TimeSpan.FromSeconds(seconds);
        _player.Seek(target < TimeSpan.Zero ? TimeSpan.Zero : target > _player.Duration ? _player.Duration : target);
        SyncPosition();
    }

    // The settings file could not be fully read; shown until it is fixed.
    public void ShowSettingsProblem(string location, IEnumerable<string> problems) =>
        Message = _settingsProblem = string.Format(Strings.SettingsProblem, location, string.Join(ProblemSeparator, problems));

    [RelayCommand]
    private void CycleAudioTrack()
    {
        var tracks = AudioTracks.ToList();
        if (tracks.Count < MinTracksToCycle)
            return;
        // FindIndex gives -1 when no track is selected, so the first track comes next; after the
        // last track comes the first again.
        var current = tracks.FindIndex(t => t.Id == _player.AudioTrackId);
        _player.SetAudioTrack(tracks[(current + 1) % tracks.Count].Id);
    }

    partial void OnPositionSecondsChanged(double value)
    {
        PositionText = TimeFormat.Clip(TimeSpan.FromSeconds(value));
        if (!_syncing)
            _player.Seek(TimeSpan.FromSeconds(value));
    }

    partial void OnVolumeChanged(int value)
    {
        if (!_syncing)
            _player.SetVolume(value);
    }

    partial void OnSelectedAudioTrackChanged(AudioTrack? value)
    {
        if (!_syncing && value is not null && value.Id != _player.AudioTrackId)
            _player.SetAudioTrack(value.Id);
    }

    partial void OnSelectedAudioDeviceChanged(AudioDevice? value)
    {
        if (!_syncing && value is not null && value.Id != _player.AudioDeviceId)
            _player.SetAudioDevice(value.Id);
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(SyncState);

    private void OnPlayerAudioChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(SyncAudio);

    private void OnCurrentClipChanged(object? sender, EventArgs e)
    {
        ClipName = Path.GetFileName(_navigator.Current) ?? string.Empty;
        ClipCounter = _navigator.Current is not null
            ? string.Format(Strings.ClipCounterFormat, _navigator.Number, _navigator.Clips.Count)
            : string.Empty;
        NextCommand.NotifyCanExecuteChanged();
        KeepCommand.NotifyCanExecuteChanged();
        YeetCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        SyncPosition();
    }

    private void SyncState()
    {
        var state = _player.State;
        IsPlaying = state == PlaybackState.Playing;
        if (state == PlaybackState.Error)
            Message = Strings.PlaybackError;
        else if (state == PlaybackState.Playing && Message == Strings.PlaybackError)
            Message = string.Empty;
        foreach (var speed in Speeds)
            speed.IsCurrent = speed.Speed.Equals(_player.Speed);
    }

    private void SyncAudio() => Sync(() =>
    {
        Volume = _player.Volume;
        IsMuted = _player.IsMuted;
        if (!AudioTracks.SequenceEqual(_player.AudioTracks))
            AudioTracks = _player.AudioTracks;
        SelectedAudioTrack = AudioTracks.FirstOrDefault(t => t.Id == _player.AudioTrackId);
        if (!AudioDevices.SequenceEqual(_player.AudioDevices))
            AudioDevices = _player.AudioDevices;
        SelectedAudioDevice = AudioDevices.FirstOrDefault(d => d.Id == _player.AudioDeviceId);
    });

    private void SyncPosition()
    {
        if (IsScrubbing)
            return;
        Sync(() =>
        {
            var duration = _player.Duration;
            DurationSeconds = duration.TotalSeconds;
            DurationText = TimeFormat.Clip(duration);
            PositionSeconds = Math.Min(_player.Position.TotalSeconds, DurationSeconds);
        });
    }

    private void Sync(Action copyFromPlayer)
    {
        _syncing = true;
        try
        {
            copyFromPlayer();
        }
        finally
        {
            _syncing = false;
        }
    }
}
