# POC: FFmpeg thumbnails, recording time and the clip strip (#5)

Throwaway app, not part of `Yeetle.slnx`. It references the main projects and reuses the app's player as it is: `PlayerView`, `VideoOverlayWindow`, `PlayerViewModel`, `ClipNavigator` and the hotkeys. Under the video it adds the thumbnail strip from spec §5. The strip sits below the video and never over it, because the native video window would cover it.

```
dotnet run --project poc/ClipStrip -- "D:\clips" [--ffmpeg <folder with ffmpeg.exe and ffprobe.exe>] [--bench]
```

- FFmpeg is found in `--ffmpeg`, else in the `YEETLE_FFMPEG` variable, else on the PATH.
- Window: every clip of the folder appears as a card with a thumbnail, its name, the recording date marked **file** (read from the video) or **mtime** (fallback to last-modified), and how long ffprobe and ffmpeg took. The current card is highlighted and kept centred. Thumbnails load lazily: the current clip, its 2 neighbours and any card scrolled into view, 2 FFmpeg processes at a time. The header line sums up the timings. Clicking a card does nothing yet, because `ClipNavigator` cannot jump to a given index.
- `--bench`: probes and thumbnails every clip one after another, prints a table and a summary, and writes `bench-*.csv` next to the exe.

## How it works

- `Ffmpeg/ProcessRunner.cs`: runs a tool with input closed, so a prompt can never wait. Output is read as bytes. Past a timeout of 15 s the whole process tree is killed and a `MediaToolException` is thrown.
- `Ffmpeg/MediaTools.cs`:
  - ffprobe JSON gives the duration and `creation_time` (container first, then the video stream). Dates before 2000 count as missing (the MP4 "no date" is 1904).
  - ffmpeg makes one JPEG 320 px wide at 10% of the clip, capped at 10 s, using `-skip_frame nokey -noaccurate_seek`, so it decodes one keyframe and nothing else. If that fails it tries again from 0:00.

## Results so far (2026-10-28, ffmpeg 7.1.1 shared build, NVIDIA ShadowPlay clips, 1440p60 H.264, local SSD)

| | avg | median | p95 | max |
| --- | --- | --- | --- | --- |
| ffprobe (date + duration), 20 clips | 183 ms | 190 ms | 286 ms | 286 ms |
| ffmpeg thumbnail, 20 clips | 308 ms | 314 ms | 478 ms | 478 ms |

- Starting the process alone costs about 85 ms for each call (`ffprobe -version`), so roughly half of the probe time is startup. A static ffmpeg build may start faster.
- Hardware decoding (`-hwaccel d3d11va`) was about twice as slow for a single frame: device setup outweighs the one decode.
- Conclusion: about 0.5 s per clip when nothing is cached. So "thumbnails visible without a placeholder" (spec §16) needs both the cache and the background preparation from §7. The probe should run during the scan, not when the card appears.

**Recording time.**
- ShadowPlay mp4: 20 / 20 had `creation_time` in the file. It matches the save time (the end of the replay), not when the moment happened.
- MKV: the date is read when the file carries one (`DateUTC`, which ffprobe reports as `creation_time`). An ffmpeg remux from mp4 to mkv dropped the date, and those files correctly fell back to mtime.
- **Not checked yet: real OBS mp4 and mkv recordings.** None were on this machine.

**Broken files (all clean errors, none hung):**

| File | Result |
| --- | --- |
| mp4 cut at 3 MB (`moov` at the end) | fails in about 50 ms: "moov atom not found" |
| random bytes, text renamed .mp4, empty .mkv | fails in about 50–100 ms with ffmpeg's reason |
| mkv cut at 3 MB / 20 MB (crash during recording) | **works**: the thumbnail comes from 0:00 after the first seek fails (the header still claims the full duration) |
| mkv with 5 MB of garbage in the middle | works |
| never-ending input with a 2 s timeout | killed after about 2.1 s, no ffmpeg process left over |
| ffmpeg missing | "Cannot start …, pass --ffmpeg" |
