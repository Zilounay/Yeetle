using Avalonia.Input;

namespace Yeetle.App.Input;

// A key press as hotkeys see it: the modifiers, the physical key position, the key it means on
// the current layout, and the character it types (null when it types none).
public readonly record struct KeyPress(KeyModifiers Modifiers, PhysicalKey Position, Key Key, string? Symbol)
{
    public static KeyPress From(KeyEventArgs e) => new(e.KeyModifiers, e.PhysicalKey, e.Key, e.KeySymbol);
}
