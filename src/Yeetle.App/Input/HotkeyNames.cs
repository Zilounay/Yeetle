using Avalonia.Input;

namespace Yeetle.App.Input;

// Writes a pressed key the way the settings file and the Tag Manager store it: by key position,
// so a tag key works the same on AZERTY and QWERTY ("Digit1", "KeyQ", "F3", "Ctrl+KeyK").
public static class HotkeyNames
{
    private const string LetterKeyPrefix = "Key";
    private const string Separator = "+";

    private static readonly (KeyModifiers Modifier, string Name)[] ModifierNames =
    [
        (KeyModifiers.Control, "Ctrl"),
        (KeyModifiers.Alt, "Alt"),
        (KeyModifiers.Shift, "Shift"),
        (KeyModifiers.Meta, "Win"),
    ];

    private static readonly HashSet<PhysicalKey> ModifierKeys =
    [
        PhysicalKey.ControlLeft, PhysicalKey.ControlRight, PhysicalKey.ShiftLeft, PhysicalKey.ShiftRight,
        PhysicalKey.AltLeft, PhysicalKey.AltRight, PhysicalKey.MetaLeft, PhysicalKey.MetaRight,
    ];

    // Null for a modifier on its own, which waits for the real key.
    public static string? FromKey(KeyModifiers modifiers, PhysicalKey key)
    {
        if (key == PhysicalKey.None || ModifierKeys.Contains(key))
            return null;
        var name = key is >= PhysicalKey.A and <= PhysicalKey.Z ? LetterKeyPrefix + key : key.ToString();
        var prefix = ModifierNames.Where(m => modifiers.HasFlag(m.Modifier)).Select(m => m.Name + Separator);
        return string.Concat(prefix) + name;
    }
}
