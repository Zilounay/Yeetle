namespace Yeetle.App.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7.9, "0:07")]
    [InlineData(754, "12:34")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void Formats_clip_times(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Clip(TimeSpan.FromSeconds(seconds)));
}
