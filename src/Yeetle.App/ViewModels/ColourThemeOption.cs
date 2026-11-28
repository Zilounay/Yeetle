using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Yeetle.App.Resources;
using Yeetle.App.Theming;
using Yeetle.Application.Settings;

namespace Yeetle.App.ViewModels;

// One card of Settings › Appearance › Colour theme (#69): a small preview of the theme on its own
// background, its name and a line about it.
public sealed partial class ColourThemeOption : ObservableObject
{
    // The preview's two lights, as on the canvas: top left and top right.
    private static readonly RelativePoint FirstLight = new(0.2, 0.2, RelativeUnit.Relative);
    private static readonly RelativePoint SecondLight = new(0.9, 0.3, RelativeUnit.Relative);
    private const double FadeAt = 0.7;

    private readonly Palette _palette;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    public ColourThemeOption(Palette palette, bool isCurrent)
    {
        _palette = palette;
        _isCurrent = isCurrent;
        PrimaryBrush = new SolidColorBrush(palette.Primary);
        LightBrush = new SolidColorBrush(palette.PrimaryLight);
        AccentBrush = new SolidColorBrush(palette.Accent);
        PageBrush = new SolidColorBrush(palette.Page);
        FirstGlowBrush = Glow(palette.Glow1, FirstLight);
        SecondGlowBrush = Glow(palette.Glow2, SecondLight);
        Relocalize();
    }

    public ColourTheme Theme => _palette.Theme;

    public IBrush PrimaryBrush { get; }

    public IBrush LightBrush { get; }

    public IBrush AccentBrush { get; }

    public IBrush PageBrush { get; }

    public IBrush FirstGlowBrush { get; }

    public IBrush SecondGlowBrush { get; }

    public void Relocalize()
    {
        (Name, Note) = _palette.Theme switch
        {
            ColourTheme.NeonViolet => (Strings.ThemeNeonViolet, Strings.ThemeNeonVioletNote),
            ColourTheme.Ember => (Strings.ThemeEmber, Strings.ThemeEmberNote),
            _ => (Strings.ThemeSukiBlue, Strings.ThemeSukiBlueNote),
        };
    }

    private static IBrush Glow(Color colour, RelativePoint centre) => new RadialGradientBrush
    {
        Center = centre,
        GradientOrigin = centre,
        GradientStops =
        {
            new GradientStop(colour, 0),
            new GradientStop(Colors.Transparent, FadeAt),
        },
    };
}
