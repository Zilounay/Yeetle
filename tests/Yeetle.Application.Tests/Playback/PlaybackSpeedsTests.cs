using Yeetle.Application.Playback;

namespace Yeetle.Application.Tests.Playback;

public class PlaybackSpeedsTests
{
    [Fact]
    public void Offers_the_speeds_from_the_spec() =>
        Assert.Equal([1, 1.25, 1.5, 2, 3, 4], PlaybackSpeeds.All);

    [Theory]
    [InlineData(1, 1.25)]
    [InlineData(2, 3)]
    [InlineData(4, 4)]
    [InlineData(1.1, 1.25)]
    public void Faster_moves_to_the_next_speed_up(double current, double expected) =>
        Assert.Equal(expected, PlaybackSpeeds.Faster(current));

    [Theory]
    [InlineData(4, 3)]
    [InlineData(1.25, 1)]
    [InlineData(1, 1)]
    [InlineData(2.5, 2)]
    public void Slower_moves_to_the_next_speed_down(double current, double expected) =>
        Assert.Equal(expected, PlaybackSpeeds.Slower(current));
}
