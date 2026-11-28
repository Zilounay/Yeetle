using Avalonia.Media;
using Yeetle.App.Theming;
using Yeetle.Application.Settings;

namespace Yeetle.App.Tests;

// The colour themes of Settings › Appearance (#69).
public class PaletteTests
{
    [Fact]
    public void Every_colour_theme_has_its_own_palette()
    {
        foreach (var theme in Enum.GetValues<ColourTheme>())
            Assert.Equal(theme, Palettes.Of(theme).Theme);
        Assert.Equal(Enum.GetValues<ColourTheme>().Length, Palettes.All.Select(p => p.Primary).Distinct().Count());
    }

    [Fact]
    public void An_unknown_theme_falls_back_to_suki_blue()
    {
        Assert.Same(Palettes.SukiBlue, Palettes.Of((ColourTheme)99));
    }

    [Fact]
    public void Soft_colours_are_the_theme_colour_seen_through()
    {
        var palette = Palettes.SukiBlue;

        Assert.Equal(Color.FromArgb(0x47, 0x0A, 0x59, 0xF7), palette.PrimarySoft);
        Assert.Equal(palette.Primary.R, palette.RowSelected.R);
        Assert.True(palette.PrimaryHover.R > palette.Primary.R && palette.PrimaryHover.B >= palette.Primary.B);
    }
}
