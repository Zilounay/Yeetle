-- What each committed session handled (#72), written at commit, since it cannot be rebuilt later:
-- the session's journal is cleared and the clips' own decision and tags keep changing. A clip
-- wiped and reviewed again appears in both sessions. Discarded sessions write nothing.
CREATE TABLE SessionClips (
    SessionId  INTEGER NOT NULL REFERENCES Sessions (Id) ON DELETE CASCADE,
    ClipId     INTEGER NOT NULL REFERENCES Clips (Id) ON DELETE CASCADE,
    -- Place in the review order, from 0.
    Position   INTEGER NOT NULL,
    -- The decision taken then: Kept, Yeeted, or New for a clip skipped.
    Decision   TEXT    NOT NULL,
    -- When it was decided or skipped; null for clips reviewed before this was recorded.
    ReviewedAt TEXT,
    PRIMARY KEY (SessionId, ClipId)
);

CREATE INDEX IX_SessionClips_ClipId ON SessionClips (ClipId);

-- The tags each clip had at commit, only to tell when they have changed since.
CREATE TABLE SessionClipTags (
    SessionId INTEGER NOT NULL,
    ClipId    INTEGER NOT NULL,
    TagId     INTEGER NOT NULL REFERENCES Nodes (Id),
    PRIMARY KEY (SessionId, ClipId, TagId),
    FOREIGN KEY (SessionId, ClipId) REFERENCES SessionClips (SessionId, ClipId) ON DELETE CASCADE
);
