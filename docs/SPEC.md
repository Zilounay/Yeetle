# Yeetle — Product & Technical Specification v0.2

> Snapshot of the living spec as of 2026-10-27. Product owner: ZiL. Decisions recorded here were confirmed on 2026-10-10.
> When the spec changes, update this file in the same pull request as the code that depends on it.

## 1. Overview

Yeetle is a free, open-source Windows desktop app that lets content creators review a pile of new clips one at a time, decide Keep / Yeet / Skip, tag them, and find them again later in a virtual library they can drag straight into their editor. The name comes from yeet: watch a clip, decide, and yeet what you don't keep (working name: Yeeter).

This document supersedes the ChatGPT spec v0.1. Anything not yet decided is listed in section 17.

**Target user.** Streamers and video creators who end a stream or recording session with tens to a few hundred clips in a folder (usually from OBS) and later edit in Premiere Pro, DaVinci Resolve or similar. Libraries grow over time, but a single review session is expected to be in the tens to low hundreds of clips, not thousands.

**The product promise.** Finish a stream with a pile of clips and, with almost no waiting, turn it into a clean, searchable personal library — without Yeetle ever touching your files unless you explicitly ask it to.

**Invariants** (protected above everything else):

1. Scanning, playing, tagging, filtering and browsing never modify a file.
2. Delete and rename only happen after an explicit, honest confirmation, and every result (success or failure) is recorded.
3. User metadata (tags, markers, decisions) is never silently lost: no automatic DB resets, backups before risky operations, crash-safe sessions.
4. Yeet means hidden in Yeetle, not deleted. Skip means not decided yet.
5. The library is virtual: Yeetle's organization comes from tags, never from folders.
6. Dragging clips out hands other apps the real files, as a copy.
7. The review loop must feel instant (target: next clip playing in under 150 ms).

## 2. Scope

The work runs in four stages: a technical POC, an MVP for ZiL and beta testers, a public v1.0, then later releases. Nothing is publicly released before v1.0.

| Stage | Purpose | Contents |
| --- | --- | --- |
| POC | Prove the risky tech before building UI | Video playback in Avalonia (clip switch under 150 ms, 4x speed, audio-track switching, overlay test), thumbnail extraction, drag-out of real files into Premiere and Explorer |
| MVP | Full core loop, tested by ZiL and beta testers | Source folders + scanning, review sessions (Keep/Yeet/Skip, tags, markers, sticky tags, undo, progress counter, carousel badges, crash recovery), boxes-and-tags model with a Tag Manager, type-to-tag, hotkeys, Explorer (filters, multi-select, edit, wipe data, drag-out, Windows context menu, marker window), session summary with delete + rename prompts, Recycle Bin delete, rename + restore, migrations + backups, settings, EN/FR, local telemetry with upload, donation prompts, in-app one-click updates |
| v1.0 public | Ready for strangers | Code signing, polished visual design, final Tag Manager UX, privacy notice/policy, website or landing page, performance tuning from beta feedback |
| Later | Nice to have | Move/rename detection (file fingerprint), relocate a source folder, Premiere/Resolve marker export, folder-to-tag rules, tag merge, file watcher, macOS |

**Explicit non-goals.** Cloud storage, accounts, collaboration, an integrated video editor or trimmer, AI auto-tagging, re-review *sessions* over old clips (old clips are edited one by one in Explorer instead), separate libraries per game, full-screen playback (theatre mode only), and any import of markers into editing software.

## 3. Core concepts and clip states

A clip carries two independent states that are never merged into one: where the file stands on disk, and what the user decided about it.

| Concept | Meaning |
| --- | --- |
| Source folder | A folder the user points Yeetle at, with an "include subfolders" option. Any number of them, merged into one library. |
| Clip | One video file known to the database: source folder + path inside it, size, last-modified date, recording date, duration, original filename, tags, markers, states. |
| Session | A review run over undecided clips, oldest first. Held in memory, journaled to the DB, committed or discarded at the end. |
| Tag / Box | The tagging vocabulary (section 6). |
| Marker | A labelled timestamp inside one clip. |

**File state** (from scanning, never from the user):

