# Phase 1 read-only storage

This document describes the retained reader pipeline. Phase 2's separate
overlay and Save As writer are documented in [staged editing](staged-editing.md);
read support here never implies write qualification.

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
  alternate data streams and attribute-list continuation records are explicitly
  unsupported rather than returned as ordinary bytes. Link metadata is listed;
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
| NTFS MFT/index record | 64 KiB |
| ISO total directory preflight | 128 MiB |
| ISO SUSP continuation | 1 MiB / 32 hops per record |
| Directory/path/index depth | 256 |
| Dependency directory listing | 131072 entries |

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
