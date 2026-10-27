using Yeetle.Application.Playback;

namespace Yeetle.Application.Tests.Playback;

public class ClipNavigatorTests
{
    private readonly FakeClipPlayer _player = new();
    private readonly ClipNavigator _navigator;

    public ClipNavigatorTests() => _navigator = new ClipNavigator(_player);

    [Fact]
    public void Load_opens_the_first_clip_and_preloads_the_second()
    {
        _navigator.Load(["a", "b", "c"]);

        Assert.Equal("a", _navigator.Current);
        Assert.Equal(["open a", "preload b"], _player.Calls);
    }

    [Fact]
    public void Next_opens_the_preloaded_clip_and_preloads_the_one_after()
    {
        _navigator.Load(["a", "b", "c"]);
        _player.Calls.Clear();

        _navigator.Next();

        Assert.Equal(1, _navigator.Index);
        Assert.Equal(["open b", "preload c"], _player.Calls);
    }

    [Fact]
    public void The_last_clip_preloads_nothing_and_next_stays_on_it()
    {
        _navigator.Load(["a", "b"]);
        _navigator.Next();
        _player.Calls.Clear();

        _navigator.Next();

        Assert.False(_navigator.CanGoNext);
        Assert.Equal("b", _navigator.Current);
        Assert.Empty(_player.Calls);
    }

    [Fact]
    public void Previous_goes_back_and_stops_at_the_first_clip()
    {
        _navigator.Load(["a", "b", "c"]);
        _navigator.Next();

        _navigator.Previous();
        _navigator.Previous();

        Assert.Equal("a", _navigator.Current);
        Assert.False(_navigator.CanGoPrevious);
    }

    [Fact]
    public void Loading_an_empty_list_clears_the_current_clip()
    {
        _navigator.Load(["a"]);
        var changes = 0;
        _navigator.CurrentChanged += (_, _) => changes++;

        _navigator.Load([]);

        Assert.Null(_navigator.Current);
        Assert.Equal(-1, _navigator.Index);
        Assert.Equal(1, changes);
    }

    private sealed class FakeClipPlayer : IClipPlayer
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<IVideoScreen> Screens => [];
        public string? CurrentClip => null;
        public PlaybackState State => PlaybackState.Stopped;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public double Speed => PlaybackSpeeds.Normal;
        public int Volume => 100;
        public bool IsMuted => false;
        public IReadOnlyList<AudioTrack> AudioTracks => [];
        public int? AudioTrackId => null;
        public IReadOnlyList<AudioDevice> AudioDevices => [];
        public string? AudioDeviceId => null;

        public event EventHandler? StateChanged { add { } remove { } }
        public event EventHandler? AudioChanged { add { } remove { } }

        public void Open(string path) => Calls.Add($"open {path}");
        public void Preload(string path) => Calls.Add($"preload {path}");
        public void TogglePause() { }
        public void Seek(TimeSpan position) { }
        public void SetSpeed(double speed) { }
        public void SetVolume(int volume) { }
        public void ToggleMute() { }
        public void SetAudioTrack(int id) { }
        public void SetAudioDevice(string id) { }
        public void Stop() { }
    }
}
