# Yeetle — instructions for Claude

Yeetle is a Windows desktop app (C#, .NET 10, Avalonia 12) for reviewing, tagging and finding video clips after a stream. The product owner is ZiL (GitHub: Zilounay). **`docs/SPEC.md` is the source of truth**: read the relevant section before implementing a feature, and never invent behavior for something it lists as open (section 17); ask instead.

## Invariants (never break these)

1. Scanning, playing, tagging, filtering and browsing never modify a user's video file.
2. Only the file-operations service may delete or rename files, only after explicit user confirmation, one file at a time, recording every outcome. Deletes go to the Recycle Bin when Windows allows it.
3. Never reset, recreate or "repair" the database by dropping data. Schema changes go through a new numbered migration, with a backup taken first.
4. A clip's file state (Present / Missing / Offline / Deleted / Needs attention) and its decision (New / Kept / Yeeted) are separate fields. Never merge them.
5. No cloud services, accounts or network calls other than the update check and the opt-out usage statistics described in the spec.
6. No long-running work on the UI thread: scanning, FFmpeg, database and file I/O are async or on background work.

## Solution layout

| Project | Contains | May reference |
| --- | --- | --- |
| `Yeetle.Domain` | Clip, tag and box rules, session state, filters, filename builder | nothing |
| `Yeetle.Application` | Scanner, session service, file operations, library queries, interfaces | Domain |
| `Yeetle.Infrastructure` | SQLite (Dapper), file system, Recycle Bin, Windows shell, FFmpeg, Velopack, telemetry | Application, Domain |
| `Yeetle.Media` | Player interface and implementation | Application |
| `Yeetle.App` | Avalonia views and view models | Application, Domain |
| `Yeetle.Desktop` | The executable: dependency injection setup (`Composition.cs`) and startup, no logic | everything |
| `*.Tests` | xUnit tests, one test project per source project (none for Desktop) | its project |

The player's interface is `IClipPlayer` in `Yeetle.Application/Playback`; `Yeetle.Media` implements it with LibVLC drawing into native child windows that `Yeetle.App` hosts (`VideoScreenHost`). Native windows cover anything drawn over them, so no control may sit on top of the video.

## Conventions

- Nullable reference types on, warnings as errors in Domain and Application.
- MVVM with CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`). No logic in code-behind beyond view wiring.
- SQL is written by hand with Dapper. Migrations live in `Yeetle.Infrastructure/Migrations/NNNN_description.sql`, numbered and never edited once merged. Enable `PRAGMA foreign_keys = ON` and WAL mode on every connection.
- All user-facing text goes in resource files (`.resx`), English and French. No hard-coded UI strings.
- Code, comments, commit messages and issues are in English.
- No magic numbers or strings. A literal whose meaning is not obvious goes in a well-named constant (or a resource in XAML), or gets a comment saying why it has that value. Strings that may change (option names, file names, separators) are constants.
- Write `string.Empty`, not `""`. The only exception is attribute arguments, which must be compile-time constants.
- User settings live in `settings.json` (`%LOCALAPPDATA%\Yeetle`), read through `ISettingsStore`. New settings get a default in `AppSettings`, so older files keep working.
- Ideas and side tasks that are not issues yet go in `docs/IDEAS.md`.
- Paths to clips are stored relative to their source folder.

## Workflow

- Every change belongs to a GitHub issue. Branch name: `issue-<number>-short-description`. Reference the issue in commits; use `Fixes #<number>` in the pull request that completes it.
- Before committing: `dotnet build` with no warnings, `dotnet test` green. New domain rules come with tests.
- Keep pull requests to one issue. If the spec has to change, update `docs/SPEC.md` in the same pull request and say so in its description.
- POC code (milestone "1 · POC") lives under `poc/` and is never referenced by the main solution. A tryout is deleted once its question is answered (see `poc/README.md`), except `poc/ClipStrip`: it is kept for postponed thumbnails (`docs/THUMBNAILS.md`) and is only removed after asking ZiL.
