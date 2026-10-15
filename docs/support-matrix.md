# Support matrix

This matrix describes the implemented reader subset, not blanket support for
every variant, feature or corrupt image. Phase 2 staged editing/Save As is
qualified separately below; direct commit and rebuild remain disabled.
Reader safety limits and detailed exclusions are documented
in [storage implementation](storage-phase1.md) and [explorer operations](explorer-operations.md).

| Container/layout | Read-only inspection | Browsing/extraction |
| --- | --- | --- |
| Raw filesystem-only IMG | Size, assumed 512-byte sector, virtual hex | Supported FAT12/16/32 or ordinary NTFS |
| Raw MBR | Primary entries, active/type/extents, unallocated metadata | Supported filesystems in valid primary partitions |
| Raw GPT | Primary/entry CRCs, usable extents, GUID/name, backup warnings | Supported filesystems in valid partitions |
| Fixed/dynamic VHD | Separate virtual/container sizes, structural checks | Same supported disk filesystem subset |
| Fixed/dynamic VHDX | Sector sizes and virtual/container metadata | Same supported disk filesystem subset; fixed test is synthetic |
| ISO9660 | Volume and descriptor metadata | Ordinary files/directories |
| Joliet | Extension selected by descriptor escape sequence | Unicode filenames, ordinary files/directories |
| Rock Ridge | Ordinary supported SUSP metadata | Ordinary files/directories; no relocated/multi-extent/interleaved layouts |
| ISO/UDF bridge | UDF recognition noted | Independently recognized ISO view only |
| UDF-only | Detection, unsupported-browsing warning | Unavailable |
| Differencing VHD/VHDX | Parent requirement diagnostic | Rejected; no automatic parent traversal |
| Unknown raw layout | Metadata/virtual hex with explicit warning | Unavailable unless a supported filesystem is independently recognized |
| BIN/CUE / CD-DA | Phase 6 reserved structure only | No parsing, browsing or audio playback |

MBR extended-chain traversal, GPT backup-only recovery, VHDX active-log replay,
filesystem repair and source-image write paths are unavailable.

## Staged editing

See [Phase 2 staged editing](staged-editing.md) for the independently qualified
raw/FAT subset, Save As validation, resource limits and failure behavior.
VHD/VHDX, NTFS and optical reader support does not imply editing support.
Sources always remain read-only; no direct-commit, ISO rebuild, bootability or
exact-layout preservation capability is advertised.

| Stack | Stage add/replace/delete/rename/create directory | Save As new image |
| --- | --- | --- |
| Unpartitioned conventional 1,474,560-byte FAT12 raw, qualified DOS 8.3 tree | Initial qualified envelope | Validated raw copy |
| Unpartitioned conventional FAT16 raw, qualified DOS 8.3 tree | Initial qualified envelope | Validated raw copy |
| Unpartitioned conventional FAT32 raw, qualified DOS 8.3 tree | Initial qualified envelope | Validated raw copy |
| Raw MBR/GPT, even with readable FAT | Disabled; partitioned writer not qualified | Disabled |
| 360K/720K or early-DOS FAT12 raw | Disabled; existing read-only compatibility retained | Disabled |
| FAT with long filenames or unsupported BPB/geometry | Disabled; reader remains available where supported | Disabled |
| VHD/VHDX, NTFS, ISO/Joliet/Rock Ridge/UDF | Disabled | Disabled |

The writing envelope additionally requires 512-byte sectors, two mirrored FATs,
extended BPB/signature, exact declared image size and zero hidden sectors.
Read-only restrictions are explained by capabilities, not guessed from file
extensions. DOS 8.3 basenames/extensions may independently be uppercase or
lowercase; display case is preserved and lookup/conflicts are case-insensitive.
Case-only file/directory renames are supported. Mixed case within a component,
live long-name records (even redundant aliases), metadata editing and general
long-name writes remain unsupported.

## Filesystem and entry restrictions

FAT uses validated read-only chains, LFN checksums/sequence, CP437 short names,
and bounded directory ancestry. Attributes/short names are exposed; timestamps
have no inferred UTC offset because FAT does not store one.
Validated early-DOS FAT12 floppy layouts can be read without a 0x55AA trailer,
with an explicit warning. This exception requires exact size, recognized floppy
geometry and matching FAT copies; it does not permit unsigned modern layouts or
establish bootability. Legacy labels are read from root-directory metadata.

NTFS supports ordinary resident/nonresident streams, Unicode names and basic
attributes/UTC timestamps after bounded structural preflight. Sparse, compressed,
encrypted and link/reparse content, alternate streams and attribute-list
continuation records are rejected explicitly. An entry being listed does not
establish that every feature of its contents can be read. NTFS formatting fails
on Linux due to the dependency's Windows SID APIs and is not enabled anywhere.

For all supported entries: bounded plain-text preview and paginated hex are
available without execution; recursive filename search is cancellable; extraction
streams to exclusive host files and reports unsupported entries/host names.
No exact-layout, bootability or audio-preservation guarantee is claimed.

The missing-first-hex-page issue reported on 2026-10-14 was reproduced and fixed:
tab selection now loads bytes without requiring Next/Previous, including for
single-page files. Binary/text encoding does not gate hex display. Regression
tests cover empty/short/exact-page/multipage files and late text-preview results;
the supplied LEVEL01.OPL also passed local physical testing. See
[platform status](platform-status.md) for evidence and the earlier owner-reported
Linux x64, Windows x64 and Raspberry Pi 500 Linux ARM64 results. macOS runtime
testing is explicitly deferred per owner; macOS remains an intended target.
The earlier launch report remains unresolved.

## Evidence and future work

### Retro-floppy priorities

360K (368640-byte conventional raw images) and 720K (737280 bytes) are explicit
retro-computing priorities. The current 720K evidence covers the supplied Compaq
FAT12 layout and synthetic legacy fixtures, not every 720K variant. Dedicated
360K qualification and wider 720K/early-DOS compatibility are planned under
Phase 7, item 7.10 of the [checklist](../checklist.txt). Existing read-only 720K
support remains enabled; this future work is not a reason to remove it.

Fixture coverage, ambiguous/missing BPBs, geometry/media-descriptor checks,
independent extraction verification and preservation/write qualification are
tracked there. Capacity alone is not a format detector; track/sector-structured
and flux captures are not implicitly covered by raw IMG support.

Storage tests cover immutable normal/damaged goldens, disposable fixed/dynamic
media, malformed partition/metadata paths, Unicode names, FAT cycles and huge
virtual addresses. Service tests independently exercise open/browse/preview/
search/extract and unchanged source hashes for all nine normal goldens.

Fixed-VHDX testing uses a fully allocated, fixed-flagged disposable image because
the upstream stream-based fixed authoring API is unimplemented. Independent-tool
interoperability remains unverified. Physical platforms, GUI and cross-publish
results are tracked in [platform status](platform-status.md).

The owner approved DiscUtils 0.16.13 for development. Release-quality work will
revisit its maintenance, alternatives and the NTFS limitations; no dependency
change may skip phase-specific safety or preservation verification.
