# SectorWorks v0.5.3-windows-launch development prerelease

The Windows portable build now opens the SectorWorks window without a second
console window. Extract the ZIP for your architecture and open
`SectorWorks.UI.exe`. Command-line resource verification remains available.

This update retains [WAV export](prerelease-v0.5.1.md) and the
[BIN/CUE Disc Layout](prerelease-v0.5.2.md). Playback and WAV export still
start at INDEX 01 and preserve every sample within the track program.
The layout shows stored pregaps and declared gaps without adding them to a WAV.
No new image-format or write capabilities are introduced. Phase 6 has not
started, and this is not the 1.0 release.

## Full regression results

| Platform | Tests passed | Expected skips | Failures |
| --- | ---: | ---: | ---: |
| Linux x64 | 2,495 | 1 | 0 |
| Windows 11 x64 laptop, standard user | 2,452 | 19 | 0 |

Both runs covered all five solution test assemblies. The Linux skip is the
Windows BCL security reference. The Windows skips are 18 Linux optical
authoring/oracle cases and one Linux XDG case. The Linux reference-copy,
launcher, formatting, and UI-resource checks passed, as did 37 tests across
the four Python harness suites. The outer Linux command reported a termination
after the verification log reached its final PASS marker; independent launcher,
formatting, and resource rechecks also exited successfully.

The Windows suite used the pinned portable QEMU utility and 4,904 successful
Linux fixture-oracle commands on generated media. An additional 294 independent
checks covered all 21 exact Windows mixed-volume source, Save As, and commit
sets. These are not elevated disk or Windows kernel authoring tests.

The release AppImage and Windows x64 ZIP both opened the generated mixed-mode
disc, displayed its indexed layout, and produced byte-identical WAVs through
the desktop folder picker. Independent decoding matched the stored program
sectors, and source hashes stayed unchanged. The Windows package launched one
app window without a console and passed resource verification.

## Packages

Portable Windows ZIPs and Linux tarballs are available for x64 and ARM64,
plus a Linux x64 AppImage and SHA-256 checksums. ARM64 is cross-published only;
no physical ARM64 or elevated Windows testing was performed.
