using System.Globalization;

namespace Yeetle.Infrastructure.Data;

// Dates are stored as text that sorts in time order. Recording dates are local wall-clock times,
// as the user sees them in filenames.
public static class DbTime
{
    private const string Format = "yyyy-MM-dd HH:mm:ss.fff";

    public static string Write(DateTime time) => time.ToString(Format, CultureInfo.InvariantCulture);

    public static DateTime Read(string text) =>
        DateTime.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    // File times carry more precision than is stored; compare them at the stored precision.
    public static DateTime Truncate(DateTime time) => Read(Write(time));
}
