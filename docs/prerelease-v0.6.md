# SectorWorks 0.6 development prerelease

- Browse and extract files from supported VMDK, VDI, QCOW2, DMG and
  DiskGenius PMFX images, and from supported ext2/3/4, exFAT, HFS+ and APFS
  filesystems.
- Open qualified Alcohol MDS/MDF, CloneCD CCD/IMG/SUB, Nero NRG,
  single-track raw CD data, ImageDisk IMD and Norton Ghost GHO layouts.
  Qualified audio tracks support playback and lossless WAV export.
- Reopen additional CUE, ISO 9660 and legacy FAT12 layouts. Qualified
  conventional 360 KB and 720 KB raw FAT12 images support staged edits,
  Save As and direct commit.

The [Phase 6 report](phase6-additional-formats.md) lists the exact supported
variants and retained restrictions. PMFX browsing and extraction are available
for the observed subset. The independent DiskGenius restore comparison and
NTFS PMFX coverage remain open. The legacy C1.GHO sample remains unsupported.

## Downloads

Choose the Windows ZIP or Linux tarball for your CPU, extract it, and open
`SectorWorks.exe` or `./SectorWorks`. The portable launcher sits alongside
`libs/`, `docs/` and `licenses/`. Linux x64 also has an AppImage.
Compare the downloaded file against `SHA256SUMS.txt`.

## Verification

The Phase 6 regression on Linux x64 passed **2,552 tests** with one
Windows-only skip across the initial run and three disjoint storage reruns.
The Windows x64 laptop passed focused and desktop checks. Its unfiltered
suite remained incomplete because the default environment lacked several
fixture tools and required output configuration. Physical ARM64 execution
remains deferred.

The 0.6 packages use the verified Phase 6 source with application version
metadata set to `0.6.0-dev`. The full regression was reused for this
prerelease. Release verification covers a locked build, the four
self-contained targets, package integrity and architecture, and Linux x64
resource smoke checks.
