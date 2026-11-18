# SectorWorks v0.5.1-wav-export development prerelease

This update adds lossless WAV export for CD audio in supported BIN/CUE images.
It does not change which images SectorWorks can open, start Phase 6, or ship 1.0.
Earlier work and limitations are documented in the
[v0.5.0 prerelease report](prerelease-v0.5.0.md).

## Export CD audio

1. Open the `.cue` file (not its `.bin` companion).
2. In **Track Information** or **Audio Player**, click **Export Audio Tracks…**.
3. Check the tracks you want or use **Select All**, then choose a folder.

SectorWorks saves one 44.1 kHz, 16-bit stereo PCM WAV per track. File names keep
the original track number, such as `Track 02.wav` for audio after track 01 data.
`BINARY` sample bytes are unchanged; `MOTOROLA` samples are byte-swapped for WAV
without changing their values. The export does not record playback and is
unaffected by playback volume or mute. BIN/CUE sources and existing output files
are never overwritten. If a batch is interrupted, completed WAV files remain;
SectorWorks reports the result and removes unfinished output.

Only the track program area is saved. Gaps, data tracks, and disc metadata are
not exported. Pre-emphasis and four-channel tracks are not selectable. Physical
disc ripping and new image formats are outside this release.

## Verification

- Added storage/service and rendered UI coverage for selected/all export,
  mixed-mode boundaries, byte order, unsupported tracks, an existing output,
  cancel before publication, and cancellation after a completed track.
- Linux x64 and standard-user Windows 11 x64: the packaged AppImage and Windows
  ZIP both exported all three audio tracks from a generated mixed-mode image
  with a data-first shared BIN, stored gaps, and a separate big-endian member.
  An independent WAV reader confirmed the expected samples; both platforms'
  WAV files matched byte-for-byte, and all source hashes stayed unchanged.
- Linux: 76 focused tests passed, zero failures or skips (37 storage/CUE,
  4 track UI, 33 services/platform extraction, 2 extraction UI). The Release
  build and targeted formatting checks passed. The full regression suite was
  not rerun.
- Self-contained Windows and Linux packages are available for x64 and ARM64,
  plus a Linux x64 AppImage and SHA-256 checksums. ARM64 is cross-published
  only; no macOS package or elevated testing.
