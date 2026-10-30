using Avalonia.Input;
using Yeetle.App.Input;
using Yeetle.Application.Settings;

namespace Yeetle.App.Tests.Input;

public class HotkeyTests
{
    [Fact]
    public void A_key_position_matches_whatever_the_layout_types()
    {
        var digit1 = Hotkey.Parse("Digit1")!;

        Assert.True(digit1.Matches(KeyModifiers.None, PhysicalKey.Digit1, "&")); // AZERTY
        Assert.True(digit1.Matches(KeyModifiers.None, PhysicalKey.Digit1, "1")); // QWERTY
        Assert.False(digit1.Matches(KeyModifiers.Shift, PhysicalKey.Digit1, "1"));
    }

    [Fact]
    public void A_character_matches_the_key_that_types_it()
    {
        var mute = Hotkey.Parse("M")!;

        Assert.True(mute.Matches(KeyModifiers.None, PhysicalKey.Semicolon, "m")); // M on AZERTY
        Assert.False(mute.Matches(KeyModifiers.None, PhysicalKey.M, ",")); // the QWERTY M position on AZERTY
        Assert.False(mute.Matches(KeyModifiers.Control, PhysicalKey.Semicolon, "m"));
    }

    [Fact]
    public void A_character_typed_with_AltGr_still_matches()
    {
        var slower = Hotkey.Parse("[")!;

        Assert.True(slower.Matches(KeyModifiers.Control | KeyModifiers.Alt, PhysicalKey.Digit5, "["));
    }

    [Theory]
    [InlineData("Shift+ArrowRight", KeyModifiers.Shift, PhysicalKey.ArrowRight)]
    [InlineData("ctrl + arrowleft", KeyModifiers.Control, PhysicalKey.ArrowLeft)]
    [InlineData("Space", KeyModifiers.None, PhysicalKey.Space)]
    public void Parses_modifiers_and_key_positions(string text, KeyModifiers modifiers, PhysicalKey position)
    {
        var key = Hotkey.Parse(text)!;

        Assert.Equal(modifiers, key.Modifiers);
        Assert.Equal(position, key.Position);
    }

    [Fact]
    public void Parses_the_plus_key() => Assert.Equal("+", Hotkey.Parse("Ctrl++")!.Character);

    [Fact]
    public void Rejects_an_empty_key() => Assert.Null(Hotkey.Parse(string.Empty));

    [Fact]
    public void A_letter_position_is_written_as_on_the_web()
    {
        var rescan = Hotkey.Parse("Ctrl+KeyR")!;

        Assert.Equal(PhysicalKey.R, rescan.Position);
        Assert.True(rescan.Matches(KeyModifiers.Control, PhysicalKey.R, null));
    }

    [Theory]
    [InlineData("Hyper+A")]
    [InlineData("NotAKey")]
    [InlineData("12")]
    public void Rejects_what_is_not_a_key(string text) => Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void The_default_keys_all_parse_and_are_used_once()
    {
        var defaults = HotkeyDefaults.Create();

        var map = new HotkeyMap(defaults, defaults.Keys.ToHashSet());

        Assert.Empty(map.Problems);
        Assert.Equal(HotkeyActions.Speed(1), map.Find(KeyModifiers.None, PhysicalKey.Digit1, "&"));
        Assert.Equal(HotkeyActions.NextClip, map.Find(KeyModifiers.Shift, PhysicalKey.ArrowRight, null));
        Assert.Equal(HotkeyActions.SeekForward, map.Find(KeyModifiers.None, PhysicalKey.ArrowRight, null));
    }

    [Fact]
    public void Reports_unknown_actions_bad_keys_and_keys_used_twice()
    {
        var settings = new Dictionary<string, List<string>>
        {
            ["Mute"] = ["M", "Banana"],
            ["NextAudioTrack"] = ["M"],
            ["Fly"] = ["F"],
        };

        var map = new HotkeyMap(settings, new HashSet<string> { "Mute", "NextAudioTrack" });

        Assert.Equal(3, map.Problems.Count);
        Assert.Equal("Mute", map.Find(KeyModifiers.None, PhysicalKey.Semicolon, "m"));
    }
}
