namespace Yeetle.App;

public static class TimeFormat
{
    private const string MinutesAndSeconds = @"m\:ss";
    private const string HoursMinutesAndSeconds = @"h\:mm\:ss";

    // 0:07, 12:34, or 1:02:03 for clips of an hour or more.
    public static string Clip(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return time.ToString(time >= TimeSpan.FromHours(1) ? HoursMinutesAndSeconds : MinutesAndSeconds);
    }
}
