# SectorWorks v0.5.5-cd-metadata development prerelease

- Open an image by dropping it onto the Open toolbar button, workspace tabs,
  or empty workspace. Opening an image without edit or rebuild support shows
  a compact list of the reasons.
- Choose **Look up track names…** in **Disc Layout** to query MusicBrainz.
  Confirm a matching edition to fill missing audio track labels for that
  workspace. A data track at the start of a supported single-session disc
  retains its track number.
- WAV export uses available track titles in filenames, with a numbered
  fallback. Export writes the same track program PCM to WAV and protects
  existing files from overwriting.

## Downloads

Extract the Windows ZIP or Linux tarball for your architecture and open
`SectorWorks.exe` or `./SectorWorks`. The portable launcher sits alongside
`libs/`, `docs/`, and `licenses/`. Windows uses a graphical launcher.
Linux x64 also has an AppImage. Verify downloads with `SHA256SUMS.txt`.

## Verification

| Linux x64 solution suite | Passed | Skipped |
| --- | ---: | ---: |
| Core | 14 | 0 |
| Dependencies | 27 | 0 |
| Services | 216 | 0 |
| Storage | 2,175 | 1 |
| UI | 85 | 0 |
| **Total** | **2,517** | **1** |

The one expected skip requires the Windows BCL security reference. The
locked build, formatting, launcher, UI resource, and five specification and
design copy checks passed. The four local Python validation suites passed
all 37 tests. The verification script reached its final PASS marker.
The outer shell returned 143 after the checks had logged their results.
Independent launcher, document-copy, format, and resource rechecks exited
successfully.

The Linux x64 tarball and AppImage passed resource checks after extraction.
In a nested X11 session, the AppImage opened a CUE dropped from Nemo and
showed the read-only reasons. A live MusicBrainz lookup of a generated
data-first disc layout labelled audio track 02 after confirmation. The
native folder picker saved a titled WAV. Independent decoding matched all
319,923,744 PCM bytes against the generated track, and the source hashes
stayed the same.
The same AppImage staged a dropped file and a pasted file in a generated
FAT12 subfolder. Save As preserved the source hash. Independent
`fsck.fat -n` and `mtype` checks verified the saved image and both files.

The owner reported successful Windows work-PC smoke testing of the UI,
packaging, and metadata review builds. The laptop was unavailable for this
regression. Windows and ARM64 packages were cross-published and checked for
archive contents, CPU architecture, and Windows GUI subsystem where
applicable. Physical ARM64 execution remains deferred.

See the [support matrix](support-matrix.md) for current format limits.
Phase 6 remains pending.
