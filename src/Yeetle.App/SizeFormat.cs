using System.Globalization;
using Yeetle.App.Resources;

namespace Yeetle.App;

public static class SizeFormat
{
    private const double Step = 1024;
    private const string WholeNumber = "0";
    private const string OneDecimal = "0.0";
    private const string UnitSeparator = " ";
    // From MB up, sizes get one decimal.
    private const int FirstUnitWithDecimal = 2;

    // 512 B, 1.5 MB, 3.2 GB, in the user's number format.
    public static string Bytes(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string[] units = [Strings.UnitBytes, Strings.UnitKilobytes, Strings.UnitMegabytes, Strings.UnitGigabytes, Strings.UnitTerabytes];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= Step && unit < units.Length - 1)
        {
            value /= Step;
            unit++;
        }
        var format = unit < FirstUnitWithDecimal ? WholeNumber : OneDecimal;
        return value.ToString(format, culture) + UnitSeparator + units[unit];
    }
}
