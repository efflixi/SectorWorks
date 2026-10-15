# Phase 1 read-only storage

This document describes the retained reader pipeline. Phase 2's separate
overlay and Save As writer are documented in [staged editing](staged-editing.md);
read support here never implies write qualification.

## Implemented Phase 2C NTFS reader subset and open qualification

On **2026-10-15** the owner authorized Phase 2C and the synchronized roadmap/
specification change before Phase 3. **Bounded reader implemented; remaining
1.0 verification/review gates open, not phase complete; stop for owner review.**
The owner subsequently deferred authentic NT4/Windows 2000/XP-created-media
qualification and dependent checks to post-1.0 **P1.NTFS.1-P1.NTFS.6** in the
[checklist](../checklist.txt). These are not passed tests or 1.0 blockers;
VM-authored fixtures suffice, without vintage hardware. Automated tests, safety
bounds, source integrity and general platform verification remain required,
with the existing separate macOS runtime deferral unchanged.
Implemented-subset evidence is recorded below,
not full version/media qualification. The retained reader
baseline below still describes prior evidence. Targets are separate bounded
**NTFS 1.2 (NT4), 3.0 (Windows 2000) and 3.1 (XP+)** ordinary read subsets.
No authentic NT4 fixture exists; synthetic version-field tests alone cannot
establish authentic legacy support.

Implemented reading covers bounded ordinary base/extension records, supported resident/
nonresident attribute lists and split data/index streams, plus version-specific
metadata layouts. A separate `NtfsReadValidation` preflight/resolver feeds the
unchanged signed reader; strict `NtfsValidation.Validate` defaults remain unchanged
for the writer. Its diagnostic now describes the Phase 2B writer subset/Phase 3,
not the obsolete Phase 1 boundary. Reused
`ValidateRecord`/`ValidateRuns` checks require explicit read-only extension-record
opt-in, not a permissive shared default. Attribute-list-dependent MFT bootstrap
is explicitly rejected: safe bootstrap mapping is not implemented, so extension
resolution must not assume the MFT mapping it is trying to establish. This exact
blocker excludes those images rather than claiming full bootstrap/list support. The observed
version is exposed as `fs.Properties["NTFS version"]`, separately from authentic
legacy fixture provenance. Resolution validates
file-reference sequences, base ownership, attribute identity/type/name, list/
run bounds, VCN ordering and complete supported extent coverage, with checked
arithmetic and bounded reads/traversal. Cycles, stale references, duplicates,
overlaps, missing extents and invalid metadata fail explicitly.
Do not broaden sparse/compressed/encrypted/reparse/link/ADS claims merely by
reading an attribute list, replay/reset a log, repair metadata or remove budgets.

Checklist **2C.1-2C.7** requires legal normal/malformed fixtures, accurate synthetic/
observed provenance, browse/preview/hex/search/extraction with independent comparisons
and unchanged source hashes, existing-writer regressions and per-volume caps.
Latest focused **49/49 (35 attribute-list + 14 version/layout/service)** covers
named-list guard, GPT mixed-volume isolation, modern/legacy extension identity
and zero-length nonresident index-bitmap
coverage on current code. Full verification passed **1001 tests/one Windows-only
skip, 1002 total**. Four cases were added after that run's build:
current inventory is 1006 including the skip, not a claimed full 1005-pass result.
Final repeat all-six self-contained publish/local Linux AppImage passes on
latest source with legal notices retained. No versioned release/
GitHub publication changed.
Cross-builds are not physical compatibility. Individual
implemented/verified subitems are tracked in the checklist; authentic legacy
qualification is deferred to **P1.NTFS**, while remaining platform and 1.0
full-phase gates stay open.
Newly readable legacy/attribute-list variants stay noneditable until Phase 3
**3.0.1-3.0.5** staged/Save As/new-output writer qualification, before **3.7**
direct commit. Existing qualified Phase 2B editing and incomplete **2B.6**
Windows-authored-media/security-reference/LFS 2.0 gates are preserved.

