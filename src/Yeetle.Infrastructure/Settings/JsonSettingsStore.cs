using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yeetle.Application.Settings;

namespace Yeetle.Infrastructure.Settings;

// settings.json, beside the database (AppPaths.Settings, spec §10). The file is meant to be
// edited by hand, so comments and trailing commas are accepted.
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private const string Header = """
        // Yeetle settings. Yeetle writes this file when its Settings change; edit it by hand only while
        // Yeetle is closed. Delete a line to get its default back.
        //
        // Hotkeys: each action lists its keys, written one of two ways:
        // - a character, like "M", "[" or "Ctrl+Z": the key that types it (or carries that letter)
        //   on your keyboard layout;
        // - a key position named as on a US keyboard, like "Space", "ArrowLeft" or "Digit1":
        //   that physical key on any layout ("Digit1" is the number row's first key on AZERTY too).
        // Modifiers come first: "Shift+ArrowRight", "Ctrl+ArrowLeft". An empty list turns an action off.

        """;

    private const string TemporarySuffix = ".new";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        // Keys stay readable: "Ctrl+Z", not "Ctrl+Z". The file is never put in a web page.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Decisions and file states by name ("Kept"), so the file reads well.
        Converters = { new JsonStringEnumConverter() },
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
        settings.LibraryView ??= defaults.LibraryView;
        settings.LibraryView.DefaultDecisions ??= defaults.LibraryView.DefaultDecisions;
        settings.LibraryView.DefaultStates ??= defaults.LibraryView.DefaultStates;
        settings.General ??= defaults.General;
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

    public string Format(AppSettings settings) => Header + JsonSerializer.Serialize(settings, Options);

    // Written beside the file first, then swapped in, so a crash never leaves half a file.
    public void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + TemporarySuffix;
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    private string? TryWriteDefaults()
    {
        try
        {
            Write(Format(new AppSettings()));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
