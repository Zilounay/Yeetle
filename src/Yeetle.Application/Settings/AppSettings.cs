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

    public LibraryViewSettings LibraryView { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

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

    // Loads the clips before and after the current one in the background, so moving on is instant.
    // Off for low-memory PCs (spec §11).
    public bool PrepareNeighbours { get; set; } = true;
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

    // A clip reached with no tags gets the tags of the clip just left (spec §5).
    public bool StickyTags { get; set; } = true;
}

// The Explorer's filters when it opens (spec §11, decided in #20): the defaults chosen in
// Settings until the user changes the filters in the Explorer, then the last ones used.
public sealed class LibraryViewSettings
{
    public List<Decision> DefaultDecisions { get; set; } = [Decision.Kept];

    public List<FileState> DefaultStates { get; set; } =
        [FileState.Present, FileState.Missing, FileState.Offline, FileState.NeedsAttention];

    // Null until the filters are changed in the Explorer; changing the defaults clears it.
    public ExplorerFilters? LastUsed { get; set; }
}

public sealed class ExplorerFilters
{
    public List<Decision> Decisions { get; set; } = [];

    public List<FileState> States { get; set; } = [];
}

public sealed class GeneralSettings
{
    // "en" or "fr"; null follows the Windows display language.
    public string? Language { get; set; }

    // The update check at startup (spec §12); Settings can also check at any time.
    public bool CheckForUpdatesAtStartup { get; set; } = true;
}

// The colours of buttons, selected tags, the seek bar and the background glow (spec §11, decided on
// #69). Keep stays green, Yeet red and Skip grey in every theme.
public enum ColourTheme
{
    SukiBlue,
    NeonViolet,
    Ember,
}

// Spec §11, Appearance (#69). Applies at once, like every setting.
public sealed class AppearanceSettings
{
    public ColourTheme ColourTheme { get; set; } = ColourTheme.SukiBlue;

    // The slow-moving glow behind the pages; off saves graphics power.
    public bool AnimatedBackground { get; set; } = true;

    // Shorter, gentler transitions, no drifting glow and no flash on Keep or Yeet.
    public bool ReduceMotion { get; set; }
}

// How many backups each pool keeps (spec §10).
public sealed class BackupSettings
{
    public int SessionCount { get; set; } = 5;

    public int DailyCount { get; set; } = 7;

    public int SafetyCount { get; set; } = 5;
}