| State | Meaning |
| --- | --- |
| Present | File found where expected. |
| Missing | Its source folder is reachable but the file is not there. Metadata is kept. |
| Offline | Its whole source folder is unreachable (unplugged drive, network down). Clips are left untouched, not marked Missing. |
| Deleted | Yeetle deleted it at the user's request. The record and its metadata stay. |
| Needs attention | The record or file could not be read properly. Shown with all readable data; the user wipes or rescans it manually. |

**Decision** (from the user):

| Decision | Meaning |
| --- | --- |
| New | Never decided. Appears in sessions. Skipping keeps a clip New. |
| Kept | Wanted. Default view of the Explorer. |
| Yeeted | Unwanted. Hidden from the default Explorer view; file untouched unless the user deletes it. Can be set back to Kept from the Explorer. |

A removed source folder is a third, separate thing: its clips can be hidden (folder marked Removed) or kept visible with scanning stopped. That is a property of the folder, never a clip decision.

## 4. Library and scanning

Yeetle reads the user's folders and records what it finds; it never writes to them during a scan.

**Source folders.** Any number. Each scans its root only by default, with an "include subfolders" checkbox that treats the whole tree as one folder. Users may pre-organize folders however they like (by game, date, stream); Yeetle does not care. Each clip's path is stored relative to its source folder, so a drive-letter change later only means updating one folder path.

**Supported files.** An editable extension list in Settings, defaulting to mp4, mkv, mov, flv, ts, webm, avi. Not "anything VLC can open", which would include music and images.

**When scans run.** At app start, when a session starts, when the window regains focus (at most once per minute), and from a Rescan button. A scan of a few thousand files on a local drive takes well under a second, so it runs in the background without a loading screen.

**Identity in the MVP.** A clip is its path inside its source folder. Moving or renaming a file outside Yeetle makes the old record Missing and the file at its new place New, even if it is the same video. Renames done by Yeetle update the path directly. If a file at a known path has a different size or date, it stays the same clip, its details are refreshed, and it is listed in the scan summary. Move/rename detection with a file fingerprint is a later feature.

**Recording date.** Taken from the recording time stored inside the video (written by OBS), falling back to the file's last-modified date. Never parsed from the filename, since users rename files. This date drives session order, Explorer order and renamed filenames.

**Files still being written.** A file modified less than 10 seconds ago, or found while OBS is running, is not imported in that scan; Yeetle shows a notice ("1 file looks like it is still recording") and picks it up on a later scan.

**Missing and offline.** If a source folder itself is unreachable, it is marked Offline and its clips are left as they are. Only files missing from a reachable folder become Missing. Yeetle never tries to find them; the user can wipe the record or restore the file.

**First scan with an existing backlog.** When the first scan of a folder finds clips, Yeetle asks: review all of them, mark them all Kept (straight to the library), or review only clips recorded after a chosen date (the rest become Kept).

**Removing a source folder.** No clips: removed silently. With clips: Hide its clips / Keep them visible but stop scanning / Cancel. Files are never touched.

## 5. Review sessions

A session runs through every New clip, oldest recording first, one clip at a time; it can be stopped at any moment and resumed another day.

**Starting.** The home screen shows how many New clips are waiting and a Start button. A skipped clip stays New and is the first one shown next time, until it is decided. The Explorer is unavailable while a session runs; Settings and the Tag Manager stay reachable.

**The screen.** The current clip plays automatically from 0:00 and loops. Below it, a thumbnail strip shows 2 previous clips, the current one (highlighted) and 2 next ones; the user can scroll back to the first clip of the session. Each thumbnail shows a shortened filename, the recording date and a decision badge (kept, yeeted, skipped). A progress counter shows the position, e.g. 17 / 43. Tags and markers sit in a compact side area. The final layout is designed after the POC (section 17).

**Deciding.** Enter keeps, Del yeets, Shift+Right moves on without deciding (a skip), Shift+Left goes back. Keep and Yeet move to the next clip. Any decision can be changed until the session ends. Ctrl+Z undoes the last actions (decisions, tags, markers) step by step.

