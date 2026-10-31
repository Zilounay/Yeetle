namespace Yeetle.Domain.Clips;

public readonly record struct ClipId(long Value);

// Where the file stands on disk (spec §3). Set by scanning and file operations, never by the user,
// and never merged with Decision.
public enum FileState
{
    Present,
    Missing,
    Offline,
    Deleted,
    NeedsAttention,
}

// What the user decided about a clip (spec §3). A skipped clip stays New.
public enum Decision
{
    New,
    Kept,
    Yeeted,
}
