using System.Text.Json;
using Yeetle.Application.Settings;

namespace Yeetle.Infrastructure.Settings;

// settings.json under %LOCALAPPDATA%\Yeetle (spec §10). The file is meant to be edited by hand,
// so comments and trailing commas are accepted.
public sealed class JsonSettingsStore(string path) : ISettingsStore
{
    private const string AppFolderName = "Yeetle";
    private const string FileName = "settings.json";

    private const string Header = """
        // Yeetle settings. Edit while Yeetle is closed; delete a line to get its default back.
        //
        // Hotkeys: each action lists its keys, written one of two ways:
        // - a character, like "M", "[" or "=": the key that types it on your keyboard layout;
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
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, FileName);

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

    // Fills in what the file leaves out: sections, lists and hotkey actions it does not mention.
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
            if (settings.Hotkeys.GetValueOrDefault(action) is null)
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
