-- Schema v1 (spec §10). Enum values are stored by name; dates as ISO 8601 text.

CREATE TABLE SourceFolders (
    Id                INTEGER PRIMARY KEY,
    Path              TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    IncludeSubfolders INTEGER NOT NULL DEFAULT 0,
    -- Active, ScanStopped (clips visible, no scanning) or Hidden (clips hidden): spec §4.
    Status            TEXT    NOT NULL DEFAULT 'Active',
    IsOffline         INTEGER NOT NULL DEFAULT 0,
    FirstScanDone     INTEGER NOT NULL DEFAULT 0,
    AddedAt           TEXT    NOT NULL
);

CREATE TABLE Clips (
    Id               INTEGER PRIMARY KEY,
    FolderId         INTEGER NOT NULL REFERENCES SourceFolders (Id),
    -- Relative to the source folder, so a drive-letter change only updates the folder.
    RelativePath     TEXT    NOT NULL COLLATE NOCASE,
    OriginalFileName TEXT    NOT NULL,
    SizeBytes        INTEGER NOT NULL,
    ModifiedAt       TEXT    NOT NULL,
    RecordedAt       TEXT    NOT NULL,
    DurationMs       INTEGER,
    -- File state and decision are separate on purpose (spec §3).
    FileState        TEXT    NOT NULL DEFAULT 'Present',
    Decision         TEXT    NOT NULL DEFAULT 'New',
    -- Set when a session skipped the clip: skipped clips come first in the next session.
    SkippedAt        TEXT,
    AddedAt          TEXT    NOT NULL,
    UNIQUE (FolderId, RelativePath)
);

CREATE INDEX IX_Clips_Decision_RecordedAt ON Clips (Decision, RecordedAt);

-- Tags and boxes in one tree: a box sits at the top or under a tag, a tag sits in a box.
CREATE TABLE Nodes (
    Id        INTEGER PRIMARY KEY,
    Kind      TEXT    NOT NULL CHECK (Kind IN ('Box', 'Tag')),
    ParentId  INTEGER REFERENCES Nodes (Id),
    Name      TEXT    NOT NULL,
    SortOrder INTEGER NOT NULL,
    Colour    TEXT,
    Hotkey    TEXT UNIQUE,
    -- Boxes only.
    Rule      TEXT CHECK (Rule IN ('PickOne', 'PickMany')),
    Required  INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IX_Nodes_ParentId ON Nodes (ParentId);

CREATE TABLE ClipTags (
    ClipId INTEGER NOT NULL REFERENCES Clips (Id) ON DELETE CASCADE,
    TagId  INTEGER NOT NULL REFERENCES Nodes (Id) ON DELETE CASCADE,
    PRIMARY KEY (ClipId, TagId)
);

CREATE INDEX IX_ClipTags_TagId ON ClipTags (TagId);

CREATE TABLE Markers (
    Id         INTEGER PRIMARY KEY,
    ClipId     INTEGER NOT NULL REFERENCES Clips (Id) ON DELETE CASCADE,
    PositionMs INTEGER NOT NULL,
    Label      TEXT    NOT NULL
);

CREATE INDEX IX_Markers_ClipId ON Markers (ClipId);

CREATE TABLE Sessions (
    Id        INTEGER PRIMARY KEY,
    StartedAt TEXT NOT NULL,
    EndedAt   TEXT,
    -- Null while open (or after a crash), then Committed or Discarded.
    Outcome   TEXT
);

-- One row per action; the last row of an open session is its current state.
CREATE TABLE SessionJournal (
    Seq       INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL REFERENCES Sessions (Id) ON DELETE CASCADE,
    Snapshot  TEXT    NOT NULL,
    CreatedAt TEXT    NOT NULL
);

CREATE TABLE FileOperations (
    Id         INTEGER PRIMARY KEY,
    ClipId     INTEGER NOT NULL REFERENCES Clips (Id),
    Kind       TEXT    NOT NULL CHECK (Kind IN ('Delete', 'Rename', 'RestoreName')),
    FromPath   TEXT    NOT NULL,
    ToPath     TEXT,
    -- Planned before the operation starts, then Done or Failed (spec §9).
    Status     TEXT    NOT NULL DEFAULT 'Planned',
    Error      TEXT,
    PlannedAt  TEXT    NOT NULL,
    FinishedAt TEXT
);
