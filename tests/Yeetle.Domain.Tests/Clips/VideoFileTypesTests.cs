using Yeetle.Domain.Clips;

namespace Yeetle.Domain.Tests.Clips;

public class VideoFileTypesTests
{
    [Theory]
    [InlineData(@"D:\clips\2026-10-11 22-15-36.mp4")]
    [InlineData("clip.MKV")]
    [InlineData("clip.webm")]
    public void Recognises_the_default_video_extensions(string path) =>
        Assert.True(VideoFileTypes.IsVideo(path, VideoFileTypes.DefaultExtensions));

    [Theory]
    [InlineData("song.mp3")]
    [InlineData("thumbnail.png")]
    [InlineData("mp4")]
    [InlineData("clip.mp4.part")]
    public void Ignores_other_files(string path) =>
        Assert.False(VideoFileTypes.IsVideo(path, VideoFileTypes.DefaultExtensions));
}
