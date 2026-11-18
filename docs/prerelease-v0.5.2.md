# SectorWorks v0.5.2-disc-layout development prerelease

Open a supported BIN/CUE image and choose **Disc Layout** in the lower pane.
It is also available from **Track Information** and **Audio Player**. The table
shows data and audio tracks in disc order, one row per region:

- **INDEX 00** is a stored pregap, not necessarily silence.
- **INDEX 01** marks the program start; **INDEX 02 and later** are subdivisions
  within the program.
- **PREGAP** and **POSTGAP** are gaps declared in the CUE without stored sectors.

Every row gives its start and length in sectors, relative to the layout
represented by the image. Stored regions also show the companion file and
its file-relative CUE time, which can reset when the source file changes.
This is not a reconstruction of the original disc's lead-in or subchannel
indexes absent from the image.

Track playback and [WAV export](prerelease-v0.5.1.md) are unchanged: both use the
existing track program area beginning at INDEX 01, keep any silence inside
that area, and exclude pregaps and postgaps. Continuous disc playback was not
added, and Phase 6 has not started.

## Verification

- 57 focused tests passed: 20 rendered/navigation UI tests and 37 CUE and
  WAV-export storage tests. The Release build and targeted formatting passed.
  The full regression suite was not rerun.
- The packaged Linux x64 AppImage and standard-user Windows x64 app showed
  the expected eight ordered regions of a generated mixed-mode disc,
  including per-file offsets, INDEX 02, and stored versus unstored gaps.
- WAVs exported by the Linux package matched the stored program sectors
  byte-for-byte when decoded independently. Both INDEX 02 subdivisions were
  retained; the stored pregap and declared gaps stayed out of the WAVs.
  The input hashes remained unchanged on both platforms.
- Self-contained packages target Windows and Linux x64 and ARM64, plus a
  Linux x64 AppImage. ARM64 is cross-published only. No UAC was needed.
