using Yeetle.Application.Playback;

namespace Yeetle.Application.Tests.Playback;

public class ClipNavigatorTests
{
    private readonly FakeClipPlayer _player = new();
    private readonly ClipNavigator _navigator;

    public ClipNavigatorTests() => _navigator = new ClipNavigator(_player);

    [Fact]
    public void Load_opens_the_first_clip_and_prepares_the_second()
    {
        _navigator.Load(["a", "b", "c"]);

        Assert.Equal("a", _navigator.Current);
        Assert.Equal(["open a", "prepare b"], _player.Calls);
    }

    [Fact]
    public void Next_opens_the_prepared_clip_and_prepares_both_neighbours()
    {
        _navigator.Load(["a", "b", "c"]);
        _player.Calls.Clear();

        _navigator.Next();

        Assert.Equal(1, _navigator.Index);
        Assert.Equal(["open b", "prepare c,a"], _player.Calls);
    }

    [Fact]
    public void Next_stays_on_the_last_clip()
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
    public void Show_opens_a_clip_only_when_it_changes()
    {
        _navigator.Load(["a", "b", "c"], start: 1);
        _player.Calls.Clear();

        _navigator.Show(1);
        Assert.Empty(_player.Calls);

        _navigator.Show(0);
        Assert.Equal(["open a", "prepare b"], _player.Calls);
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
        public void Prepare(IReadOnlyList<string> paths) => Calls.Add($"prepare {string.Join(",", paths)}");
        public void NextFrame() { }
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
