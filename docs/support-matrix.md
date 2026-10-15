# Support matrix

This matrix describes the implemented reader subset, not blanket support for
every variant, feature or corrupt image. Partial Phase 2B staged editing/Save As is
qualified separately below; direct commit and rebuild remain disabled.
Reader safety limits and detailed exclusions are documented
in [storage implementation](storage-phase1.md) and [explorer operations](explorer-operations.md).

| Container/layout | Read-only inspection | Browsing/extraction |
| --- | --- | --- |
| Raw filesystem-only IMG | Size, assumed 512-byte sector, virtual hex | Supported FAT12/16/32 or ordinary NTFS |
| Raw MBR | Primary entries, active/type/extents, unallocated metadata | Supported filesystems in valid primary partitions |
| Raw GPT | Primary/entry CRCs, usable extents, GUID/name, backup warnings | Supported filesystems in valid partitions |
| Fixed/dynamic VHD | Separate virtual/container sizes, structural checks | Same supported disk filesystem subset |
| Fixed/dynamic VHDX | Sector sizes and virtual/container metadata | Same supported disk filesystem subset; synthetic and independent fixed fixtures |
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

See [staged editing](staged-editing.md) for the independently qualified
Phase 2B FAT subset, Save As validation, resource limits and failure behavior.
NTFS and optical reader support does not imply editing support.
Sources always remain read-only; no direct-commit, ISO rebuild, bootability or
exact-layout preservation capability is advertised.

| Stack | Stage add/replace/delete/rename/create directory | Save As new image |
| --- | --- | --- |
| Raw filesystem-only/primary MBR/GPT, qualified conventional FAT12/16/32 | Qualified short/LFN/mixed-case/NFC BMP subset | Validated same-layout raw copy |
| Fixed/dynamic VHD, filesystem-only/primary MBR/GPT, qualified FAT | Qualified separately per variant/layout/filesystem | Validated same-variant VHD |
| Fixed/dynamic VHDX, filesystem-only/primary MBR/GPT, qualified FAT | Qualified separately, 512/512 and 512/4096 | Validated same-variant VHDX |
| 360K/720K or early-DOS FAT12 raw | Disabled; existing read-only compatibility retained | Disabled |
| FAT outside qualified naming/metadata/BPB/geometry bounds | Disabled; reader remains available where supported | Disabled |
| Ordinary NTFS in any required container/layout | Blocked: portable existing-volume ACL inheritance | Disabled |
| ISO/Joliet/Rock Ridge/UDF | Disabled until Phase 4 | Disabled |

