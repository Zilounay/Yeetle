using Avalonia.Media;
using Yeetle.Application.Settings;

namespace Yeetle.App.Theming;

// The colours of one colour theme (Settings › Appearance, #69), from ZiL's canvas "Yeetle · SukiUI
// look". Keep, Yeet and Skip are not here: they are the same in every theme.
public sealed record Palette(
    ColourTheme Theme,
    // Buttons, the current page, the play button, selected tags.
    Color Primary,
    // The seek bar, links, the volume.
    Color PrimaryLight,
    // Markers.
    Color Accent,
    // The page under the glow.
    Color Page,
    // The three soft lights of the background, top left, top right and bottom.
    Color Glow1,
    Color Glow2,
    Color Glow3,
    // The edge of a selected tag.
    Color ChipSelectedBorder,
    // Tag names on the small tag pills of lists.
    Color TagText)
{
    // How strong the theme colour is behind a selected thing, from the canvas.
    private const double SoftAlpha = 0.28;
    private const double ChipAlpha = 0.30;
    private const double RowAlpha = 0.20;
    private const double RowBorderAlpha = 0.55;
    private const double PillAlpha = 0.18;
    private const double AccentSoftAlpha = 0.16;
    // Hovered primary buttons: this much closer to white.
    private const double HoverLighten = 0.14;

    public Color PrimarySoft => WithAlpha(Primary, SoftAlpha);
    public Color ChipSelected => WithAlpha(Primary, ChipAlpha);
    public Color RowSelected => WithAlpha(Primary, RowAlpha);
    public Color RowSelectedBorder => WithAlpha(PrimaryLight, RowBorderAlpha);
    public Color TagPill => WithAlpha(Primary, PillAlpha);
    public Color AccentSoft => WithAlpha(Accent, AccentSoftAlpha);
    public Color PrimaryHover => Lighten(Primary, HoverLighten);

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(alpha * byte.MaxValue), c.R, c.G, c.B);

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (byte.MaxValue - c.R) * amount),
        (byte)(c.G + (byte.MaxValue - c.G) * amount),
        (byte)(c.B + (byte.MaxValue - c.B) * amount));
}

public static class Palettes
{
    // Suki blue, the default: SukiUI's own blue with warm yellow markers.
    public static readonly Palette SukiBlue = new(ColourTheme.SukiBlue,
        Primary: Color.Parse("#0A59F7"), PrimaryLight: Color.Parse("#5B8CFF"), Accent: Color.Parse("#F7B23B"),
        Page: Color.Parse("#0D0F16"),
        Glow1: Color.Parse("#4D0A59F7"), Glow2: Color.Parse("#387C4DFF"), Glow3: Color.Parse("#24D64860"),
        ChipSelectedBorder: Color.Parse("#4F7FFF"), TagText: Color.Parse("#C9D6FF"));

    // Violet with cyan markers.
    public static readonly Palette NeonViolet = new(ColourTheme.NeonViolet,
        Primary: Color.Parse("#7C4DFF"), PrimaryLight: Color.Parse("#A98BFF"), Accent: Color.Parse("#2FD8EE"),
        Page: Color.Parse("#0C0A14"),
        Glow1: Color.Parse("#577C4DFF"), Glow2: Color.Parse("#2E2FD8EE"), Glow3: Color.Parse("#29FF40A0"),
        ChipSelectedBorder: Color.Parse("#9B7BFF"), TagText: Color.Parse("#DCD0FF"));

    // Amber on a warm dark, blue markers.
    public static readonly Palette Ember = new(ColourTheme.Ember,
        Primary: Color.Parse("#D97A06"), PrimaryLight: Color.Parse("#F2A43A"), Accent: Color.Parse("#5FA8FF"),
        Page: Color.Parse("#120E0B"),
        Glow1: Color.Parse("#4DD97A06"), Glow2: Color.Parse("#33B43C28"), Glow3: Color.Parse("#1A5FA8FF"),
        ChipSelectedBorder: Color.Parse("#F0A03A"), TagText: Color.Parse("#FFD9A8"));

    public static IReadOnlyList<Palette> All { get; } = [SukiBlue, NeonViolet, Ember];

    public static Palette Of(ColourTheme theme) => All.FirstOrDefault(p => p.Theme == theme) ?? SukiBlue;
}
