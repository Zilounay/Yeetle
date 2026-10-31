namespace Yeetle.App.Input;

// How a key from the settings file is shown on a chip: "Digit1" as "1", "F3" as "F3".
public static class HotkeyLabels
{
    private const string DigitPrefix = "Digit";
    private const string LetterPrefix = "Key";

    public static string Short(string key)
    {
        foreach (var prefix in new[] { DigitPrefix, LetterPrefix })
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && key.Length > prefix.Length)
                return key[prefix.Length..];
        }
        return key;
    }
}
