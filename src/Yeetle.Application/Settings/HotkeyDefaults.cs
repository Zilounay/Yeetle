namespace Yeetle.Application.Settings;

// The default keys (spec §14).
//
// A key is written one of two ways:
// - a character, like "M", "[" or "Ctrl+Z": the key that types it (or carries that letter) on
//   the current layout;
// - a key position named as on a US keyboard, like "Space", "ArrowLeft" or "Digit1": that
//   physical key whatever the layout, so "Digit1" is the number row's first key on AZERTY too.
// Modifiers come first, joined with "+": "Shift+ArrowRight", "Ctrl+ArrowLeft".
public static class HotkeyDefaults
{
    public static Dictionary<string, List<string>> Create() => new()
    {
        [HotkeyActions.Keep] = ["Enter"],
        [HotkeyActions.Yeet] = ["Delete"],
        // Letters with Ctrl follow the key labels, so Ctrl+Z is Z on AZERTY too.
        [HotkeyActions.Rescan] = ["Ctrl+R"],
        [HotkeyActions.Undo] = ["Ctrl+Z"],
        [HotkeyActions.StopSession] = ["Escape"],
        [HotkeyActions.TypeTag] = ["T"],
        // M is Mute, as in VLC (spec §14).
        [HotkeyActions.AddMarker] = ["K"],
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
        [HotkeyActions.NextFrame] = ["E"],
        // Replaces full screen, as in VLC (spec §7: theatre mode, no full screen).
        [HotkeyActions.TheatreMode] = ["F"],
        [HotkeyActions.Slower] = ["NumPadSubtract"],
        [HotkeyActions.Faster] = ["NumPadAdd"],
        // No default key: the number row and F-keys belong to tags (spec §14).
        [HotkeyActions.NormalSpeed] = [],
        [HotkeyActions.Speed(1)] = [],
        [HotkeyActions.Speed(2)] = [],
        [HotkeyActions.Speed(3)] = [],
        [HotkeyActions.Speed(4)] = [],
        [HotkeyActions.Speed(5)] = [],
        [HotkeyActions.Speed(6)] = [],
    };
}

// Action names as written in the settings file.
public static class HotkeyActions
{
    public const string Keep = "Keep";
    public const string Yeet = "Yeet";
    public const string Rescan = "Rescan";
    public const string Undo = "Undo";
    public const string StopSession = "StopSession";
    public const string TypeTag = "TypeTag";
    public const string AddMarker = "AddMarker";
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
    public const string NextFrame = "NextFrame";
    public const string TheatreMode = "TheatreMode";

    // The n-th speed of PlaybackSpeeds.All, from 1.
    public static string Speed(int number) => $"Speed{number}";
}
