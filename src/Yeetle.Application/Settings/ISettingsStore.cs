namespace Yeetle.Application.Settings;

public interface ISettingsStore
{
    // Reads the settings. Missing values take their defaults; a missing file is created with the
    // defaults so the user can edit it. A file that cannot be read is left untouched and the
    // defaults are used, with the reason in Problem.
    LoadedSettings Load();

    // The file's text for these settings. Cheap, so it is taken where the settings change.
    string Format(AppSettings settings);

    // Replaces the file with this text. Blocking: SettingsService runs it in the background.
    void Write(string text);
}

public sealed record LoadedSettings(AppSettings Values, string Location, string? Problem = null);
