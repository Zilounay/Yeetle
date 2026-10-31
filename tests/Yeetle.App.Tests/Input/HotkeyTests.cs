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
    [InlineData("Digit1", "1")]
    [InlineData("Digit0", "0")]
    [InlineData("F3", "F3")]
    [InlineData("KeyQ", "Q")]
    [InlineData("Shift+Digit1", "Shift+1")]
    [InlineData("Ctrl++", "Ctrl++")]
    public void Chips_show_short_key_names(string key, string label) => Assert.Equal(label, HotkeyLabels.Short(key));

    [Theory]
    [InlineData(KeyModifiers.None, PhysicalKey.Digit1, "Digit1")]
    [InlineData(KeyModifiers.None, PhysicalKey.Q, "KeyQ")]
    [InlineData(KeyModifiers.None, PhysicalKey.Z, "KeyZ")]
    [InlineData(KeyModifiers.None, PhysicalKey.Minus, "Minus")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, PhysicalKey.F3, "Ctrl+Shift+F3")]
    public void A_pressed_key_is_written_by_position(KeyModifiers modifiers, PhysicalKey key, string text)
    {
        Assert.Equal(text, HotkeyNames.FromKey(modifiers, key));
        Assert.True(Hotkey.Parse(text)!.Matches(modifiers, key, null));
    }

    [Fact]
    public void A_modifier_alone_is_not_a_key() => Assert.Null(HotkeyNames.FromKey(KeyModifiers.Control, PhysicalKey.ControlLeft));

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
        Assert.Equal(HotkeyActions.Faster, map.Find(KeyModifiers.None, PhysicalKey.NumPadAdd, "+"));
        Assert.Null(map.Find(KeyModifiers.None, PhysicalKey.Digit1, "&")); // the number row is for tags
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
