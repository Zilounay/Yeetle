using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Clips;

namespace Yeetle.Infrastructure.Tests.Clips;

public sealed class FileSystemClipFolderReaderTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("yeetle-tests-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public async Task Lists_the_video_files_oldest_first()
    {
        var now = DateTime.UtcNow;
        CreateFile("newest.mp4", now);
        CreateFile("oldest.mkv", now.AddHours(-2));
        CreateFile("middle.mov", now.AddHours(-1));

        var clips = await new FileSystemClipFolderReader(new AppSettings()).ListClipsAsync(_folder.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(["oldest.mkv", "middle.mov", "newest.mp4"], clips.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Skips_other_files_and_subfolders()
    {
        CreateFile("clip.mp4", DateTime.UtcNow);
        CreateFile("notes.txt", DateTime.UtcNow);
        _folder.CreateSubdirectory("sub");
        CreateFile(Path.Combine("sub", "nested.mp4"), DateTime.UtcNow);

        var clips = await new FileSystemClipFolderReader(new AppSettings()).ListClipsAsync(_folder.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_folder.FullName, "clip.mp4"), Assert.Single(clips));
    }

    [Fact]
    public async Task A_missing_folder_throws() =>
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new FileSystemClipFolderReader(new AppSettings()).ListClipsAsync(Path.Combine(_folder.FullName, "gone"),
                TestContext.Current.CancellationToken));

    private void CreateFile(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_folder.FullName, name);
        File.WriteAllBytes(path, []);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}
