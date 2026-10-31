using LibVLCSharp.Shared;
using Yeetle.Application.Playback;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace Yeetle.Media.Vlc;

// Reads durations with LibVLC, which already ships for playback, so nothing else is needed (spec §4).
public sealed class VlcDurationReader : IDurationReader, IDisposable
{
    // Reading a local file's header takes milliseconds; a broken file must not hold up the others.
    private const int ParseTimeoutMs = 5000;

    private readonly Lazy<LibVLC> _vlc = new(() =>
    {
        Core.Initialize();
        return new LibVLC();
    });

    public TimeSpan? Read(string path)
    {
        using var media = new VlcMedia(_vlc.Value, new Uri(path));
        var status = media.Parse(MediaParseOptions.ParseLocal, ParseTimeoutMs).GetAwaiter().GetResult();
        return status == MediaParsedStatus.Done && media.Duration > 0 ? TimeSpan.FromMilliseconds(media.Duration) : null;
    }

    public void Dispose()
    {
        if (_vlc.IsValueCreated)
            _vlc.Value.Dispose();
    }
}
