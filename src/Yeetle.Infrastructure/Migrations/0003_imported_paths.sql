-- Every box and tag a tags file has brought, by its path (its names from the top, see
-- VocabularyMerge), with the node it became (#65). A path listed here is never created again, so
-- a tag the user renamed or deleted in the Tag Manager stays renamed or deleted.
CREATE TABLE ImportedPaths (
    Path   TEXT    PRIMARY KEY,
    NodeId INTEGER NOT NULL REFERENCES Nodes (Id)
);