**Sticky tags.** When the next clip has no tags yet, the tags of the previous clip are applied to it automatically and shown as carried over, so a run of Dota clips needs no re-tagging. Removing one is a single click.

**Required boxes (soft).** If a visible required box is empty when the user presses Keep, the box flashes red and the clip is not kept; pressing Keep again keeps it anyway.

**Tag changes mid-session.** Editing tags in the Tag Manager applies immediately and permanently: clips only hold tag IDs, so a renamed tag simply shows its new name. Creating a tag adds it to no clip; deleting a tag warns "X clips will be affected" and removes it everywhere.

**Crash safety.** Every action is written immediately to a session journal in the database. Nothing becomes final until Commit, but after a crash Yeetle offers: Resume / Commit / Discard.

**Leaving.** A clearly visible Stop button ends the session. Closing the app mid-session asks: Save and leave / Discard and leave / Cancel.

**Ending.** The user first sees a read-only summary, then chooses Commit or Discard. After a commit, two separate opt-in prompts follow: "X files were yeeted. Delete them from the filesystem as well?" with the Recycle Bin warning (section 9), then "Rename all kept files by their tags? (N files affected)" with a preview. Only kept clips are ever renamed.

```
Session ends (Stop, or last clip reached)
  -> Read-only summary (counts + collapsible kept / yeeted / skipped lists)
  -> Commit? --discard--> nothing saved; clips stay New -> home
       | commit
  -> Saved to the database (journal cleared, backup taken)
  -> "Delete the yeeted files?"  (yes: Recycle Bin where possible, honest warning)
  -> "Rename the kept files by their tags?"  (yes: preview old -> new; originals stored)
  -> home
```

Discarding drops every decision, tag and marker made in the session; the clips stay New.

## 6. Tagging model

The vocabulary has two kinds of things: tags, which are put on clips, and boxes, which hold tags and carry the rules. This replaces v0.1's tags + top-level groups, which could not express "a clip is on one map".

```
[Game]  pick one · required
   Counter-Strike 2
      [Map]  pick one · required
         Dust2   Mirage   Ancient
      [Weapon]  pick many
         M4   AWP
   Dota 2
      [Hero]  pick many
         Huskar   Pudge   Crystal Maiden
[People]  pick many
   ZiL   Friend A
[Moments]  pick many        <- loose tags live in a box too
   Funny   Educational   Clutch   Fail
```

Boxes (in brackets) are never put on a clip or in a filename; everything else is a tag.

**Rules**

- A box has one switch, Pick one or Pick many, and an optional Required flag.
- A box sits at the top level or under a tag. A box under a tag only appears when that tag is selected, so Dota's Hero box never shows on a CS clip.
- A tag can also hold tags directly, without a named box (Funny → Unexpected): that is an invisible pick-many box.
- Selecting a tag also selects its parent tags, and both are stored on the clip. Removing a tag removes the tags below it.
- In a pick-one box, choosing a tag swaps out the one already chosen (Dust2 then Mirage leaves Mirage), along with anything below it.
- Required is soft: it only applies when the box is visible, and only warns (section 5).
- Several top-level tags on one clip are allowed (two games, or games plus people); only box rules limit choices.
- Clips store tag IDs only. Renaming a tag changes its name everywhere; deleting one removes it from every clip after a warning with the count; a new tag starts on no clip. Moving tags is not supported.

**Searching.** Tags from the same box combine with OR, tags from different boxes with AND. "CS2 + Dust2 + Mirage + M4" finds CS2 clips on Dust2 or Mirage that have an M4.

**Picking tags during review.**

- Visible chips: tags that have a hotkey, carried-over tags from the previous clip, the most-used tags, and the boxes under any selected tag. The exact mix will be tuned during testing.
- T opens a type-to-tag box. Typing shows matching existing tags with their path (Dota 2 › Hero › Crystal Maiden); Enter applies the highlighted one. Enter with no match opens the Tag Manager with the typed text ready as a new tag. Tags cannot be created from the box itself.

**Tag Manager.** Reachable from Settings and from the main screens, even during a session. It shows the whole vocabulary visually as nested boxes and chips, with fast creation, rename, delete, colour, hotkey, pick one/many and Required. Its order is the order used in renamed filenames.

