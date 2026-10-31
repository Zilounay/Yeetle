-- Deleting a tag or box in the Tag Manager retires it instead of removing it (#62): it leaves the
-- Tag Manager and can no longer be picked, but the clips that carry it keep it under its old
-- name. Ids are never reused, so a new tag always gets a new id.
ALTER TABLE Nodes ADD COLUMN Retired INTEGER NOT NULL DEFAULT 0;
