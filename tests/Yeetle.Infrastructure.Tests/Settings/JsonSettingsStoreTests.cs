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
    public void Former_default_keys_are_replaced_and_own_keys_kept()
    {
        Write("""
            {
              "hotkeys": { "Undo": ["Ctrl+KeyZ"], "Speed1": ["Digit1"], "Rescan": ["Ctrl+F5"] }
            }
            """);

        var hotkeys = new JsonSettingsStore(SettingsPath).Load().Values.Hotkeys;

        Assert.Equal(["Ctrl+Z"], hotkeys[HotkeyActions.Undo]);
        Assert.Empty(hotkeys[HotkeyActions.Speed(1)]);
        Assert.Equal(["Ctrl+F5"], hotkeys[HotkeyActions.Rescan]);
    }

    [Fact]
    public void Keys_are_written_readably()
    {
        new JsonSettingsStore(SettingsPath).Load();

        Assert.Contains("\"Ctrl+Z\"", File.ReadAllText(SettingsPath));
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

    [Fact]
    public async Task A_change_is_saved_and_read_back_with_its_new_sections()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var settings = new SettingsService(store.Load(), store, new Data.NullLog());

        await settings.ChangeAsync(s =>
        {
            s.Review.StickyTags = false;
            s.General.CheckForUpdatesAtStartup = false;
            s.LibraryView.LastUsed = new ExplorerFilters { Decisions = [Domain.Clips.Decision.New], States = [] };
        });

        var reread = new JsonSettingsStore(SettingsPath).Load();
        Assert.Null(reread.Problem);
        Assert.False(reread.Values.Review.StickyTags);
        Assert.False(reread.Values.General.CheckForUpdatesAtStartup);
        Assert.Equal([Domain.Clips.Decision.New], reread.Values.LibraryView.LastUsed!.Decisions);
        Assert.Contains("\"New\"", await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_file_from_before_the_new_sections_gets_their_defaults()
    {
        Write("""{ "player": { "seekSeconds": 7 } }""");

        var values = new JsonSettingsStore(SettingsPath).Load().Values;

        Assert.Equal(7, values.Player.SeekSeconds);
        Assert.True(values.Player.PrepareNeighbours);
        Assert.True(values.Review.StickyTags);
        Assert.True(values.General.CheckForUpdatesAtStartup);
        Assert.Equal([Domain.Clips.Decision.Kept], values.LibraryView.DefaultDecisions);
        Assert.Null(values.LibraryView.LastUsed);
    }

    // Appearance arrived with the visual pass (#69).
    [Fact]
    public void A_file_from_before_appearance_gets_suki_blue_and_the_moving_glow()
    {
        Write("""{ "general": { "language": "fr" } }""");

        var appearance = new JsonSettingsStore(SettingsPath).Load().Values.Appearance;

        Assert.Equal(ColourTheme.SukiBlue, appearance.ColourTheme);
        Assert.True(appearance.AnimatedBackground);
        Assert.False(appearance.ReduceMotion);
    }

    [Fact]
    public void The_colour_theme_is_read_by_name()
    {
        Write("""{ "appearance": { "colourTheme": "Ember", "reduceMotion": true } }""");

        var appearance = new JsonSettingsStore(SettingsPath).Load().Values.Appearance;

        Assert.Equal(ColourTheme.Ember, appearance.ColourTheme);
        Assert.True(appearance.ReduceMotion);
    }
}