**Names and filenames.** Tag names can use any characters and alphabets, Cyrillic included. Only when renaming files does Yeetle build a filename-safe version: it removes the characters Windows forbids (`\ / : * ? " < > |`), control characters and trailing dots or spaces, avoids reserved names such as CON or NUL, and keeps letters as they are. Box names never appear in filenames. Case-insensitive search is done in C#, since SQLite only folds A–Z.

## 7. Player and markers

The same player is used in sessions and when a clip is opened from the Explorer; it must switch clips in under 150 ms and never show a loading state in normal use.

**Player.**

- Play/pause, seek bar, volume slider, mute, theatre mode (no full screen).
- Speeds 1x, 1.25x, 1.5x, 2x, 3x, 4x.
- Every clip starts at 0:00 and loops at the end; there is no auto-advance.
- Audio: the first track in the file plays by default. Switching is one key (B cycles tracks, as in VLC) or a small always-visible picker (dropdown or scroll wheel), never a right-click menu. Typical OBS setup: game mix, mic, Discord on separate tracks.
- Frame stepping exists as a hotkey but is not a focus.
- Performance: the current clip loads first; the next and previous clips and the visible thumbnails are prepared in the background. Thumbnails are generated lazily with FFmpeg and cached; the cache can be deleted at any time without losing data.

**Markers.**

- K creates a marker 1 second before the key press, named "Marker N" (N = next number for that clip). Playback continues; the label can be typed inline right away or later, and hotkeys are paused while typing.
- Precision is not a goal: markers are guides for finding moments later while editing, and a few seconds off is acceptable. They behave like Premiere Pro markers.
- Markers show as ticks on the seek bar (hover shows the label) and as a list under the tags, where they can be renamed, moved or deleted. They can overlap and be added while paused.

**Marker window.** From the Explorer selection (button and shortcut), Yeetle opens a small read-only text window listing each selected clip with its tags and markers, for the user to copy while editing:

```
### 2026-10-11 22-15-36.mp4 ###
Tags: Counter-Strike 2, Dust2, M4, Funny
Markers:
1:27  Smoke kill
3:17  Triple kill
```

No file is written and nothing is exported to editing software.

## 8. Explorer (the library)

The Explorer is the second half of Yeetle: a virtual library of every clip in the database, built to feel like Windows Explorer but organized by tags instead of folders.

**Why custom.** Windows can embed a real Explorer view, but only for real folders; showing Yeetle's filtered results inside it would need a native shell extension, which is fragile and can crash Explorer itself. So the view is built in Avalonia and reuses the real Windows pieces where possible. Estimated effort: 1–2 weeks.

**View.** Thumbnail grid (list view optional), newest recording first. Each item shows thumbnail, shortened name, recording date, duration and tags. Sorting by date, duration or name. Thumbnails load lazily and the grid is virtualized.

**Filters.** Tag filters (same box = OR, different boxes = AND, a parent tag matches clips tagged with its children). A decision filter: Kept by default, plus New, Yeeted. A file-state filter: Missing, Offline, Deleted, Needs attention. A text search over filenames and marker labels. An "incomplete" filter lists clips with an empty required box.

**Selection and actions.** Explorer-standard behavior: click, Ctrl/Shift multi-select, Ctrl+A, drag-select, arrow keys, Del, F2. Right-click shows the genuine Windows context menu for the files (Open with, Send to, etc.) with Yeetle's own entries on top. Actions:

- Open: the clip opens in Yeetle's player/editor, without carousel or Skip. Tags, markers and the Keep/Yeet decision are editable and saved immediately, with undo.
- Show in folder; open in the default Windows player.
- Set Kept / Yeeted (un-yeeting is just setting Kept).
- Delete from disk (section 9).
- Rename by tags and Restore original name (section 9).
- Show markers (the marker window, section 7).
- Wipe data: removes tags, markers and decision so the clips become New and return in the next session. This is the only way to re-review a clip. It asks for confirmation with the clip count and takes a backup first.