The writing envelope additionally requires 512-byte sectors, two mirrored FATs,
extended BPB/signature, exact bounded-volume size and the correct absolute
hidden-sector count (zero for filesystem-only images).
Read-only restrictions are explained by capabilities, not guessed from file
extensions. DOS 8.3 basenames/extensions may independently be uppercase or
lowercase; display case is preserved and lookup/conflicts are case-insensitive.
Case-only file/directory renames and checked LFN/mixed-case/NFC BMP Unicode
are supported. Metadata editing, unqualified names and arbitrary feature
preservation remain unsupported. Independent volumes have independent
capabilities and a shared ordered staged history.

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
Separately, ordinary existing-volume Add/mkdir fail in ACL inheritance on Linux;
other successful operation probes do not qualify a complete portable NTFS writer.
See the [concrete blocker](dependency-evaluation.md#phase-2b-existing-volume-ntfs-blocker-2026-10-15).

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

### Owner-approved 1.0 read-only boundary

The roadmap targets 1.0. VMDK, VDI, QCOW2, DMG, ext2/ext3/ext4, exFAT,
HFS+ and APFS target qualified **read-only subsets where feasible**, not
implemented or blanket read support. Phase 7 must evaluate variants/features,
obtain legal fixtures and test browsing/extraction. Detection-only is not those
capabilities. Record exact limitations and concrete reader blockers for owner
review; infeasibility does not silently delete a reader requirement.

Modification/staging, modified-image Save As, direct commit, rebuild, creation
and formatting for these additions are outside 1.0 requirements. They are not
unassigned release-blocking writer gaps and need no invented future write
milestone or delivery date. Core raw/VHD/VHDX FAT/ordinary-NTFS, optical and retro
write milestones remain required.

Apply caps per stack: a read-only container caps FAT/NTFS; ext/exFAT/HFS+/APFS
stay read-only in writable raw/VHD/VHDX. Internal parsers, not host mounting,
determine application support. Record platform limitations only from evidence.

### Required editing delivery roadmap

#### Phase 2B qualification ledger (implementation authorized 2026-10-15)

`Qualified` means the bounded FAT subset described above, with independent
operation/preservation checks, not every reader-supported variation.
`Blocked` means mandatory NTFS delivery remains unresolved and disabled.
Layouts must be structurally valid; **Phase 2B is not complete**.

| Container variant | Layout | FAT12 | FAT16 | FAT32 | Ordinary NTFS |
| --- | --- | --- | --- | --- | --- |
| Raw | Filesystem-only | Qualified | Qualified | Qualified | Blocked |
| Raw | Primary MBR | Qualified | Qualified | Qualified | Blocked |
| Raw | GPT | Qualified | Qualified | Qualified | Blocked |
| Fixed VHD | Filesystem-only | Qualified | Qualified | Qualified | Blocked |
| Fixed VHD | Primary MBR | Qualified | Qualified | Qualified | Blocked |
| Fixed VHD | GPT | Qualified | Qualified | Qualified | Blocked |
| Dynamic VHD | Filesystem-only | Qualified | Qualified | Qualified | Blocked |
| Dynamic VHD | Primary MBR | Qualified | Qualified | Qualified | Blocked |
| Dynamic VHD | GPT | Qualified | Qualified | Qualified | Blocked |
| Fixed VHDX | Filesystem-only | Qualified | Qualified | Qualified | Blocked |
| Fixed VHDX | Primary MBR | Qualified | Qualified | Qualified | Blocked |
| Fixed VHDX | GPT | Qualified | Qualified | Qualified | Blocked |
| Dynamic VHDX | Filesystem-only | Qualified | Qualified | Qualified | Blocked |
| Dynamic VHDX | Primary MBR | Qualified | Qualified | Qualified | Blocked |
| Dynamic VHDX | GPT | Qualified | Qualified | Qualified | Blocked |

Qualification bounds: conventional 512-byte FAT sectors, FAT12
1,474,560-byte volumes only (360K/720K excluded); FAT16/32 within the existing
64 GiB volume, 8 MiB FAT and 1,048,576-cluster limits. Primary MBR and consistent
primary/backup GPT only; partition extents cannot change. VHD uses 512-byte
logical sectors. VHDX 512/512 has 18 FAT rows above; 512/4096 has another 18
independently checked rows (fixed/dynamic, three layouts, three FAT variants).
Together with raw/VHD this is 63 FAT matrix rows. VHDX 4096/4096 is explicitly
rejected; no 4Kn writer is qualified. The naming subset is defined in the
[editing guide](staged-editing.md#current-phase-2b-envelope-and-mandatory-blocker).
Sparse/compressed/encrypted/reparse/ADS/attribute-list
NTFS features, differencing parents, active-log replay and repair stay gated.
Every newly enabled cell requires all five operations, history/discard,
multi-volume isolation, metadata/allocation and unchanged-region checks, failure
injection, and source hash evidence. Direct commit remains disabled throughout.

The owner approved this documentation correction on 2026-10-15, including a
separate **Phase 2B before Phase 3**, then authorized its implementation.
The ledger reports actual partial delivery; the roadmap retains required
NTFS work rather than treating evaluation as implementation.
The [checklist](../checklist.txt) is authoritative for task status and review gates;
the [technical specification](specifications/Cross_Platform_Disk_Image_Workspace_Tech_Spec_v0.3.txt)
section 6.6 records the same delivery policy.

| Required stack/subset | Staged editing and validated new output | Source commit | Reconstruction |
| --- | --- | --- | --- |
| Qualified unpartitioned raw FAT12/16/32 | Phase 2 verified; Phase 2B broadens naming | Phase 3 item 3.7 | Normally not needed; copy-and-apply Save As |
| Conventional FAT12/16/32 with qualified LFN/mixed-case/Unicode | Phase 2B FAT delivered, bounded subset | Phase 3 item 3.7 on qualified disk stacks | Normally not needed |
| Raw primary MBR/GPT with qualified FAT or ordinary NTFS | FAT delivered; NTFS blocked, items 2B.3/2B.6 | Phase 3 item 3.7 | Normally not needed; preserve partition layout |
| Fixed VHD and dynamic VHD, each filesystem-only/MBR/GPT + FAT/ordinary NTFS | FAT delivered per variant; NTFS blocked, items 2B.4/2B.6 | Phase 3 item 3.7 | Normally not needed; retain variant/virtual size |
| Fixed VHDX and dynamic VHDX, each filesystem-only/MBR/GPT + FAT/ordinary NTFS | FAT delivered per variant/sector subset; NTFS blocked, items 2B.5/2B.6 | Phase 3 item 3.7 | Normally not needed; validate container metadata/log state |
| Ordinary NTFS in filesystem-only raw | Blocked, item 2B.6; no writer enabled | Phase 3 item 3.7 | Normally not needed; formatting is separate |
| ISO 9660/Joliet/supported Rock Ridge | Phase 4 items 4.2/4.5 | Not an in-place filesystem-write target | Phase 4 validated rebuild; source replacement requires Phase 3 safeguards |
| Declared UDF and ISO/UDF bridge subsets | Phase 4 item 4.6, including UDF reads beyond detection | Not an in-place filesystem-write target | Phase 4; preserve consistent bridge namespaces |
| Qualified BIN/CUE data and mixed-mode layouts | Phase 6 items 6.8-6.9 | Not an in-place track-write target | Phase 6 validated new output sets; source-set replacement requires recovery safeguards |
| Conventional 360K/720K FAT12 subsets | Phase 7 item 7.10 | Phase 7 item 7.10 using Phase 3 safeguards | Normally not needed for raw file edits; create/conversion separate |

Disk coverage applies to structurally valid combinations with declared resource,
sector-size and feature bounds, not every variation an upstream library recognizes.
Each container/layout/filesystem row needs separate operation, allocation,
metadata, source-integrity and failure evidence. FAT LFN support must preserve
short aliases and display spelling; NTFS modification does not require or imply
NTFS formatting. Differencing parents, VHDX active-log replay, filesystem repair
and partition movement/resizing remain outside this expansion.

Required delivery cannot be closed by evaluating a library or proving one stack.
An unsafe/unavailable writer remains blocked until verified or explicitly
scope-approved by the owner with a named follow-up milestone. Save As-only scope
is not an automatic exemption from a scheduled direct-commit requirement.
Bootability and exact-layout preservation still require independent proof.
Phase 7 item 7.1 and release closure accept the intentional 1.0 read-only
boundary above without requiring future writers. Required reader blockers and
required core/optical/retro writer gaps still require explicit owner review.

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

The historical synthetic fixed-VHDX reader fixture remains separate from new
independently qemu-authored fixed/dynamic VHDX write fixtures and independent
output checks. Physical platforms, GUI and cross-publish
results are tracked in [platform status](platform-status.md).

The owner approved DiscUtils 0.16.13 for development. Release-quality work will
revisit its maintenance, alternatives and the NTFS limitations; no dependency
change may skip phase-specific safety or preservation verification.
