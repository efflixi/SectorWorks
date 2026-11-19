# SectorWorks v0.5.4-compat development prerelease

## Compatibility

- CUE sheets may contain one initial `REM SESSION 1` or `REM SESSION 01`
  marker. CUE companion, track layout and raw-sector checks still apply.
- Standalone ISO and qualified raw ISO tracks accept two specific patterns
  of missing geometry mirrors and a verified one-block parent-length
  placeholder. The rebuild preserves the geometry bytes and writes the
  actual parent length. The geometry exception is limited to single-volume
  constants. Directory and file extent copies still have to agree.
- FAT editing accepts apostrophes in existing short names. FAT12 Save As
  preserves standard bad-cluster markers and their cluster bytes.
- A primary MBR FAT16 partition with a short suffix can qualify for Save As.
  The output validation covers the suffix as part of the unchanged disk
  regions. FAT32 allocation validation can use the whole supported
  8 MiB FAT table while retaining the per-chain bound. The newly qualified
  bad-cluster, suffix and large FAT32 profiles offer Save As only.

A read-only check covered all 176 available files from the previous
compatibility report. 6 gained editing and 7 additional CUE
sheets open. Some titles progressed to another blocker. The largest groups
still include opaque producer fields, CloneCD sidecars, associated-file
records and nonordinary raw sectors. Large editable images may take longer to
open because establishing the source-integrity baseline reads them twice.

## Verification

| Platform | Checks | Passed | Skipped |
| --- | --- | ---: | ---: |
| Linux x64 | Focused storage, including optical and disk Save As/commit matrices | 418 | 0 |
| Linux x64 | Complete service and UI suites | 294 | 0 |
| Windows 11 x64 | Focused FAT, CUE and audio tests | 50 | 4 |

The four Windows skips require Linux optical tools. A follow-up Linux run
verified the additional apostrophe-alias replacement assertion. A generated
1,113,048-cluster FAT32 image passed Save As, independent `fsck.fat` and
`mtype` checks, and a source digest comparison. Formatting and all five
authoritative specification and design copy checks passed.

The packaged x64 Linux tarball, AppImage and Windows portable build passed
their UI resource verifiers. The packages for both architectures include
license notices and the Windows executables use the GUI subsystem.
Windows tests used the laptop's standard account. ARM64 packages were
cross-published and architecture-checked.

## Downloads

Extract the Windows ZIP or Linux tarball for your architecture. Open
`SectorWorks.UI.exe` on Windows or run `./SectorWorks.UI` on Linux.
Linux x64 also has an AppImage. Check package digests with `SHA256SUMS.txt`.

Phase 6 remains pending. See the [support matrix](support-matrix.md) for the
current scope and remaining format limitations.