**Drag out.** Dragging a selection hands the real files to the target (Windows Explorer, Premiere Pro, DaVinci Resolve, a browser upload, another folder) as a copy, never a move. Missing, Offline and Deleted clips are left out of the drag with a short notice.

## 9. File operations and safety

Delete and rename are the only two things Yeetle ever does to a file, and both run one file at a time with every result recorded.

**Order of operations.** The database is saved first, then each file operation runs, then each outcome (done, or failed with its reason) is written to the database. Planned operations are journaled before they start, so after a crash Yeetle re-checks each file and records what actually happened.

**Delete.**

- Offered after a commit (yeeted clips of that session) and from the Explorer (any selection).
- Files go to the Recycle Bin wherever Windows allows it. The prompt says so honestly, roughly: "Files go to the Recycle Bin when possible. Network drives, USB drives and very large files skip the Recycle Bin and are deleted permanently. Only continue if you are ready to lose these files." It shows the exact file count and a collapsible list, in destructive styling with a warning icon.
- The record stays with file state Deleted, keeping its tags and markers.

**Rename.**

- Offered after a commit (kept clips of that session, opt-in each time) and from the Explorer.
- New name = the clip's tags in Tag Manager order, then the recording date and time, then the original extension: `CounterStrike2_Dust2_M4_Funny_2026-10-11_22-15-36.mp4`. Within a tag, spaces and hyphens are dropped so the underscore stays the only separator.
- Each tag is capped at 15 characters in a filename. If the full path would exceed the Windows limit of 260 characters, tags are shortened, last in order first, until it fits.
- Name collisions get a numeric suffix (`_2`, `_3`).
- A preview lists old → new names, flags any shortened ones and warns that editing projects that use the old names (e.g. in Premiere) will show the media as offline.
- The original filename is stored, so "Restore original name" is available in the Explorer and the summary.

**Errors.** Every file operation assumes the file may be gone, locked by another app, read-only, on a disconnected drive, or blocked by permissions. Failures are listed per file in plain language. A partial failure is never reported as success, and the database only records what really happened.

## 10. Data, backups and recovery

The database holds the user's work, so it is backed up automatically, upgraded carefully, and never reset to get the app open.

