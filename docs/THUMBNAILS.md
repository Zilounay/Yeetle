# Thumbnails and FFmpeg (deferred)

**Status (2026-10-30, #5):** thumbnails are postponed to "Later" (SPEC §2). They do not add enough value to justify shipping FFmpeg with the MVP. Without FFmpeg, a clip's recording date is its file's last-modified date and its duration comes from LibVLC (SPEC §4).

The POC that answered the technical questions stays in `poc/ClipStrip` so the work can restart from it. Its README has the measurements: about 0.5 s per clip uncached (ffprobe about 180 ms, one thumbnail about 310 ms), clean errors and no hangs on broken files.

## Why it was postponed: shipping FFmpeg

Users must not have to download FFmpeg themselves, so it would ship inside the Yeetle install. The problem is size: the full static build used for testing (gyan.dev 9.0.2 "full_build") has an `ffmpeg.exe` and an `ffprobe.exe` of about 228 MB each, about 455 MB for the pair. A static full build contains every codec and filter twice, once per exe, and Yeetle needs a tiny part of it. `ffplay.exe` would never ship.

## Options for when thumbnails come back

From smallest install to largest:

1. **Custom minimal build (recommended).** Compile FFmpeg with only what Yeetle needs: H.264, HEVC, AV1 and VP9 decoders, the mov/mp4 and matroska demuxers, the mjpeg encoder for the JPEG thumbnails, and the scale filter. Both exes usually end up a few MB each. Build it once in a GitHub Actions job (a cross-compile script or vcpkg) and store the result as a release asset that the packaging step pulls in. Most work up front; small install and small updates.
2. **Shared build.** `ffmpeg.exe` and `ffprobe.exe` become tiny and use one set of DLLs, so the codecs are not paid twice. Still 100+ MB for a "full" shared build, less for "essentials". No build work, large download.
3. **Download on first run.** Small installer, but it needs the network, a download step, checksum checks and handling of failures, and thumbnails would not work offline at first start. Not worth it while option 1 exists.

Velopack sends only what changed between versions, so whatever is bundled is a one-time cost, not one per update.

**Alternative without FFmpeg:** LibVLC already ships and can take snapshots. But a snapshot needs the clip to actually play into a video output, which is slower and fiddlier than one FFmpeg call. That is why SPEC §13 originally chose FFmpeg processes.

## Licensing

Yeetle is GPL-3.0, so an LGPL or GPL FFmpeg build is fine (SPEC §12). Shipping it means:

- FFmpeg's license text goes with the other license notices.
- The exact FFmpeg source and build configuration used must be available, for example by linking the source tag and the build script from the notices. A custom build in CI covers this naturally, since the script lives in the repo.

## Code to change when bringing it into the app

`MediaTools.Locate` in `poc/ClipStrip/Ffmpeg/MediaTools.cs` looks in the `--ffmpeg` folder, then `YEETLE_FFMPEG`, then the PATH. The app's version should look first in a folder next to the exe (for example `AppContext.BaseDirectory\ffmpeg\`) and only then fall back to the PATH.

When thumbnails return, also restore the parts of the spec that were cut for #5: the thumbnail strip (§5), background thumbnail preparation and cache (§7, §10, §11), the Explorer grid (§8), and the FFmpeg rows in §12, §13 and §16. Recording date could then come from the file again (`creation_time`), but the POC showed that ShadowPlay writes the save time there, which is close to the last-modified date anyway.
