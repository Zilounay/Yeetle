using Avalonia.Media;

namespace Yeetle.App.ViewModels;

// The colours a tag can have in the Tag Manager, stored as "#RRGGBB".
public static class TagColours
{
    // Distinct on the dark theme, from the same family as the Keep / Yeet colours.
    public static IReadOnlyList<string> Palette { get; } =
        ["#E5534B", "#F0883E", "#D4A72C", "#57AB5A", "#39C5CF", "#539BF5", "#B083F0", "#E275AD"];

    public static IBrush? Brush(string? colour) =>
        colour is not null && Color.TryParse(colour, out var parsed) ? new SolidColorBrush(parsed) : null;
}