**Where things live** (all under `%LOCALAPPDATA%\Yeetle\`):

| Item | Format | Notes |
| --- | --- | --- |
| Library database | SQLite (`yeetle.db`) | Clips, folders, tags and boxes, markers, session journal, file-operation log |
| App settings | JSON file | Kept outside the database so a broken database does not also lose the source folders |
| Telemetry | Separate SQLite file | So restoring a backup does not roll it back, and a telemetry problem never blocks the app |
| Thumbnail cache | Image files | Disposable; deleting it loses nothing |
| Backups | SQLite copies | See below |
| Logs | Text files | Keep full file paths locally; paths are removed only if logs are ever sent |

**Main tables (first draft).** Clips, SourceFolders, Nodes (tags and boxes in one tree: kind, parent, name, order, colour, hotkey, pick one/many, required), ClipTags, Markers, SessionJournal, FileOperations. The schema version is stored in the database.

**Migrations.** Numbered SQL scripts run in order at startup, before anything else touches the data. A backup is taken before every migration. A migration never drops user data. An older Yeetle that finds a newer schema refuses to open it with a clear message instead of damaging it.

**Backups.** Made with SQLite's own backup mechanism (safe while the app runs), into separate pools so one kind cannot push out another:

- after each session commit: last 5 kept
- daily: last 7 kept
- before a migration, a tag delete, a wipe or a batch file operation: last 5 kept

Counts are adjustable in Settings. Only the database is backed up, never video files.

**Corruption.** If the database fails its integrity check, Yeetle does not recreate it. It opens a recovery screen listing the available backups to restore, then rescans. The user can also send the damaged file to the developer: in the MVP Yeetle opens its folder and explains how, with a clear note that it contains file paths and tags. Unreadable single records go to the Needs attention list (section 3).

**One instance.** Only one copy of Yeetle can run at a time; a second launch brings the first window forward.

## 11. Settings

Settings are reachable at any time, including during a session; anything configurable elsewhere in this document lives here.

| Area | Settings |
| --- | --- |
| Library | Source folders (add, remove, include subfolders), supported extensions, Rescan |
| Tags | Opens the Tag Manager |
| Hotkeys | Every action and tag hotkey, rejects duplicates (one key = one action), restore defaults |
| Player | Default speed, default volume, seek steps (default 5 s, and 30 s with Ctrl), volume step (default 5), theatre mode, background preparation of neighbouring clips (on/off, for low-memory PCs) |
| Review | Sticky tags on/off, marker offset (default 1 s before the key press) |
| Library view | Default filters, grid or list |
| Data | Backup counts per pool, open the data folder, restore a backup, clear the thumbnail cache |
| Privacy | Usage statistics on/off |
| General | Language (English, French), check for updates |

## 12. Telemetry, donations, updates and license

Yeetle is free, open source under GPL-3.0, funded by PayPal donations, and updates itself in one click.

**Usage statistics.**

- On by default. A notice at first start explains what is collected; one switch in Settings turns it off.
- Collected: counts and durations only (sessions, clips reviewed, kept, yeeted, skipped, tags applied, markers created, drags out, deletes, renames, time in app, playback time), app version, Windows version, and a random install ID.
- Never collected: filenames, paths, tag names, marker labels, or anything from the videos.
- Recorded locally first and uploaded in the background when online, so startup is never delayed.
- Because the install ID makes the data pseudonymous under GDPR, a short privacy notice is needed before v1.0.

**Donations.** PayPal only, to ZiL's account, through a donation link rather than a raw email address in the code. A prompt appears only at app start or on the session summary, never mid-session, at milestones such as 30 days of actual use or 1,000 clips yeeted. Yeetle remembers the last prompt and waits at least 14 days before another one. Each prompt offers Donate / Not now / Don't ask again.

**Updates (MVP).** Velopack with GitHub Releases. Yeetle checks in the background at startup; when a version is available it shows "Update available", and one click downloads it and restarts. Installation is per-user and needs no administrator rights.

**Signing.** MVP builds are unsigned, so testers will see a Windows SmartScreen warning (More info → Run anyway). Before v1.0, builds are signed for free through the SignPath Foundation program for open-source projects (eligibility to confirm). Azure Artifact Signing is not available to individuals in the EU.

**License.** Yeetle is GPL-3.0: anyone can read, fork and modify it, and forks must stay open source. All planned dependencies are compatible:

| Dependency | License |
| --- | --- |
| Avalonia, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Velopack | MIT |
| Dapper | Apache 2.0 |
| LibVLC / LibVLCSharp | LGPL 2.1+ |
| FFmpeg | LGPL or GPL build, both fine |
| libmpv (if the POC picks it) | GPL, fine |

License notices for all of them ship with the app.

## 13. Tech stack and architecture

C# on .NET 10 with Avalonia 12 is confirmed after review, and the POC settled the video player (option A, decided 2026-10-28, #8).

| Part | Choice | Why |
| --- | --- | --- |
| Language and runtime | C# on .NET 10 LTS | Supported until November 2028 |
| UI | Avalonia 12, MVVM with CommunityToolkit.Mvvm | Free and open source, good Rider support, easy custom styling, supports dragging files out to other apps. WPF would gain nothing: same video limits, weaker Rider designer. |
| Database | SQLite via Microsoft.Data.Sqlite + Dapper | Local file, no server. Numbered SQL migration scripts, WAL mode, foreign-key checks turned on (off by default in SQLite). |
| Playback | LibVLCSharp drawing into native child windows (option A) | Lowest CPU use (about 0.5% against 8% for option B at 1080p60); controls sit over the video in a transparent overlay window |
| Thumbnails and metadata | ffmpeg.exe / ffprobe.exe run as separate processes | Simple, robust, no native bindings |
| Installer and updates | Velopack + GitHub Releases | One-click updates, no admin rights |
| Code, tickets, CI | Public GitHub repo, GitHub Issues, GitHub Actions | One place; commits close tickets |
| Tests | xUnit | Standard for .NET |
| IDE | JetBrains Rider 2026 | Already set up |

**Player options for the POC.**

| Option | Buttons over video | Effort | Main risk |
| --- | --- | --- | --- |
| A. LibVLCSharp native view | No (a native window covers the area) | Low | No overlays at all |
| B. VLC frames drawn by Avalonia | Yes | Medium | Higher CPU use at 1080p60 and 4x |
| C. libmpv drawn through OpenGL inside Avalonia | Yes | Medium to high | C# wrapper is an early beta; we may write a small one |

Outside C#, only Qt 6 (C++ or Python) would be a serious alternative. Electron/Tauri (no audio-track switching in browser video) and WinUI 3 (HEVC needs a paid codec on many PCs) are ruled out. If neither B nor C feels right, Yeetle uses A with buttons in a bottom bar and side area, which is acceptable.

**Decision (2026-10-28, #8).** Option A. Option B worked but cost about 15 times the CPU; option C was dropped. Buttons still sit over the video: they live in a separate transparent window kept over the video area, the same approach as LibVLCSharp's WPF player. Clip switches stay under 150 ms by preloading the next clip in a second native window behind the current one.

**Solution structure.**

```
Yeetle.Desktop         the executable: composes every layer below (dependency injection), starts the app
   | uses
Yeetle.App             Avalonia: session screen, Explorer, Tag Manager, Settings, view models
   | uses
Yeetle.Application     scanner, session service, file operations, library queries,
   |                   and the interfaces it needs (storage, files, media)
   | uses                         ^ implements                 ^ implements
Yeetle.Domain          Yeetle.Infrastructure            Yeetle.Media
clip, tag and box      SQLite via Dapper, files,        player interface,
rules, session state,  Recycle Bin, shell menu,         VLC or mpv (from the POC)
filters, rename        FFmpeg, Velopack, telemetry
builder; no deps
```

The domain project holds every rule (tags and boxes, session state, filters, filename building) with no dependencies, so it is tested first and without a UI. The UI talks only to the application layer, which reaches SQLite, the file system, FFmpeg and the player through interfaces. Only `Yeetle.Desktop` knows the implementations: it registers them and hands the app its view models.

## 14. Default hotkeys

Playback keys follow VLC where they do not clash with review actions; every key is reassignable, one key can only do one thing, and hotkeys pause while a text field has focus.

| Action | Key | Note |
| --- | --- | --- |
| Keep and go to next clip | Enter | |
| Yeet and go to next clip | Del | Reversible until the session ends |
| Next clip without deciding (skip) | Shift+Right | VLC uses this for a 3 s jump |
| Previous clip | Shift+Left | |
| Undo | Ctrl+Z | Repeatable |
| Play / pause | Space | VLC |
| Seek back / forward | Left / Right | VLC keys; 5 s by default |
| Long seek back / forward | Ctrl+Left / Ctrl+Right | VLC keys; 30 s by default |
| Volume up / down | Up / Down, mouse wheel | |
| Mute | M | VLC |
| Next audio track | B | VLC |
| Slower / faster / normal speed | [ / ] / = | VLC |
| Speed 1x, 1.25x, 1.5x, 2x, 3x, 4x | 1–6 (number row) | Matched by key position, so the same keys on AZERTY. Clashes with the tag shortcuts below: to settle before #15 |
| Next frame | E | VLC |
| Theatre mode | F | Replaces VLC full screen |
| Add marker | K | M is taken by Mute |
| Type-to-tag box | T | |
| Tag shortcuts | 1–9, F1–F12 | Assigned per tag in the Tag Manager |
| Stop session | Esc | Opens the Save / Discard / Cancel prompt |
| Rescan | F5 | |
| Marker window (Explorer) | Ctrl+M | |
| Settings | Ctrl+, | |

Later additions (not MVP): Tab to move focus across tag chips and Backspace to remove the focused tag.

## 15. Build plan

The POC comes first because the player decides the screen layout; then the MVP is built in slices that each leave a working, testable app. Each step is tracked as GitHub issues under a milestone.

**Setup**

1. Set up the public repo github.com/Zilounay/Yeetle (GPL-3.0, README, .gitignore).
2. Create the solution structure (section 13) on Avalonia 12 and .NET 10, with a GitHub Actions build on every push.
3. Create the milestones and labels, then file this plan as issues.

**POC (about a week, throwaway code)**

1. Player option A: open a clip, switch clips in under 150 ms, play at 4x, switch audio tracks.
2. Options C, then B: put a button over playing video.
3. FFmpeg: time to extract one thumbnail, and read OBS's recording time from the file.
4. Drag 10 files out to Windows Explorer, Premiere Pro and DaVinci Resolve.
5. Test with ZiL's real clips: OBS mp4 and mkv, multiple audio tracks, H.264, HEVC and AV1, 1080p60 and higher.

Gate: choose the player, and with it whether buttons can sit over the video.

**Design (together)**

1. Mockups of the session screen, Explorer and Tag Manager, based on what the POC allows.

**MVP slices**

1. Domain rules + tests: tags and boxes, session state, filters, filename builder.
2. Database: schema, migrations, backups, settings file, single instance, logging.
3. Source folders and scanning: first-scan choice, Missing/Offline, files still recording.
4. Player and thumbnail cache.
5. Session screen: navigation, decisions, thumbnail strip with badges, counter, undo, journal and crash recovery.
6. Tagging in sessions: chips, sticky tags, hotkeys, type-to-tag, required boxes.
7. Tag Manager.
8. Markers.
9. Session end: summary, commit/discard, delete and rename prompts, restore names.
10. Explorer: grid, filters, search, editing, wipe data, Windows context menu, drag out, marker window.
11. Settings and EN/FR translations.
12. Usage statistics and donation prompts.
13. Installer and one-click updates; first beta build to testers.

**v1.0**

1. Fixes and tuning from beta feedback.
2. Final visual design.
3. Code signing, privacy notice, landing page.
4. Public release on GitHub Releases.

## 16. Testing

Rules are tested automatically from the first slice; file safety is tested against real failure cases; the feel is tested by ZiL and beta testers on real streamer clips.

**Automated (xUnit).**

- Domain: parent tags auto-added, children removed with their parent, pick-one swaps, required warnings, filter logic (OR within a box, AND across), session decisions and undo, filename building (order, 15-character cap, 260-character path limit, collisions, forbidden characters, Cyrillic).
- Database: every migration from every earlier version, backup pools and restore, journal replay after a simulated crash.
- Scanning: new, missing, offline folder, changed file at the same path, files still recording, subfolders on and off, same filename in two folders.
- FFmpeg: recording time and thumbnail from sample files, including a corrupt file.

**Manual scenarios.**

- First launch with 0 clips and with 1,000 clips (all three first-scan choices).
- App killed mid-session, then Resume, Commit and Discard.
- Delete to a local disk, a USB drive and a network drive; a file locked by Premiere; a read-only folder.
- Rename with very long tag lists and deep folders.
- Unplugged drive at startup.
- Drag-out to Explorer, Premiere, Resolve and a browser upload.
- Hotkeys while typing a marker label or in the type-to-tag box.

**Performance targets.** Next clip playing in under 150 ms; thumbnails visible without a placeholder in normal use; scan of 2,000 clips under 1 second on a local SSD; app ready in under 2 seconds.

## 17. Still open

No product question blocks the start; these are decided at the step noted (each is a GitHub issue labelled `type: decision`).

- [ ] Screen layouts for the session screen, Explorer and Tag Manager — designed together after the POC.
- [ ] Which chips show by default during review (hotkeyed, carried over, most used) — tuned in beta.
- [x] Player choice (A, B or C) — POC gate. Option A, see section 13.
- [ ] Service that receives usage statistics (Aptabase or a small own endpoint) — before MVP slice 12.
- [ ] PayPal donation link to use in the app — before MVP slice 12.
- [ ] SignPath Foundation eligibility for free signing — before v1.0.
- [ ] Check that "Yeetle" is free as a trademark and Microsoft Store name — before any logo work.
- [ ] Confirm the hotkey changes versus the earlier chat: marker on K (M stays Mute, as in VLC) and audio track on B (as in VLC).
- [ ] Confirm the proposed performance targets beyond the 150 ms clip switch (section 16) — after the POC.
