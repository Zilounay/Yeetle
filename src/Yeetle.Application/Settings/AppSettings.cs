using Yeetle.Application.Playback;
using Yeetle.Domain.Clips;

namespace Yeetle.Application.Settings;

// Everything the user can configure (spec §11), kept in an editable JSON file (spec §10).
// Every setting has its default here, so a file written by an older Yeetle keeps working.
public sealed class AppSettings
{
    public PlayerSettings Player { get; set; } = new();

    public LibrarySettings Library { get; set; } = new();

    public BackupSettings Backups { get; set; } = new();

    public ReviewSettings Review { get; set; } = new();

    // Action name to the keys that trigger it; see HotkeyDefaults for how keys are written.
    public Dictionary<string, List<string>> Hotkeys { get; set; } = HotkeyDefaults.Create();
}

// Defaults from spec §11.
public sealed class PlayerSettings
{
    public double DefaultSpeed { get; set; } = PlaybackSpeeds.Normal;

    public int DefaultVolume { get; set; } = VolumeLevels.Full;

    public double SeekSeconds { get; set; } = 5;

    public double LongSeekSeconds { get; set; } = 30;

    public int VolumeStep { get; set; } = 5;

    // Starts with the video filling the window, side panels and clip strip hidden.
    public bool TheatreMode { get; set; }
}

public sealed class LibrarySettings
{
    public List<string> VideoExtensions { get; set; } = [.. VideoFileTypes.DefaultExtensions];
}

// Defaults from spec §11.
public sealed class ReviewSettings
{
    // A marker lands this long before K is pressed, since the moment has just happened.
    public double MarkerOffsetSeconds { get; set; } = 1;
}

// How many backups each pool keeps (spec §10).
public sealed class BackupSettings
{
    public int SessionCount { get; set; } = 5;

    public int DailyCount { get; set; } = 7;

    public int SafetyCount { get; set; } = 5;
}
