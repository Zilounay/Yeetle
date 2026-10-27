namespace Yeetle.Application.Settings;

// The default keys (spec §14, plus speeds on the number row as asked by ZiL).
//
// A key is written one of two ways:
// - a character, like "M", "[" or "=": the key that types it on the current layout;
// - a key position named as on a US keyboard, like "Space", "ArrowLeft" or "Digit1": that
//   physical key whatever the layout, so "Digit1" is the number row's first key on AZERTY too.
// Modifiers come first, joined with "+": "Shift+ArrowRight", "Ctrl+ArrowLeft".
public static class HotkeyDefaults
{
    public static Dictionary<string, List<string>> Create() => new()
    {
        [HotkeyActions.Keep] = ["Enter"],
        [HotkeyActions.Yeet] = ["Delete"],
        [HotkeyActions.TogglePause] = ["Space"],
        [HotkeyActions.NextClip] = ["Shift+ArrowRight"],
        [HotkeyActions.PreviousClip] = ["Shift+ArrowLeft"],
        [HotkeyActions.SeekForward] = ["ArrowRight"],
        [HotkeyActions.SeekBack] = ["ArrowLeft"],
        [HotkeyActions.LongSeekForward] = ["Ctrl+ArrowRight"],
        [HotkeyActions.LongSeekBack] = ["Ctrl+ArrowLeft"],
        [HotkeyActions.VolumeUp] = ["ArrowUp"],
        [HotkeyActions.VolumeDown] = ["ArrowDown"],
        [HotkeyActions.Mute] = ["M"],
        [HotkeyActions.NextAudioTrack] = ["B"],
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
}

// Action names as written in the settings file.
public static class HotkeyActions
{
    public const string Keep = "Keep";
    public const string Yeet = "Yeet";
    public const string TogglePause = "TogglePause";
    public const string NextClip = "NextClip";
    public const string PreviousClip = "PreviousClip";
    public const string SeekForward = "SeekForward";
    public const string SeekBack = "SeekBack";
    public const string LongSeekForward = "LongSeekForward";
    public const string LongSeekBack = "LongSeekBack";
    public const string VolumeUp = "VolumeUp";
    public const string VolumeDown = "VolumeDown";
    public const string Mute = "Mute";
    public const string NextAudioTrack = "NextAudioTrack";
    public const string Slower = "Slower";
    public const string Faster = "Faster";
    public const string NormalSpeed = "NormalSpeed";

    // The n-th speed of PlaybackSpeeds.All, from 1.
    public static string Speed(int number) => $"Speed{number}";
}
