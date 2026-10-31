namespace Yeetle.Application.Playback;

// Reads how long a video file plays, without playing it.
public interface IDurationReader
{
    // Null when the file cannot be read as a video. Blocking: callers run it in the background.
    TimeSpan? Read(string path);
}
