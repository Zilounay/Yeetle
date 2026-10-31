using System.Diagnostics;
using Yeetle.Media.Vlc;

namespace Yeetle.Media.Tests.Vlc;

// Spec §16: durations from files, including a corrupt one (a clean answer, no hang).
public sealed class VlcDurationReaderTests : IDisposable
{
    // Well within the reader's own timeout: a corrupt file is answered quickly.
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(10);

    private readonly VlcDurationReader _reader = new();
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("yeetle-media-tests-");

    public void Dispose()
    {
        _reader.Dispose();
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void A_corrupt_file_has_no_duration()
    {
        var path = Path.Combine(_folder.FullName, "broken clip.mp4");
        var bytes = new byte[300_000];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        var watch = Stopwatch.StartNew();

        Assert.Null(_reader.Read(path));
        Assert.True(watch.Elapsed < Quick);
    }

    [Fact]
    public void A_missing_file_has_no_duration() => Assert.Null(_reader.Read(Path.Combine(_folder.FullName, "gone.mp4")));
}
