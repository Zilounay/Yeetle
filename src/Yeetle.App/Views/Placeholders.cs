using Avalonia;

namespace Yeetle.App.Views;

// Shapes for the loading placeholders (#78): bars of uneven length look like text about to appear.
public static class Placeholders
{
    // The name bar of each placeholder row in the Explorer list, as the space left at its right.
    public static IReadOnlyList<Thickness> ExplorerRows { get; } =
    [
        new(0, 0, 40, 0), new(0, 0, 120, 0), new(0, 0, 10, 0), new(0, 0, 90, 0),
        new(0, 0, 60, 0), new(0, 0, 140, 0), new(0, 0, 70, 0), new(0, 0, 100, 0),
    ];
}
