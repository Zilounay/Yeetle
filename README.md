# Yeetle

[![Build](https://github.com/Zilounay/Yeetle/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/Zilounay/Yeetle/actions/workflows/build.yml)

**Keep it or yeet it.** Yeetle is a free, open-source Windows app that helps streamers and video creators get through the pile of clips left after a stream: watch each clip, tag it, keep or yeet it, then find it again later and drag it straight into Premiere Pro, DaVinci Resolve or any other app.

> **Status: pre-alpha.** The spec is done and the technical proof of concept is next. Nothing is ready to download yet.

## What it will do

- **Review fast.** One clip at a time, keyboard-first: Enter keeps, Del yeets, Shift+→ skips. Tags carry over from the previous clip, so a run of clips from the same game needs no re-tagging.
- **Tag properly.** Games, maps, heroes, friends, moments: your own vocabulary, with rules like "one map per clip".
- **Mark moments.** Drop a marker on the kill or the joke while the clip plays, and see all markers later.
- **Find anything.** A library organized by tags instead of folders: "CS2 + Dust2 + M4" finds those clips in a second.
- **Drag out real files.** Select clips and drag them into your editor, a folder or a browser upload, just like from Windows Explorer.
- **Never lose your files.** Yeetle never touches a file unless you explicitly ask it to delete or rename it, and it backs up its own database automatically.

## Tech

C# on .NET 10, Avalonia 12, SQLite, FFmpeg and a VLC- or mpv-based player (chosen during the proof of concept). Windows first.

The full product and technical specification is in [`docs/SPEC.md`](docs/SPEC.md). Work is tracked in [GitHub Issues](https://github.com/Zilounay/Yeetle/issues), grouped by milestone.

## License

Yeetle is licensed under the [GNU General Public License v3.0](LICENSE). You can use, study, modify and share it; modified versions you distribute must stay open source under the same license.