Tower's approximately 8.42 GB raw NTFS 3.0 image remains outside the repository
and must not be copied, modified or distributed. Initial rejection concerns
the live directory attribute list at MFT 26 referencing indexing extension 8336;
the native index warning is now independently diagnosed under **2C.6/2C.V5**.
ntfs-3g 2022.10.3 limits whole non-BITMAP attribute reads to 65536 bytes;
Tower's in-bounds contiguous index is 344064 bytes. Initial in-place enumeration
now succeeds for 905 directories/16199 files, zero directory failures, writing
false, unchanged source size/mtime. Counts use retained hidden/system/metafile
enumeration filters, not all on-disk entries. Three complete explicit-path
streams match independent `ntfscat` hashes, and service ReadyReadOnly preview/
hex/search works with enumerated ordinary entries. Initial hidden/system
`boot.ini` service selection failed under unchanged filters; no filter UX change
was made. No owner-image full source hash, extraction or content persistence
has been performed in this evidence.
The final latest-source owner probe repeats the same bounded results.
Resident index-root compression flags represent default directory compression;
compressed file data remains denied. Empty nonresident index streams are
handled. These results do not certify health, whole-image hashes or extracted data.
Modern extension-header record identity at offset 44 is checked only with USA
offset >=48, not legacy USA offset 42. Empty nonresident index bitmaps are
supported without attempting an empty-run read; final focused tests cover both.
See [initial evidence and pinned sources](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15)
and the [qualification ledger](support-matrix.md#phase-2c-ntfs-reader-qualification-plan).

## Retained reader pipeline

Composition entry point: `new SectorWorks.Storage.ImageOpener()` implements
Core's `IImageOpener`. No DiscUtils types cross the Storage boundary. Opening,
directory enumeration and reads should run on an application worker, not the UI
thread. A session owns its filesystems, open parser file streams, container and
source handle; dispose the session after its consumers stop.

The opening pipeline uses wired internal `IReadOnlyContainerHandler`
implementations for raw/ISO/VHD/VHDX and selects their Core `FormatDetection`
results by priority. Wired `IReadOnlyFileSystemHandler` factories probe NTFS,
FAT and optical media and return Core `IReadOnlyFileSystem` implementations.
These interfaces are implementation details, not additional Core dependencies.

Every opened adapter uses Core's explicit `IReadOnlyFileSystem.Capabilities`
read-only default: browse/extract/preview apply to ordinary supported files;
full filesystem validation, writes, rebuild and preservation guarantees remain
unavailable. NTFS/optical per-feature exclusions are reported in properties or
operation errors, not silently treated as successful reads. UDF-only media has
no opened adapter/volume, so it contributes no browse/extract/preview capability.

## Implemented paths

* Content-prioritized VHDX, VHD and ISO detection, with a bounded raw fallback.
  The extension never establishes support. A short, recognizable filesystem
  boot sector or an obviously truncated optical/container candidate is rejected.
* `FileBlockDevice` uses managed random-access file I/O with a read-only handle.
  `PartitionBlockDevice` is a non-owning bounded view. Both validate the entire
  requested range before reading, including overflow-safe EOF and zero-length
  cases. Reads may return fewer bytes, with a maximum 1 MiB request. Logical
  size is independent of resident memory; raw sector size defaults to 512 and
  physical sector size is unknown. VHDX reports its recorded sector sizes.
* Fixed/dynamic VHD and VHDX expose virtual bytes without expansion. VHD footer/
  dynamic-header checksums and VHDX header/region CRC32C are checked before
  invoking the dependency. Container and virtual capacities are separate.
  Differencing images fail with a parent-requirement diagnostic: no automatic
  parent path, locator, adjacent file or external resource is followed.
  VHDX requiring log replay is rejected rather than repaired.
* MBR primary entries expose type, active flag, checked extents and overlap
  checks. Extended containers are reported but not traversed. Protective MBR
  selects GPT; hybrid MBR is warned about, not silently merged.
* GPT validates revision, size, header CRC32, entry-array CRC32, identifiers,
  usable bounds, metadata overlap and partition overlap. Valid primary tables
  remain browsable when backup CRCs or primary/backup consistency fail, with
  explicit warnings. A damaged primary is not silently recovered from backup.
  Unallocated usable byte ranges (not reserved GPT metadata) are exposed in
  image properties, including a zero-region count.
* FAT12/16/32 uses a small internal read-only reader, not the dependency's
  cluster-chain walker. It validates BPB/allocation extents, clusters, end
  markers, free/bad/reserved references, repeated clusters and directory
  ancestry. Reads do not return repeated content from cyclic chains. LFN
  sequence/checksum validation, UTF-16 names, CP437 short names, case-insensitive
  lookup, empty files/directories, attributes and timestamps are supported.
  FAT timestamps have unspecified kind: the source stores wall-clock time,
  not a UTC offset, and properties state that limitation.
  Early-DOS FAT12 floppies without a 0x55AA trailer are accepted only with a
  pre-extended-BPB jump target, supported 40/80-track floppy geometry, exact
  declared size, bounded allocation layout and identical FAT copies. All normal
  bounds/chain checks remain active; a missing trailer on a modern layout is
  still rejected. A warning distinguishes this read-only legacy case from a
  bootability claim. Without an extended label record, FAT12/16 volume labels
  come from the root directory instead of interpreting bootstrap bytes as text.
* NTFS ordinary resident/nonresident file data, names, basic attributes and UTC
  timestamps use the pinned dependency after independent boot/MFT validation.
  Multi-sector fixups, bounded/nonzero attribute lengths, resident values,
  mapping-pair progress/extents and index-entry progress/child graphs are
  checked before dependency browsing. Unicode and independent file positions
  are tested. Compressed, sparse, encrypted and reparse-point/link content,
  alternate data streams remain explicitly unsupported. Phase 2C's separate
  bounded resolver above adds ordinary supported attribute-list continuations,
  not unsupported content semantics or MFT bootstrap lists. Link metadata is listed;
  links and ancestor links are not followed.
* ISO9660/Joliet and ordinary Rock Ridge directories/files are read-only.
  Joliet is selected only when a Joliet escape sequence is actually present;
  otherwise Rock Ridge precedes vanilla ISO9660. The adapter works around the
  dependency's misleading `ActiveVariant` value on its Joliet path.
  Preflight bounds directory records, checks dual-endian extents, detects
  directory cycles and validates SUSP progress, continuation extents/cycles,
  names, link components and timestamps before invoking its parser.
  El Torito catalog LBA and volume/naming metadata are descriptive only:
  **bootability and exact-layout preservation are never guaranteed**.
  Owner-authorized compatibility refinements on 2026-10-15 accept unused
  out-of-range addresses only for zero-length file sections, with an opening
  warning. Empty file streams return EOF without asking the dependency to seek
  to those addresses; there is no filename whitelist or source rewriting.
  Nonempty extents, directories, endian pairs and unsupported allocation/SUSP
  features retain their checks. ISO9660/Joliet directory metadata is decoded in
  bounded batches from the validated directory extents because the pinned VFS
  repeatedly reparses parents for each entry property. Metadata reads still
  pass through `DeviceStream` and the shared parser budget; no limit is raised.
  Version hiding, case-insensitive duplicate detection, attributes and valid
  UTC timestamps are retained; invalid names, Unicode or dates fail explicitly.
  Rock Ridge keeps its existing SUSP-aware metadata path.
* UDF volume recognition sequences are reported, but UDF browsing is not
  implemented and no extra dependency was added. An ISO/UDF bridge exposes
  only the independently recognized ISO view. BIN/CUE/CD-DA remain Phase 6.

All reader paths described here are read-only. Neither detection nor the
dependency's writer APIs alone enable editing. The separately qualified Phase 2
writer operates only on new output, never on these source devices.

## Resource, cancellation and concurrency policy

The original image is never loaded whole. Directory metadata may be read into
bounded arrays. Limits deliberately reject some otherwise valid very large or
unusual images rather than silently misinterpret them:

| Operation/structure | Limit |
| --- | --- |
| Individual underlying stream/file read | 1 MiB |
| Dependency operation | 128 MiB read and 131072 reads/seeks |
| GPT entry array | 16 MiB, 16384 entries, 128–4096-byte power-of-two entries |
| VHD BAT | 16 MiB / 4194304 entries |
| VHDX region / individual metadata item | 64 MiB / 1 MiB |
| VHDX virtual capacity | 64 TiB, also bounded by the 64 MiB BAT limit |
| FAT chain | 1048576 clusters; file chain also bounded by declared file length |
| FAT/ISO directory | 16 MiB |
| NTFS initialized MFT / aggregate allocated indexes | 128 MiB each |
| Individual NTFS directory allocation | 16 MiB |
| NTFS attribute list / all lists per open | 1 MiB / 32 MiB |
| NTFS attribute-list entries / extension records per file | 4096 / 256 |
| NTFS extension-record reads per open | 32 MiB |
| NTFS index bitmap | 64 KiB |
| NTFS MFT/index record | 64 KiB |
| ISO total directory preflight | 128 MiB |
| ISO SUSP continuation | 1 MiB / 32 hops per record |
| Directory/path/index depth | 256 |
| Dependency directory listing | 131072 entries |

NTFS index bitmaps select active allocation blocks; unused blocks are skipped
rather than interpreted as live indexes. All MFT records remain structurally
validated, including inactive records, but inactive records do not trigger
attribute-reference resolution. Index graph depth is bounded at 256.

Open and directory enumeration accept cancellation; preflights and FAT
walkers check it between bounded units. File streams support cancellation on
each async read. Dependency work is synchronous behind these APIs, with
cancellation checked on guarded reads/seeks; it is not a forcibly interruptible
process sandbox. Every dependency object/stream in a session shares one gate,
including virtual-device reads and independent extracted-file streams.
Anticipated I/O/format, range, overflow and unsupported-feature failures become
`ImageFormatException` with diagnostic context. Cancellation is not disguised as
corruption; programming failures are not broadly swallowed.

## Verification and remaining limitations

Owner-supplied Compaq diagnostic media was investigated locally on 2026-10-14.
It is a 737280-byte FAT12 floppy with an early-DOS BPB, 1024-byte clusters,
matching FAT copies and root label `DIAGS`, but no 0x55AA trailer. The original
reader's mandatory trailer check caused the opening error. All ten ordinary
files were opened and hashed after the compatibility fix, and the source SHA-256
remained `313ff1d0692ca4a54b3c1c99dd0ba33624be46caf1396c4873b0b5a9532507f1`.
This supports read-only access, not bootability or exhaustive disk certification.
The supplied image is ignored locally and is not redistributed as a golden;
regressions use newly authored synthetic data rather than its contents.
The owner has made 360K and 720K retro-PC floppies explicit priorities. Preserve
this existing 720K subset; dedicated 360K qualification and broader legacy
variants are future Phase 7 item 7.10 work, not an expansion implemented here.

Run, using the SDK selected by `scripts/common.sh`:

```sh
"$DOTNET" test tests/SectorWorks.StorageTests/SectorWorks.StorageTests.csproj
"$DOTNET" format tests/SectorWorks.StorageTests/SectorWorks.StorageTests.csproj --verify-no-changes
```

The 61-test suite materializes every immutable golden through the existing fixture
helper (compressed/expanded hashes), opens all nine normal goldens and asserts
the five malformed cases fail. Successful opens read the sentinel and verify
unchanged expanded source hashes. Additional disposable synthetic tests cover
fixed containers, missing/forbidden parents, active-log rejection, 8 TiB sparse
raw and 1 TiB dynamic VHDX addressing, MBR/GPT damage, CRCs/backup disagreement,
Unicode/empty FAT, FAT cycles, NTFS unsupported flags/reparse/Unicode/concurrent
streams, Rock Ridge, El Torito and nonprogressing NTFS/SUSP metadata.

DiscUtils 0.16.13's stream-based fixed-VHDX authoring API throws
`NotImplementedException`. The fixed-VHDX reader test therefore allocates every
block in a disposable dynamic image and sets the format's `LeaveBlocksAllocated`
file-parameter flag. This tests that reader path, not independent authoring-tool
interoperability. No golden is changed. NTFS Unicode is a same-length synthetic
filename mutation of the independently authored disposable golden.

Unsupported optical cases include multi-extent/interleaved allocation,
extended-attribute blocks, Rock Ridge relocation and SUSP extension selectors.
The preflight may reject unusual deleted NTFS metadata or large MFTs rather
than recover them. NTFS sparse/compression support is **not** claimed merely
because upstream exposes it. Detection is not comprehensive filesystem repair,
validation certification, fuzzing certification or protection against every
possible third-party parser defect. Additional hostile-media fuzzing/process
isolation and independent fixed/bootable optical interoperability fixtures are
release-hardening work. Runtime verification here is Linux x64 only; the
owner-approved dependency/NTFS formatting risk remains for release-quality
re-evaluation, not a Phase 1 fork prerequisite.
