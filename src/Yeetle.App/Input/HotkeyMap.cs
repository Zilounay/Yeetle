using Avalonia.Input;
using Yeetle.App.Resources;

namespace Yeetle.App.Input;

// The hotkeys from the settings file, checked against the actions the app knows.
public sealed class HotkeyMap
{
    private readonly List<(Hotkey Key, string Action)> _bindings = [];
    private readonly List<string> _problems = [];

    public HotkeyMap(IReadOnlyDictionary<string, List<string>> settings, IReadOnlySet<string> knownActions)
    {
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (action, keys) in settings)
        {
            if (!knownActions.Contains(action))
            {
                _problems.Add(string.Format(Strings.HotkeyUnknownAction, action));
                continue;
            }
            foreach (var text in keys)
            {
                if (Hotkey.Parse(text) is not { } key)
                    _problems.Add(string.Format(Strings.HotkeyInvalidKey, text, action));
                else if (!used.TryAdd(WithoutSpaces(text), action)) // one key, one action (spec §14)
                    _problems.Add(string.Format(Strings.HotkeyUsedTwice, text, used[WithoutSpaces(text)], action));
                else
                    _bindings.Add((key, action));
            }
        }
    }

    public IReadOnlyList<string> Problems => _problems;

    // "Ctrl + M" and "Ctrl+M" are the same key.
    private static string WithoutSpaces(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    public string? Find(KeyPress press) =>
        _bindings.FirstOrDefault(b => b.Key.Matches(press)).Action;
}
