# POC A: VLC native view baseline (#3)

Throwaway app: LibVLCSharp 3.10.2 with LibVLC 3.0.24 on Avalonia 12.1.4. Not part of `Yeetle.slnx`.

The video is a VLC child window embedded in the Avalonia window through `NativeVideoView` (a small `NativeControlHost`). LibVLCSharp.Avalonia's `VideoView` is built for Avalonia 11; on 12 it never hands its window to VLC, so VLC opens a separate top-level window instead.

```
dotnet run --project poc/VlcBaseline -- "D:\clips" [--preload] [--benchmark] [:vlc-option=value ...]
```

- No flags: one player; switching loads the next clip into it.
- `--preload`: two stacked video views; the next clip is loaded in the hidden one, paused on its first frame (`:start-paused`), and a switch shows that view and resumes it. Only "next" is preloaded; "previous" falls back to a normal load.
- `--benchmark`: opens every clip in turn (2–3 laps), cycling speeds 1x–4x and trying every audio track, writes `benchmark-*.csv` next to the exe, prints a summary and exits.
- `:option` arguments are passed to VLC for each clip, e.g. `:file-caching=50` or `:no-audio`.

The bar under the video shows the position; click or drag it to seek.

Keys: ← → switch clip, Space pause, 1–6 speeds (1x, 1.25x, 1.5x, 2x, 3x, 4x), B next audio track, M mute. The bar has pickers for the audio track and the audio output device; the line under the clip name shows the actual audio state.

## What the numbers mean

- **clock_ms**: time from the key press until VLC's playback clock moves. In single-player mode this is the best measure of "new clip on screen": traced screen captures showed the new picture appears at the same moment, while VLC's `Vout` event (≈ 70 ms) only means its window was created.
- **picture_ms** (preload mode): when the preloaded view was made visible.
