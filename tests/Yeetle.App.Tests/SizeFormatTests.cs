using System.Globalization;
using Yeetle.App.Resources;

namespace Yeetle.App.Tests;

public class SizeFormatTests
{
    [Fact]
    public void Formats_sizes_with_the_right_unit()
    {
        var culture = CultureInfo.InvariantCulture;

        Assert.Equal("512 " + Strings.UnitBytes, SizeFormat.Bytes(512, culture));
        Assert.Equal("2 " + Strings.UnitKilobytes, SizeFormat.Bytes(2048, culture));
        Assert.Equal("1.5 " + Strings.UnitMegabytes, SizeFormat.Bytes(1536 * 1024, culture));
        Assert.Equal("3.2 " + Strings.UnitGigabytes, SizeFormat.Bytes(3_435_973_837, culture));
    }

    [Fact]
    public void Uses_the_number_format_of_the_language() =>
        Assert.Equal("1,5 " + Strings.UnitMegabytes, SizeFormat.Bytes(1536 * 1024, CultureInfo.GetCultureInfo("fr-FR")));
}
