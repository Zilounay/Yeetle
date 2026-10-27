using Yeetle.Application.Settings;
using Yeetle.Infrastructure.Settings;

namespace Yeetle.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("yeetle-tests-");

    private string SettingsPath => Path.Combine(_folder.FullName, "Yeetle", "settings.json");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public void A_missing_file_is_created_with_the_defaults()
    {
        var loaded = new JsonSettingsStore(SettingsPath).Load();

        Assert.Null(loaded.Problem);
        Assert.True(File.Exists(SettingsPath));
        var reread = new JsonSettingsStore(SettingsPath).Load();
        Assert.Null(reread.Problem);
        Assert.Equal(new AppSettings().Hotkeys.Count, reread.Values.Hotkeys.Count);
    }

    [Fact]
    public void Values_left_out_of_the_file_take_their_defaults()
    {
        Write("""
            // edited by hand
            {
              "player": { "seekSeconds": 10, },
              "hotkeys": { "Mute": ["F9"] },
            }
            """);

        var settings = new JsonSettingsStore(SettingsPath).Load().Values;

        Assert.Equal(10, settings.Player.SeekSeconds);
        Assert.Equal(30, settings.Player.LongSeekSeconds);
        Assert.Equal(["F9"], settings.Hotkeys[HotkeyActions.Mute]);
        Assert.Equal(["Space"], settings.Hotkeys[HotkeyActions.TogglePause]);
        Assert.Contains(".mkv", settings.Library.VideoExtensions);
    }

    [Fact]
    public void A_broken_file_is_reported_and_left_untouched()
    {
        Write("{ \"player\": ");

        var loaded = new JsonSettingsStore(SettingsPath).Load();

        Assert.NotNull(loaded.Problem);
        Assert.Equal(5, loaded.Values.Player.SeekSeconds);
        Assert.Equal("{ \"player\": ", File.ReadAllText(SettingsPath));
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, json);
    }
}
