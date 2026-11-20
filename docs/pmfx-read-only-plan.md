# Phase 6 PMFX read-only plan and feasibility evidence

## Current Phase 6 review

The direct read-only PMFX parser is implemented in the current workspace.
See the [Phase 6 report](phase6-additional-formats.md#diskgenius-verification)
for byte-exact generated tests, in-place evidence and the paid-edition
restriction on the remaining vendor check. The dated research below
records the prior feasibility work.

## Superseding scope/acceptance decision — November 17, 2026

[The accepted 1.0 scope and exit audit](1.0-scope-closeout.md) closes revised
Phase2/2B/2C/2D/3 gates on existing evidence and explicit owner acceptance.
Remaining modern NTFS qualification is DEFERRED POST-1.0 to P1.NTFS.MODERN.1–.6;
physical ARM64 is NOT VERIFIED — DEFERRED POST-1.0 BY OWNER, separately
P1.ARM64.LINUX/P1.ARM64.WINDOWS. Four-target builds remain; owner has hardware
for later testing. Prior dated blocked/pending descriptions below retain their
historical scope, not current exit requirements. No Phase4 or new probes are
authorized by this documentation task. Safety/media/manual-tunnel rules remain.


## Decision and scope

On **2026-10-17**, the owner requested DiskGenius PMFX read-only browsing and
extraction in **Phase 6, item 6.11**, for interoperability between DiskGenius
at work and SectorWorks at home. This approves roadmap/documentation changes,
**not implementation now**. PMFX is not a currently implemented or qualified
product reader. The [checklist](../checklist.txt) remains authoritative;
[technical specification section 6.2.2](specifications/Cross_Platform_Disk_Image_Workspace_Tech_Spec_v0.3.txt)
records the scope amendment.

**Phase 6 includes implementation, not just planning.** Its completion requires
PMFX images in the declared qualified subset to open directly in the normal
SectorWorks application for browsing and extraction. A plan, detection-only
result or external raw conversion does not satisfy this gate. Any unresolved
reader blocker keeps Phase 6 incomplete unless the owner explicitly changes
the requirement. "Not implementation now" limits this documentation task,
not the required Phase 6 deliverable.

The deliverable is a bounded container reader exposing existing qualified
partition/filesystem access through normal browse, preview, hex, filename search
and file/directory extraction. Container recognition is not filesystem support.
FAT/NTFS inside PMFX must remain read-only even where those filesystems are
writable in raw/VHD/VHDX. Staging, metadata edits, modified Save As, direct
commit, rebuild, creation/formatting and image conversion/export are outside
this addition. No future PMFX writer milestone/date is promised. This does not
reduce existing disk, optical or retro writing requirements.

## Research evidence, not reader qualification

Local research completed before this roadmap decision used scratch-only probes.
Owner bytes/code/data were not sent to public research services. Public source
was inspected, not executed against owner images.

### Initial small FAT16 image

The owner explicitly authorized a local copy of the small sample:
8,990,720 bytes, SHA-256
`c52dff3380d62a101fe6b89e66b9a76338843ffd428c5d0052010122f02f0acb`.
Its 63 payloads were complete checksum-valid zlib members, each yielding 1 MiB.
The reconstructed logical disk was 65,601,536 bytes, with an active MBR type
`0x06` partition at LBA 32.

Unchanged existing SectorWorks binaries consumed that **raw derivative**, not
PMFX: 15 directories and 400 ordinary files, totaling 12,989,701 content bytes.
Browse, text preview, hex, filename search and recursive extraction succeeded.
All 400 extracted file hashes/lengths matched SectorWorks streams and independent
mtools reads. `fsck.fat -n` returned 0 on the derivative. A zero-byte extra entry
reported by 7-Zip was independently identified as a volume-label entry
(attributes `0x28`), not a missing ordinary file.

The copied source, decoded disk/partition, extracted owner files and scratch
build outputs were removed. The original full size/hash matched before/after.
This establishes one downstream workflow feasibility case, not a PMFX reader,
bootability, general filesystem health or vendor-restore equivalence.

### Ten-image in-place follow-up

Ten local PMFX images were investigated **without copying containers, writing
expanded disks or persisting extracted/decoded contents**. All source sizes,
complete SHA-256 hashes, device/inode identities and modification times matched
before/after. Only small probe sources, logs, hashes and JSON research evidence
remain in local session scratch; they are not production code or fixtures.
The original small sample is included in this ten-image corpus, not an
eleventh independent case.

| Measure | Observed result |
| --- | ---: |
| Physical source bytes, combined | 22,373,679,104 |
| Header logical bytes, combined | 122,050,674,688 |
| Logical map entries inspected | 116,399 |
| Present payloads processed in bounded memory | 79,767 |
| Complete checksum-valid zlib payloads | 77,431 |
| Payloads treated as verbatim by exact stored block size | 2,336 |
| All-zero absent map entries | 36,632 |
| Single-fragment chained payloads reassembled successfully | 12 |
| Observed partition filesystems | Five FAT16, five FAT32; no NTFS |

Every present payload was processed, not only sampled. Each present decoded
block was 1 MiB; final blocks were clipped to header logical geometry and their
decoded padding beyond logical EOF was zero. All compressed members passed
termination/checksum/exact-size/no-trailing-input checks.

All ten exposed one primary MBR FAT partition (type `0x06` or `0x0c`), starting
at LBA 32, 63 or 2048. Bounded BPB, cluster geometry, FAT reserved-entry and root
metadata probes succeeded; hidden-sector counts matched partition starts and
long-name slots were observed. This follow-up did **not** recursively enumerate
or compare all file contents, extract files, or run full native filesystem
checks. The initial derivative's broader workflow evidence remains separate.

Two images had backup-mode field 2, sparse maps and ten/two chained payloads
respectively; the other eight had field 4. Logical hashes were computed without
materializing disks, interpreting absent entries as zeros. That interpretation
is corroborated by public code but **not independently verified against a
DiskGenius restore**. Similarly named images had different partition starts;
do not assume that backup modes preserve identical original disk layouts.
Filenames describe source media, not necessarily stored/logical size: one
"80GB" sample records 33,820,286,976 logical bytes, for an unknown reason;
another roughly 7.95 GB logical disk has a roughly 2.147 GB FAT16 partition.
Neither observation proves truncation or corruption.

## Observed binary layout

All values below are **little-endian research observations**, not an official
format grammar or permission to accept arbitrary field values. Reconfirm them
with legal independently authored fixtures before implementation.

| Offset | Encoding | Observed value/interpretation |
| --- | --- | --- |
| `0x000` | 4 bytes | `PMFX` |
| `0x004` | u32 | `0x100` |
| `0x008` | u16 | 256 |
| `0x00a` | u16 | 1 or 9; setting-like field, not formally specified |
| `0x00c` | u32 | Flags 0 |
| `0x010` | u32 | 512 bytes/sector |
| `0x014` | u64 | Logical sector count |
| `0x01c` | u32 | 2048 sectors/block = 1 MiB |
| `0x020` | u32 | 65536 entries/table |
| `0x024` | u32 | One L1 entry |
| `0x028` | u32 | L1 pointer location `0x518` |
| `0x054` | u32 | `0x418` |
| `0x058` | UTF-16 | `PmfxImage` |
| `0x100` | 4 bytes | `CLDK` |
| `0x10c` | u32 | Backup-mode field 2 or 4 |
| `0x518` | u64 | Table address `0x1000` |

The observed table is 65536 x 16 bytes = 1 MiB. Each entry contains a u64
payload/head offset, u32 allocated size with a high-bit chained flag, and u32
total stored payload length. Unused rows were zero. All observed physical
payload/head slots were 4 KiB aligned and non-overlapping. Fragment extents
were also checked against every payload/head/fragment extent. Physical order
does not establish logical block order.

Observed decoding rules:

- An all-zero entry was interpreted as an absent/zero block. Partially zero
  or inconsistent entries must not silently become zeros.
- Stored length exactly 1 MiB was treated as verbatim. All other present
  payloads were complete zlib members. Independent restore comparison is
  still required to qualify this classification.
- Both setting values 1 and 9 contained zlib, with sampled headers generally
  `78 01` and `78 da` respectively. A compression-setting interpretation is
  plausible; a formally specified field meaning is not established.
- Each of the twelve chained heads began with u32 fragment count **1**, then
  u64 fragment offset and u32 fragment allocated length. Inline compressed
  bytes began at head +16. Concatenating that inline area with the needed
  prefix of the fragment extent to the declared total stored length produced
  a valid zlib member. Fragment allocation can include padding.
- Multi-fragment/nested chains were not encountered. No proprietary LZ77
  was encountered, and no encryption prevented decoding this corpus. Neither
  observation qualifies detection/support of those variants.

## Critical decoder qualification gate

On .NET 10.0.13, `System.IO.Compression.ZLibStream` decoded all 63 original
valid blocks with Python-matching hashes. However, the combination
`ReadExactly(expectedBytes)` followed by `ReadByte() == -1` also accepted a
missing final checksum byte, a missing four-byte checksum, and trailing data
after a valid member. A corrupted *present* checksum was rejected.

Thus exact output length plus apparent EOF is **insufficient**. The production
integration must prove real member termination, checksum validation and exact
compressed-input consumption; buffered source position alone is not proof.
Do not assume the runtime API exposes these guarantees or choose a library
without testing them. Python research used bounded `decompressobj()` output
(block size +1), exact output length, `eof`, empty `unused_data` and empty
`unconsumed_tail`. This is reference evidence, not a Python runtime dependency.
Codec choice/provenance/licensing and these negative tests remain unresolved
production gates under 6.11.1/6.11.4.

## Public references and licensing

Official manuals describe backup modes/compression, not a sufficient binary
specification:

- [Disk backup](https://www.diskgenius.com/manual/backup-disk-to-image-file.php).
- [Partition backup](https://www.diskgenius.com/manual/partition-backup.php),
  including full/incremental options; those options are not qualified here.

Public implementation references reviewed:

- FsRover at `a8f71e7da16b94f6da6470d2fd8bf223af6ffb5c`:
  [PMFX reader](https://github.com/a1ive/FsRover/blob/a8f71e7da16b94f6da6470d2fd8bf223af6ffb5c/grub/grub-core/io/pmfx.c),
  [decoder](https://github.com/a1ive/FsRover/blob/a8f71e7da16b94f6da6470d2fd8bf223af6ffb5c/grub/grub-core/lib/dgcomp.c)
  and [help](https://github.com/a1ive/FsRover/blob/a8f71e7da16b94f6da6470d2fd8bf223af6ffb5c/help.md).
  File notices are GPL-3.0-or-later: potentially compatible with this project's
  GPLv3 distribution with correct provenance/notices, subject to review of
  every imported file/dependency. Native GRUB code is not a drop-in managed
  reader. Its mode-2/file-backup and mode-4/all-sector interpretation is
  third-party evidence, not an official field specification.
- LovelyCarve at `c973732980e9436a97960e4f96e2e77ebee44566`:
  [PMFX reader](https://github.com/LovelyTeams/LovelyCarve/blob/c973732980e9436a97960e4f96e2e77ebee44566/src-tauri/src/core/evidence/readers/pmfx.rs).
  Repository AGPLv3 licensing requires explicit review before any copying;
  do not assume compatibility with GPL-only integration. This reference
  describes table/chained-fragment structures but assumes zlib setting 9;
  this corpus also uses 1. It was a very recent project at review, not
  evidence of mature reader qualification.

Pinned DiscUtils 0.16.13 has no PMFX handler; tested installed QEMU/7-Zip do not
open these original containers. No production dependency, source import or
license inventory changed during research or this planning update.
Prefer a small managed implementation behind the replaceable Storage boundary,
but choose it only after completing the codec/provenance evaluation.

## Implementation and qualification sequence

The numbered tasks live in **6.11.1-6.11.8** in the checklist. Execution requires
separate Phase 6 authorization.

1. **Scope and references:** review public-source licenses; define exact accepted
   field combinations and per-stack rows. Initial observed-subset targets are
   512-byte sectors, 1 MiB blocks, one table, setting values 1/9, modes 2/4,
   zlib/verbatim, sparse maps and single-fragment chains.
2. **Independent fixtures:** use legally generated FAT16/FAT32 data and a legal
   DiskGenius authoring environment to establish restore-reference hashes and
   metadata. Include both settings and each mapping type; record software
   version, backup options, source/raw/PMFX hashes and exact expected contents.
   Hand-built fixtures help negative tests but alone cannot establish vendor
   interoperability. Obtain a separate NTFS PMFX reference and retain current
   NTFS version/feature restrictions. Owner images are never fixture/package
   inputs; no work-media transfer or DiskGenius redistribution is authorized.
3. **Container implementation:** distinct PMFX identity/content-priority detection,
   handler and random-access device -> existing partition/filesystem readers ->
   shared Services. Reject recognized unsupported PMFX instead of Raw fallback.
   Check every geometry/count/offset operation and metadata/payload/fragment
   extent, overlap, alignment and declared coverage before use. Never guess a
   codec after a decoding error, repair metadata, follow parents automatically
   or expand the disk to a temporary raw file for normal opening.
4. **Bounds and lifecycle:** declare measured limits before enabling support.
   Research's initial envelope was <=64 GiB logical size, <=65536 map entries,
   a 1 MiB table, <=1 MiB physical requests, <=1 MiB stored payloads,
   exactly 1 MiB decoded blocks with a one-byte over-expansion sentinel,
   and a four-block decoded LRU. Only one fragment per chained block was allowed.
   Bound allocated extents and fragment reads separately from useful payload
   length. These are research bounds, not measured product peak-memory or
   final budgets. Specify aggregate metadata I/O/work, cache/decode allocations,
   operation deadlines/work limits, cancellation and synchronization/disposal;
   retain existing filesystem budgets. Measure the normal service opening path,
   including source-identity checks, rather than only the container probe.
   Full payload sweeps/full before-after hashes are qualification work, not
   permission to add an unconditional decoding sweep to production opening.
5. **Hostile input:** test invalid headers/unknown fields, huge counts,
   overflow, partial-zero entries, metadata/payload/fragment overlap,
   short/out-of-file reads, bad chains, invalid lengths, truncated checksums,
   trailing members/bytes, output over-expansion, final-block/EOF edges,
   source mutation and cancellation. Unsupported features need concrete errors,
   never success-shaped zero filling or silent Raw/codec fallback.
6. **User workflows/capabilities:** normal selection/file filters, physical and
   logical sizes, PMFX properties, read-only reasons, shared browse/preview/hex/
   search and single/recursive extraction with hostile-name/output-race tests.
   Compare file hashes/metadata against independent references; verify write
   denial at Storage, Services and UI boundaries without demoting qualified
   raw/VHD/VHDX editing. Keep original owner hashes unchanged in any separately
   authorized in-place repeats, with no persisted content unless authorized.
7. **Release gates:** run targeted and existing-reader/writer regressions,
   all four supported cross-publishes, available Windows/Linux x64/ARM64 runtime workflows,
   and large/sparse random-access memory/responsiveness checks.
   Publish exact supported rows, codec/fixture provenance, measured limits,
   unsupported reasons and concrete blockers, then stop for owner review.

Unqualified variants currently include multiple tables, multi-fragment/nested
chains, other versions/flags/sector/block geometries, proprietary LZ77,
encryption, split/incremental/parent semantics and vendor restore fidelity.
Filesystem-only/GPT/multiple-volume PMFX composition has no evidence in this
corpus and requires its own rows/fixtures before advertising it. An actual
NTFS PMFX sample would improve research but is not present evidence.
Successful signature/BPB/payload checks never certify full filesystem health,
bootability, exact-layout preservation or safe writing.
