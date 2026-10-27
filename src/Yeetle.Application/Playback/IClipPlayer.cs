namespace Yeetle.Application.Playback;

// Plays one clip at a time and keeps another one loaded in the background, so switching to
// it is instant (spec §7). Commands are queued and return at once; their effect shows in the
// properties and events. Clips start at 0:00 and loop.
public interface IClipPlayer
{
    // The video areas the UI must host, one native window each, stacked in the same place. The
    // player keeps the current clip's screen on top; the others hold a preloaded clip.
    IReadOnlyList<IVideoScreen> Screens { get; }

    string? CurrentClip { get; }
    PlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    double Speed { get; }
    int Volume { get; }
    bool IsMuted { get; }
    IReadOnlyList<AudioTrack> AudioTracks { get; }
    int? AudioTrackId { get; }
    IReadOnlyList<AudioDevice> AudioDevices { get; }
    string? AudioDeviceId { get; }

    // Raised on a background thread.
    event EventHandler? StateChanged;

    // Raised on a background thread when the tracks, devices, volume or mute changed.
    event EventHandler? AudioChanged;

    // Plays the clip from 0:00; instant when it is the preloaded one.
    void Open(string path);

    // Loads the clip in a hidden screen, paused on its first frame, ready for Open.
    void Preload(string path);

    void TogglePause();
    void Seek(TimeSpan position);
    void SetSpeed(double speed);
    void SetVolume(int volume);
    void ToggleMute();
    void SetAudioTrack(int id);
    void SetAudioDevice(string id);

    // Stops every screen and waits for it. Call before the video windows are destroyed.
    void Stop();
}
