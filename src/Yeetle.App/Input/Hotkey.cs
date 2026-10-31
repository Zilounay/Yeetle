using Avalonia.Input;

namespace Yeetle.App.Input;

// One key as written in the settings file (see HotkeyDefaults): a character, matched by what the
// key types on the current layout, or a key position, matched whatever the layout.
public sealed class Hotkey
{
    private const char Separator = '+';
    private const string LetterKeyPrefix = "Key";
    private const KeyModifiers AltGr = KeyModifiers.Control | KeyModifiers.Alt;

    private static readonly Dictionary<string, KeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = KeyModifiers.Control,
        ["Control"] = KeyModifiers.Control,
        ["Shift"] = KeyModifiers.Shift,
        ["Alt"] = KeyModifiers.Alt,
        ["Win"] = KeyModifiers.Meta,
        ["Meta"] = KeyModifiers.Meta,
    };

    private Hotkey(KeyModifiers modifiers, PhysicalKey position, string? character)
    {
        Modifiers = modifiers;
        Position = position;
        Character = character;
    }

    public KeyModifiers Modifiers { get; }

    // Set for a key position, PhysicalKey.None for a character.
    public PhysicalKey Position { get; }

    // Set for a character, null for a key position.
    public string? Character { get; }

    public static Hotkey? Parse(string text)
    {
        var (modifierNames, key) = Split(text);
        if (key.Length == 0)
            return null;

        var modifiers = KeyModifiers.None;
        foreach (var name in modifierNames)
        {
            if (!ModifierNames.TryGetValue(name, out var modifier))
                return null;
            modifiers |= modifier;
        }

        if (IsCharacter(key))
            return new Hotkey(modifiers, PhysicalKey.None, key);
        // Letter positions are named as on the web ("KeyR"); Avalonia names them by the letter.
        if (key.Length == LetterKeyPrefix.Length + 1 && key.StartsWith(LetterKeyPrefix, StringComparison.OrdinalIgnoreCase)
                                                    && char.IsAsciiLetter(key[^1]))
            key = key[^1].ToString();
        // Enum.TryParse also accepts numbers ("12"), which are not key names.
        if (key.All(char.IsDigit) || !Enum.TryParse<PhysicalKey>(key, ignoreCase: true, out var position)
                                  || position == PhysicalKey.None)
            return null;
        return new Hotkey(modifiers, position, null);
    }

    public bool Matches(KeyPress press)
    {
        if (Character is null)
            return press.Position == Position && PositionModifiers(press.Modifiers) == Modifiers;
        // A letter or digit pressed with Ctrl types no character, so it is matched by the key that
        // carries it on the current layout: Ctrl+Z is the Z key on AZERTY too, not the QWERTY Z position.
        var sameKey = KeyFor(Character) is { } key && press.Key == key;
        // Typing a character may take Shift or AltGr (Ctrl+Alt), as for [ on AZERTY, so those
        // are part of the character rather than modifiers of their own.
        return (sameKey || string.Equals(press.Symbol, Character, StringComparison.OrdinalIgnoreCase))
               && WithoutTypingModifiers(press.Modifiers) == WithoutTypingModifiers(Modifiers);
    }

    public static bool IsNumberRow(PhysicalKey position) => position is >= PhysicalKey.Digit0 and <= PhysicalKey.Digit9;

    // The number row types its digits with Shift on AZERTY, so its keys match with or without
    // Shift unless the hotkey asks for Shift itself (spec §14: matched by key position).
    private KeyModifiers PositionModifiers(KeyModifiers pressed) =>
        IsNumberRow(Position) && !Modifiers.HasFlag(KeyModifiers.Shift) ? pressed & ~KeyModifiers.Shift : pressed;

    // The virtual key of a letter or digit, which follows the keyboard layout.
    private static Key? KeyFor(string character) => character[0] switch
    {
        var c and >= 'a' and <= 'z' => Key.A + (c - 'a'),
        var c and >= 'A' and <= 'Z' => Key.A + (c - 'A'),
        var c and >= '0' and <= '9' => Key.D0 + (c - '0'),
        _ => null,
    };

    // "Ctrl+Shift+M" gives (["Ctrl", "Shift"], "M"). The key itself may be the separator:
    // "Ctrl++" is Ctrl with the + key.
    private static (IEnumerable<string> Modifiers, string Key) Split(string text)
    {
        var separatorIsKey = text.Length > 1 && text.EndsWith(Separator);
        var body = separatorIsKey ? text[..^1] : text;
        var parts = body.Split(Separator, StringSplitOptions.TrimEntries);
        var key = separatorIsKey ? Separator.ToString() : parts[^1];
        var modifiers = separatorIsKey ? parts.Where(p => p.Length > 0) : parts[..^1];
        return (modifiers, key);
    }

    private static bool IsCharacter(string key) => key.Length == 1;

    private static KeyModifiers WithoutTypingModifiers(KeyModifiers modifiers)
    {
        modifiers &= ~KeyModifiers.Shift;
        return (modifiers & AltGr) == AltGr ? modifiers & ~AltGr : modifiers;
    }
}
