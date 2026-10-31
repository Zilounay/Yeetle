using System.Text.Encodings.Web;
using System.Text.Json;
using Yeetle.Application.Settings;

namespace Yeetle.Infrastructure.Settings;

// settings.json, beside the database (AppPaths.Settings, spec §10). The file is meant to be
// edited by hand, so comments and trailing commas are accepted.
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private const string Header = """
        // Yeetle settings. Edit while Yeetle is closed; delete a line to get its default back.
        //
        // Hotkeys: each action lists its keys, written one of two ways:
        // - a character, like "M", "[" or "Ctrl+Z": the key that types it (or carries that letter)
        //   on your keyboard layout;
        // - a key position named as on a US keyboard, like "Space", "ArrowLeft" or "Digit1":
        //   that physical key on any layout ("Digit1" is the number row's first key on AZERTY too).
        // Modifiers come first: "Shift+ArrowRight", "Ctrl+ArrowLeft". An empty list turns an action off.

        """;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // Keys stay readable: "Ctrl+Z", not "Ctrl+Z". The file is never put in a web page.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Default keys that changed before the first release. A file that still has one of them
    // gets the current default, so nobody has to edit it by hand.
    private static readonly Dictionary<string, string[]> FormerDefaults = new()
    {
        [HotkeyActions.Undo] = ["Ctrl+KeyZ"],
        [HotkeyActions.Rescan] = ["Ctrl+KeyR"],
        [HotkeyActions.Slower] = ["["],
        [HotkeyActions.Faster] = ["]"],
        [HotkeyActions.NormalSpeed] = ["="],
        [HotkeyActions.Speed(1)] = ["Digit1"],
        [HotkeyActions.Speed(2)] = ["Digit2"],
        [HotkeyActions.Speed(3)] = ["Digit3"],
        [HotkeyActions.Speed(4)] = ["Digit4"],
        [HotkeyActions.Speed(5)] = ["Digit5"],
        [HotkeyActions.Speed(6)] = ["Digit6"],
    };

    public LoadedSettings Load()
    {
        if (!File.Exists(path))
            return new LoadedSettings(new AppSettings(), path, TryWriteDefaults());
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
            return new LoadedSettings(Complete(settings), path);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LoadedSettings(new AppSettings(), path, e.Message);
        }
    }

    // Fills in what the file leaves out (sections, lists, hotkey actions it does not mention) and
    // replaces former default keys.
    private static AppSettings Complete(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.Player ??= defaults.Player;
        settings.Library ??= defaults.Library;
        settings.Backups ??= defaults.Backups;
        settings.Review ??= defaults.Review;
        settings.Library.VideoExtensions ??= defaults.Library.VideoExtensions;
        settings.Hotkeys ??= [];
        foreach (var (action, keys) in defaults.Hotkeys)
        {
            var current = settings.Hotkeys.GetValueOrDefault(action);
            if (current is null || (FormerDefaults.TryGetValue(action, out var former) && current.SequenceEqual(former)))
                settings.Hotkeys[action] = keys;
        }
        return settings;
    }

    private string? TryWriteDefaults()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Header + JsonSerializer.Serialize(new AppSettings(), Options));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
