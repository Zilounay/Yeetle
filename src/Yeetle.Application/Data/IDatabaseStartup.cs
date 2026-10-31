namespace Yeetle.Application.Data;

public enum DatabaseState
{
    Ready,
    // Failed its integrity check: the recovery screen offers backups, nothing is reset (spec §10).
    Damaged,
    // Written by a newer Yeetle: left untouched.
    NewerSchema,
    // Could not be opened at all (locked, no permission, disk error).
    Unavailable,
}

public sealed record DatabaseCheck(DatabaseState State, int SchemaVersion, string? Problem = null);

// Opens the library database at startup: checks it, backs it up and upgrades it before anything
// else touches the data (spec §10).
public interface IDatabaseStartup
{
    // Blocking; run it on a background thread.
    DatabaseCheck Prepare();

    // The schema this build writes; an older file is upgraded to it.
    int LatestSchemaVersion { get; }

    string DataFolder { get; }
}
