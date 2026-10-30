namespace Yeetle.App.Input;

// How a key from the settings file is shown on a chip: "Digit1" as "1", "Shift+KeyQ" as "Shift+Q".
public static class HotkeyLabels
{
    private const string DigitPrefix = "Digit";
    private const string LetterPrefix = "Key";
    private const char Separator = '+';

    public static string Short(string text)
    {
        // Only the key itself is shortened, not its modifiers; "Ctrl++" ends with the + key.
        var split = text.Length > 1 ? text.LastIndexOf(Separator, text.Length - 2) : -1;
        var key = text[(split + 1)..];
        return text[..(split + 1)] + ShortKey(key);
    }

    private static string ShortKey(string key)
    {
        foreach (var prefix in new[] { DigitPrefix, LetterPrefix })
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && key.Length > prefix.Length)
                return key[prefix.Length..];
        }
        return key;
    }
}
