namespace Yeetle.Application.Playback;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Error,
}

public sealed record AudioTrack(int Id, string Name);

public sealed record AudioDevice(string Id, string Name);

public static class VolumeLevels
{
    public const int Silent = 0;

    // The clip's own loudness. Players can go louder, but that distorts the sound.
    public const int Full = 100;
}
