# Support matrix

## Phase 5 BIN/CUE capabilities

| CUE layout | Read / playback | Modified output |
| --- | --- | --- |
| Dedicated MODE1/2048 file with qualified non-boot ISO | Browse, extract and inspect track bytes | Validated new BIN/CUE folder; fixed track size |
| Separate-file multi-data or mixed-mode set | Browse supported data; play supported CD-DA | Rebuild qualified data tracks; preserve other members and cue bytes |
| MODE1/2352 or ordinary MODE2/2352 Form 1 | Bounded data-track browse/extract and raw track access | Rebuild a qualified non-boot ISO track at the same length, with one data track per member and no extra data indexes; preserve other tracks |
| BINARY or MOTOROLA audio-only | Direct signed 16-bit stereo PCM; recognized program areas | No audio modifications |
| UDF-only or ISO/UDF inside supported CUE data track | Bounded browse/extract | UDF-bearing tracks stay read-only |

No BIN/CUE source commit, CD burning, subchannel decoding or general boot/layout
claim. See [Phase 5](phase5-bin-cue.md) for exact gates, backend requirements
and evidence. Existing Phase 4 standalone ISO/UDF support is separate.

The [second compatibility follow-up](../Existing_File_Compatibility.md) also
accepts one-digit CUE track and index numbers without changing the sheet.
Raw rebuild qualification checks consecutive stored sector addresses and
EDC/ECC throughout the edited track. It preserves addresses and copies
untouched audio/index ranges exactly, with ordinary XA Form 1 subheaders only.
Unknown protection, sidecars, Form 2, and opaque XA subheaders still block
rebuild. No repair of mismatching checksums is performed.

## Phase 4 optical capabilities

Qualified images rebuild to a validated new file, never to their source.
The [Phase 4 report](phase4-optical-rebuild.md) lists exact format and
preservation predicates, limits and platform evidence. Unsupported boot,
hybrid, opaque and divergent-namespace layouts stay read-only.

The [compatibility follow-up](../Existing_File_Compatibility.md) qualifies
standalone ISO zero trailers and narrowly recognized descriptor version stamps.
Rebuild retains the trailer length and descriptor bytes; nonzero untracked data
and unknown descriptor fields still block it. Hybrid disk and optical views are
validated separately. A rejected disk map no longer hides an otherwise valid
optical filesystem, but neither view can be edited. A valid disk filesystem can
also open as Raw when a conflicting optical signature does not validate; that
conflict keeps writing disabled.

Rebuild can preserve safe existing ASCII aliases and partial directory extents
whose unused allocation bytes are all zero. Newly authored names still use the
normal namespace rules. A bounded CD-XA label and ordinary Form 1 directory
metadata can be retained; XA interleaving, file numbers and opaque producer
fields are not reconstructed. Sector layout reference: [ECMA-130](https://ecma-international.org/publications-and-standards/standards/ecma-130/).

The [third compatibility pass](../Existing_File_Compatibility.md) adds these
bounded variants for standalone ISO and otherwise qualified ISO-in-CUE:

- Contiguous, byte-identical copies of the primary descriptor. Every copy is
  regenerated at its original descriptor address; different primary views are
  not merged.
- Empty recording dates (all six calendar bytes zero, with a valid UTC offset);
  original date bytes are retained, and the browser shows no date.
- Document identifier fields that resolve to ordinary root files or carry
  recognized inline publisher/copyright text. Referenced files keep their root
  identifier; unresolved or ambiguous references stay read-only.
- Ordinary inline Apple Finder metadata and narrowly recognized Personal RomMaker
  and Personal Disc Scribe stamps. Associated files/resource forks, opaque
  system areas and unverified checksums are still not rebuilt.

None of these changes qualifies additional ISO/UDF bridges or enables optical
source commit. CloneCD subchannel sidecars and UltraISO binary metadata remain
read-only. Descriptor semantics: [ECMA-119](https://ecma-international.org/publications-and-standards/standards/ecma-119/).

The fourth compatibility pass accepts a uniquely case-matched BIN companion
beside its CUE sheet on Linux. Exact spelling wins; links and multiple fallback
matches are refused. Rebuilt sets retain the original CUE text and use its
declared member spelling. Recognized sidecar files still block rebuilding.

Standalone ISO and otherwise qualified raw ISO-in-CUE can retain more legacy
root and single-volume descriptor conventions, blank recording-date fields,
and different visibility bits on a directory's listing and self records.
Complete path tables may have trailing zero bytes. A bounded mkisofs mastering
record is retained in its original position; its verified descriptor
fingerprint is recalculated only for qualified ordinary unshared files.

FAT32 permits a stale or uninitialized **backup** FSInfo hint if both copies
agree outside the hint bytes, boot sectors match, and the primary hints and FAT
allocation qualify independently. Output summary copies must match, and their
other bytes must remain unchanged. This does not enable repair of stale primary
counts or unqualified filesystem writes.

## Accepted 1.0 scope — November 17, 2026

**Phase 2 / 2B / 2C / 2D and Phase 3: COMPLETE / OWNER ACCEPTED UNDER THE
REVISED 1.0 SCOPE.** The [owner-approved assignment](sectorworks_1_0_scope_change_and_phase_closeout_sol_prompt.txt)
and [closeout audit](1.0-scope-closeout.md) supersede older blocked/pending
summaries below; dated source/test/cleanup findings retain their original scope.

All qualified FAT/NTFS functionality is retained, including structural legacy/list
rows and native passive NTFS3.1: 512-byte sectors, 4096-byte clusters/index blocks,
1024-byte MFT records, exactly `0x1400` flags, paired clean LFS1.1 pages and the
exact empty repair/deleted-state predicate. Full record/allocation/security-
reference preservation and every existing admission check remain intact.
Default TxF-enabled/transaction-bearing and other unqualified states stay
**read-only**, not unrestricted modern Windows writing.

**DEFERRED POST-1.0:** [P1.NTFS.MODERN.1–.6](../checklist.txt) cover default
TxF/CLFS/KTM checkpoint/active/prepared/in-doubt, TOPS/`$TXF_DATA`, unsupported
repair/deleted, coherent detached LFS2/cache qualification and dependent writer
rows. These are unresolved, not passed or impossible. Existing **P1.NTFS.1–.6**
authentic historical qualification is preserved. Physical ARM64 is **NOT VERIFIED
— DEFERRED POST-1.0 BY OWNER**, separately **P1.ARM64.LINUX** and
**P1.ARM64.WINDOWS**; owner has hardware for later testing. Neither deferral gates
1.0/phases; four-target builds/packages remain, cross-publish is not runtime proof,
and macOS remains outside scope. Unrelated later-phase requirements stay open.

No product code or test result is changed by this documentation closeout.
[Existing exact-source evidence/packages](../artifacts/focused-ntfs-20261117/evidence-index.json)
remain immutable; see the new [closeout index](../artifacts/scope-closeout-20261117/evidence-index.json).
**STOP: Phase 4 is next available for owner authorization, not started or
authorized here.** No new native/ARM64 experiment, external push, tag or release.



## November17 focused native NTFS correction

The [focused diagnosis](focused-ntfs-diagnosis.md) supersedes historical blanket
repair/deleted exclusions for one precisely bounded NTFS3.1 state: TxF and repair
logging disabled (`0x1400`), clean LFS1.1, empty repair/deleted structures, and
unchanged complete passive metadata records/allocation bytes. Portable five-
operation staged editing, Save As and commit now work on that state. Native
read-only/writable oracles and standard-user Windows GUI outputs pass; exact
container/layout evidence is in the focused ledger. No Windows mounting,
formatting, replay or repair is an application prerequisite. Default TxF-bearing
and coherent LFS2 images remain outside write qualification. Physical ARM64 is
**NOT VERIFIED — DEFERRED POST-1.0 BY OWNER**; see P1.ARM64.LINUX/P1.ARM64.WINDOWS. No physical pass is inferred.


## Current bounded writer capabilities

| Subset | Modify / validated Save As | Source commit |
| --- | --- | --- |
| Existing qualified FAT12/16/32 and ordinary NTFS rows | Retained; final Windows regression and independent exact-output checks pass | Retained independent decision |
| Bounded NTFS 1.2/3.0/3.1 ordinary + resident/nonresident moved-data, split-data and moved-root-index lists | Qualified 441-row Linux/Windows selections, enabled | Separately qualified 441-row selections, enabled |
| Readable default TxF/transaction-bearing, dirty/ambiguous NTFS or LFS2/cache layouts | Read-only; modern-state qualification deferred to P1.NTFS.MODERN | Unavailable |
| Native passive NTFS3.1 exact0x1400/cleanLFS1.1/empty repair-deleted state | Qualified five-operation Save As;15 unique disk/layout rows | Separately qualified;60 native volume oracles/standard-user Windows output checks |
| Intentionally unqualified NTFS 1.2 reader fixture, MFT-bootstrap lists and unknown allocation/security/metadata | Unavailable | Unavailable |

The expanded set has 21 metadata/version features × 21 container/layout/physical
rows: **420 new** plus **21 retained ordinary 3.1**. It uses logical sectors 512,
clusters/index blocks 4096 and MFT records 1024; VHDX separately covers physical
512/4096. Each raw, fixed/dynamic VHD and fixed/dynamic VHDX row covers filesystem-only,
primary MBR and GPT. NTFS 1.2 requires empty `$Quota`, no `$Extend`, 48-byte SI,
per-file security and the qualified reserved-record/header subset. Existing
resource/path/allocation/security/log bounds remain. Generic structure predicates
and independent output checks apply; fixture provenance is not a filename whitelist.

<a id="phase-2d-retained-closure-ledger-2026-10-17"></a>

## Historical Phase 2D retained closure ledger, 2026-10-17

Owner authorized bounded closeout, not a new capability claim. Historical
2B/2C statuses remain incomplete until the following evidence genuinely passes:

| Retained requirement | Named closure items |
| --- | --- |
| 2B.3-2B.5 outstanding ordinary-NTFS rows; 2B.6 operations/replaceability/current Windows reference/media/log safety | 2D.2 codec; 2D.3 native media; 2D.4 clean-log safety; 2D.5 row regression |
| 2B.7 outstanding fixture/workflow and shared-workflow verification; blocked 2B exits | 2D.3/2D.5/2D.9; 2D.E1/2D.E4 |
| 2C.3 bounded metadata/content; 2C.5 synthetic hashes/features; V3/E1 | 2D.6 per-row reproducible reader evidence |
| 2C.6 source integrity/selected extraction; V3/E4 | 2D.7 local-only Tower |
| 2C.V1/V2/V7/V8; E6 | 2D.1 ledger/authority; 2D.9 full verification/docs/review |
| 2C.V6 physical/build parent; E5 | 2D.8 consolidated actual platforms/builds; 2D.5-2D.9 |

The separately authorized October 16 Phase 3 backend delivery remains intact;
2D precedes its remaining work, not its historical implementation. Earlier
current-state statements denying all direct commit are superseded by that
recorded backend increment. New legacy/attribute-list writer/Save As/commit
expansion stays under 3.0/3.7, never inferred from reader success. Current
Windows codec execution is not inheritance/NTFS interoperability proof; current
Windows-authored disposable-media native reopen is separately required.
Clean LFS 2.0/cache safety must be demonstrated, never enabled by removing
gates or replay/reset/normalization. If unsafe/infeasible, report a blocker.
Tower needs complete source hashes and representative selected content/extraction,
not exhaustive health certification; original stays external/uncopied/unchanged.
P1.NTFS historical authoring remains deferred; bootstrap lists unsupported.
ARM64 2D.8c is unavailable/blocked, not waived by an unspecified latest-build
owner report.
See [current 2D evidence](platform-status.md#phase-2d-preflight-and-closeout-2026-10-17).

The owner subsequently resumed the recommended Windows runtime corrective
pass and portable generated-media verification under checklist 2D.10.
This does not resume remaining Phase 3 features or broaden the writer matrix.
TxF/CLFS, LFS 2.0/cache-safety and unavailable ARM64 requirements remain open.
Historical results below are not passes for the repaired source.

Verified 2D closures so far: Windows codec **271/271**; bounded reader
**66/66 each on Linux/Windows x64**, including nine exact workflow rows;
local-only Tower full source hashes/selected extraction/content comparisons.
These close the corresponding bounded 2C reader/Tower subitems, not the parent
platform/review gates. Full Linux verification is **1136 passes/one Windows-only
skip**, with all then-configured publishes/local Linux AppImage passing. Historical test
counts remain dated evidence, not sums added to these totals.

Windows-native author/source-readonly reopen pass on a disposable fixture,
but edited-output interoperability and the Windows-derived writer matrix do
not. Writable-attached `fsutil` reports LFS 2.0; detached raw pages/read-only
reopen report 1.1. Modern-log/cache-safety qualification remains unresolved;
native TxF/CLFS metadata is an independent writer blocker. No metadata stripping,
normalization, new write permission or new legacy/attribute-list editing follows.

The initial broader actual Windows execution was **not passing**: selected storage
400 pass/57 fail; Services 153 pass/3 fail. Failures expose missing independent
tools, shared application-log contention, source-sharing errors in direct
commit, and a preference exception/cleanup-test mismatch. Isolated reader
passes do not erase those outcomes. Historical Linux matrix/backend
qualification was not Windows runtime qualification. Exact classifications and
source-integrity limitations are in [platform status](platform-status.md#broader-windows-runtime-execution-failures-retained-not-a-platform-pass).

The frozen corrective source now passes actual Windows **177/177 Services**,
**392/392 reader/security/safety**, **12/12 handoff guards**, **168/168 existing
writer workflows** and **9/9 genuine two-commit dynamic-growth cases**.
All **186 exact outputs** pass **876 mandatory independent Linux commands**,
with unchanged hashes for all 84 input rows. This resolves retained runtime/
tooling defects without dropping Linux-native authoring coverage. These are
qualified legal Linux-authored/generated stacks, not Windows-authored TxF/CLFS,
LFS 2.0/cache-safety, GUI or ARM64 qualification. Source/media manifests and
compact byte-exact archives are in the
[final corrective record](platform-status.md#final-global-session-corrective-snapshot).
The final Linux complete suite passes **1178/one Windows-only skip**, with
zero build warnings/errors and then-configured publishes/local AppImage passing.
That corrective snapshot stopped for review. The 2026-11-16 assignment resumes
implementation under expanded authority; TxF/LFS/current-Windows-authored
interoperability and unavailable ARM64 remain incomplete.

This matrix describes the implemented reader subset, not blanket support for
every variant, feature or corrupt image. Revised Phase 2B staged editing/Save As is
qualified separately below. Phase 3 direct commit is now implemented and
verified for every row that was already Phase 2B Save-As-qualified (see the
ledger below). The current bounded legacy/list expansion at the top of this
document adds separately qualified structural rows. Unsupported features and
modern Windows transaction/log states remain noneditable. Optical rebuild is
available for Phase 4 qualified layouts; Phase 5 optical expansion and Phase 6
additional formats remain separate.
Phase 2C implements a separately bounded NTFS reader subset with targeted
evidence below; authentic historical-media qualification is deferred to post-1.0
**P1.NTFS.1-P1.NTFS.6** in the [checklist](../checklist.txt). Revised1.0
verification/review gates are accepted complete; modern NTFS and physicalARM64
follow-ups are separately deferred under P1.NTFS.MODERN/P1.ARM64.
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
| ISO/UDF bridge | Detect both sets of descriptors; independently reconcile supported namespace trees | Bounded UDF 1.02 plus ISO views when qualified; ISO-only fallback when safe |
| UDF-only | Detection and bounded UDF 1.02 ROM-profile validation | Ordinary supported files/directories; unsupported layouts may be detection-only |
| Differencing VHD/VHDX | Parent requirement diagnostic | Rejected; no automatic parent traversal |
| Unknown raw layout | Metadata/virtual hex with explicit warning | Unavailable unless a supported filesystem is independently recognized |
| BIN/CUE / CD-DA | Bounded CUE track geometry and raw program-area hex | Supported optical data-track browse/extract and direct CD audio playback |

MBR extended-chain traversal, GPT backup-only recovery, VHDX active-log replay
and filesystem repair are unavailable. Source commit is limited to the qualified
disk rows below.

## Staged editing

See [staged editing](staged-editing.md) for the independently qualified
Phase 2B FAT and managed NTFS subsets, Save As validation, resource limits and
failure behavior. Reader support alone never implies editing support.
Staging and Save As keep sources read-only. The separately authorized Phase 3
backend/UI supports direct commit for qualified rows with recorded Linux/Windows
evidence; the Windows source-locking defects were repaired and verified. Phase 4
qualified optical images rebuild to a new image. General bootability and
exact-layout preservation are not guaranteed.

| Stack | Stage add/replace/delete/rename/create directory | Save As new image |
| --- | --- | --- |
| Raw filesystem-only/primary MBR/GPT, qualified conventional FAT12/16/32 | Qualified short/LFN/mixed-case/NFC BMP subset | Validated same-layout raw copy |
| Fixed/dynamic VHD, filesystem-only/primary MBR/GPT, qualified FAT | Qualified separately per variant/layout/filesystem | Validated same-variant VHD |
| Fixed/dynamic VHDX, filesystem-only/primary MBR/GPT, qualified FAT | Qualified separately, 512/512 and 512/4096 | Validated same-variant VHDX |
| Bounded legacy FAT12 and conventional 360/720/1,200/1,680/2,880 KiB floppy geometries | Qualified where exact BPB, allocation, names and metadata pass | Validated same-size output; newly admitted floppy profiles stay Save As only |
| FAT outside qualified naming/metadata/BPB/geometry bounds | Disabled; reader remains available where supported | Disabled |
| Qualified ordinary NTFS 3.1 in raw/fixed-dynamic VHD/fixed-dynamic VHDX, filesystem-only/primary MBR/GPT | Qualified bounded ordinary/structural/native-passive subsets; unsupported modern states deferred | Validated same-layout/variant copy |
| NTFS outside qualified metadata/security/log bounds | Disabled with concrete per-volume reason | Disabled |
| Supported ISO/Joliet/Rock Ridge/UDF/bridge | Qualified shared staging; unsupported variants read-only | Validated rebuild only; no in-place commit |

The FAT writing envelope additionally requires 512-byte sectors, two mirrored FATs,
an extended BPB/signature or a qualified legacy floppy BPB, exact bounded-volume
size and the correct absolute hidden-sector count (zero for filesystem-only
images). FAT boot and root-directory labels are preserved independently,
including a missing or differing root label; ambiguous multiple root labels and
unqualified FAT32 FSInfo/backup metadata still block writing.
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
encrypted and link/reparse content and alternate streams are rejected explicitly.
Phase 2C now reads bounded ordinary attribute lists/extension records and split
data/index streams through a separate preflight before the unchanged signed
reader; MFT bootstrap lists remain explicitly blocked. An entry being listed does not
establish that every feature of its contents can be read. NTFS formatting fails
on Linux due to the dependency's Windows SID APIs and is not enabled anywhere.
The original dependency's existing-volume Add/mkdir fail in ACL inheritance on
Linux; the owner-authorized isolated managed repair removes that blocker for
the separately qualified subset, without changing the original reader/package.
See the [concrete blocker](dependency-evaluation.md#phase-2b-existing-volume-ntfs-blocker-2026-10-15).
The owner reaffirmed NTFS writing as a 1.0 requirement. The subsequent
[bounded managed investigation](dependency-evaluation.md#bounded-managed-ntfs-repair-investigation-2026-10-15)
reached its stop point, then the owner authorized implementation/qualification.
The [managed repair](dependency-evaluation.md#managed-ntfs-repair-implementation-and-qualification)
now has native/Linux and bounded native-Windows passive-subset evidence,
not unrestricted ordinary Windows-media qualification.
Writing accepts 512-byte logical sectors, matching primary/final backup boot
records and original hidden sectors, clean volume/allocation metadata and
supported basic ACL semantics. All eight cluster sizes 512 through 65536 have
native raw-volume workflow evidence; the 21 container rows use 4096-byte clusters.
Clean LFS1.1 writer acceptance now has synthetic and exact native passive-subset
output evidence. Windows codec/reference and bounded native edited-output
interoperability pass. Default TxF/CLFS and LFS2/cache qualification remains
unresolved and DEFERRED POST-1.0 to P1.NTFS.MODERN. Unsupported logs are never
replayed, reset or normalized.
NTFS formatting remains disabled. Current commit UI and bounded startup recovery
are implemented; candidate, published R3 and corrected R4 Linux AppImage evidence
and the completed resumed/focused Windows desktop checks are distinguished in the [current review](phase2d-phase3-review.md).

For ISO9660/Joliet, bounded batch directory metadata avoids the pinned reader's
repeated parent parsing without increasing parser budgets. Zero-length files
with unused out-of-range addresses open with a warning and read as empty without
seeking; no filename whitelist, nonempty-extent exemption or source rewriting
is used. Other allocation/structural checks and the existing Rock Ridge path
remain intact. See [the owner-authorized compatibility evidence](platform-status.md#iso-reader-compatibility-refinement-2026-10-15).

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
Linux x64, Windows x64 and Raspberry Pi 500 Linux ARM64 results.

## Evidence and future work

### Phase 6 PMFX read-only delivery plan

Owner-approved roadmap addition **2026-10-17**, checklist **6.11.1-6.11.8**.
This is a required read-only addition, **not an implemented reader** or
authorization to start Phase 6. It follows the 1.0 read-only/blocker-review
policy below. See [research and qualification steps](pmfx-read-only-plan.md)
for observed fields, strict decoder completion, bounds, licensing and fixtures.
Phase 6 must implement and qualify direct PMFX opening, browsing and extraction
inside SectorWorks before completion. Research/detection/external raw conversion
or a future implementation plan is insufficient; unresolved required-reader
blockers keep the phase incomplete unless the owner explicitly changes scope.

| Planned stack/subset | Current evidence | Required qualification | Editing/save modes |
| --- | --- | --- | --- |
| PMFX, observed 512-byte-sector/1 MiB-block/single-table subset, settings 1/9, zlib/verbatim/sparse/single-fragment chains -> primary MBR -> FAT16/FAT32 | Scratch payload scan of ten images; five FAT16/five FAT32; initial small raw derivative passed existing services/native content comparison | Independent DiskGenius restore/fixture comparisons, production handler/device, normal browse/preview/hex/search/extraction, malformed/resource/lifecycle/source-integrity tests | Disabled even for otherwise writable FAT |
| PMFX -> supported NTFS subset | No NTFS PMFX in the corpus | Legal independently authored reference, normal reader workflows and existing NTFS version/feature restrictions | Disabled; no NTFS writer qualification follows |
| Other PMFX tables/chains/geometry/codecs/encryption/parent/incremental/split layouts | Unknown/unqualified | Separate evaluation and exact rejection reasons until independently qualified | Outside requested scope |

All present payloads were processed in memory, but the ten-image pass did not
qualify full filesystem contents, health or bootability. Sparse-zero/verbatim
semantics still need vendor-reference comparisons. Unchanged before/after
source hashes establish preservation during research, not product safety.
PMFX staging, modified Save As, commit, rebuild, creation/formatting and image
conversion/export are outside this addition, with no future writer promise.
File extraction is required; image conversion is not implied.

### Phase 2C NTFS reader qualification plan

Owner authorization **2026-10-15** adds Phase 2C before Phase 3 and approves the
matching technical-specification roadmap change. **Bounded reader implemented;
Phase 2C COMPLETE / OWNER ACCEPTED UNDER THE REVISED 1.0 SCOPE** (November17).
The owner subsequently moved authentic NT4/Windows 2000/XP-created-media
qualification and dependent checks to post-1.0 **P1.NTFS.1-P1.NTFS.6**.
These are incomplete follow-ups, not passed tests or 1.0 blockers. VM-authored
media suffices; vintage hardware is unnecessary. Automated tests, safety bounds,
source integrity and general platform checks remain required. This ledger separates qualification from reader
evidence below. Preserve the existing reader and qualified Phase 2B
writer rows above.

| Version / authoring target | Bounded reader subset | Current evidence / gate | Newly readable editing |
| --- | --- | --- | --- |
| NTFS 1.2 / Windows NT 4.0 | Version-specific ordinary metadata/files/indexes and bounded supported attribute lists/extensions | Structural synthetic layout and shared-service tests pass; no authentic NT4 fixture exists; authentic qualification deferred to P1.NTFS | Bounded structural writer now qualified; exact current bounds above |
| NTFS 3.0 / Windows 2000 | Ordinary file/directory access including supported live index continuation records | Bounded synthetic rows and Tower full-source hashes/selected extraction/native comparisons pass under 2D.6/2D.7, not whole-volume health; authentic historical authoring qualification deferred to P1.NTFS; native index warning is a tool-size limit | Bounded structural writer now qualified; exact current bounds above |
| NTFS 3.1 / Windows XP and later | Existing ordinary reads plus separately bounded attribute-list/extensions | Structural synthetic and new reader tests pass; authentic historical XP authoring/runtime qualification deferred to P1.NTFS, not blanket media support | Existing subset retained; bounded structural legacy/list expansion qualified separately above |

Per-version rows require exact feature/layout/provenance, bounded reference/VCN/
extent validation, normal/malformed fixtures, shared browse/preview/hex/search/
extraction, independent comparisons and unchanged source hashes. Authentic
historical-media provenance/comparisons are deferred to **P1.NTFS.2-P1.NTFS.4**;
accurate synthetic/observed provenance and bounded qualification remain required.
Attribute lists do not imply sparse/compressed/encrypted/reparse/link/ADS support.
Ordinary base/extension and split data/index reads use a separate reader
resolver; strict writer defaults stay unchanged. Attribute-list-dependent MFT
bootstrap is explicitly rejected because safe mapping is not implemented.
This excludes those layouts, not silently repaired or fully supported. Observed
`NTFS version` properties are metadata, not authentic legacy fixture provenance.
Use existing raw/fixed-dynamic VHD/fixed-dynamic VHDX filesystem-only/primary
MBR/GPT read composition where structurally valid; container recognition never
qualifies the contained NTFS variant. Mixed-volume caps must remain independent.

The approximately 8.42 GB raw Tower NTFS 3.0 owner image stays outside the
repository; do not copy, modify or distribute it. Initial rejection involves
the live directory attribute list at MFT 26 referencing indexing extension 8336.
Historical Phase 2C in-place reader results: NTFS 3.0, 905 directories/16199 files, zero
directory failures, root 24 entries, writing false, unchanged source size/mtime.
These counts follow retained hidden/system/metafile enumeration filters, not
all on-disk entries. Three complete explicit-path streams match independent
`ntfscat` hashes; service ReadyReadOnly text/hex/search passes with enumerated
ordinary entries. Initial hidden `boot.ini` service selection failed under
unchanged filters. No owner extraction/content persistence or full-image hash
was performed; no health certification follows.
That Phase 2C snapshot's final focused **49/49 tests passed (35 attribute-list + 14 version/layout/service)**,
including named-list guard, mixed-GPT FAT/NTFS isolation, modern/legacy
extension-header identity and empty nonresident index-bitmap tests.
See [canonical verification chronology/logs](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15)
for the earlier full run and final focused coverage. Final repeat then-configured
self-contained publish/local Linux AppImage passed on that source with legal
notices retained. The final owner-probe repeat passes the same bounded read-only
checks; no full-image hash or extraction. Versioned releases
were untouched and no GitHub publication occurred. This is cross-build evidence,
not physical compatibility.

Subsequent 2D.6/2D.7 verification closed the nine declared bounded reader rows
(66/66 focused passes on each of Linux/Windows x64), full Tower before/after
source hashes and 1,235,021 bytes of selected extraction/native comparisons.
Private extracted outputs were removed; this does not certify all content or
volume health. See [completed bounded Tower evidence](platform-status.md#completed-local-only-tower-sourcecontent-checks).
Resident index-root default-compression flags and empty nonresident
index streams are handled; compressed file content remains denied.
The native index warning is diagnosed as ntfs-3g 2022.10.3's 65536-byte
non-BITMAP `ntfs_attr_readall` limit versus Tower's 344064-byte index, not
corruption evidence; [pinned source/evidence](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15).
No authentic NT4 or full Tower qualification is claimed.

The exact implemented/verified subitems, bounded synthetic read-test exit
**2C.E2**, preserved-writer/capability **2C.E3**, regression **2C.V4**,
and native-warning **2C.V5** are checked; cross-build subwork **2C.V6a** is
verified on its final source. Parent **2C.V6**, **E5** and **E6** close under
retained Linux/Windows x64 evidence, four publishes, reconciled documentation
and November17 conditional owner acceptance. Physical ARM64 stays NOT VERIFIED
and deferred under **P1.ARM64**; authentic historical qualification stays **P1.NTFS**.
Newly readable attribute-list/legacy volumes remain noneditable until Phase 3
qualifies staged operations/history, validated Save As and new-output NTFS
writing under **3.0.1-3.0.5**, BEFORE **3.7** direct commit.
Only historical-OS-authored writer-media interoperability moves to
**P1.NTFS.5**; required disposable version/layout/feature writer fixtures,
independent safety checks and failure tests remain in **3.0.4** for 1.0.
All existing qualified Phase 2B FAT/NTFS editing remains intact. Actual
bounded native passive Windows-authored output evidence separately passes
**2B.6/2D.3–.5**; default-TxF/CLFS and LFS2/cache writer work is incomplete but
explicitly DEFERRED POST-1.0 under **P1.NTFS.MODERN**, not replaced by reader proof. The Windows codec-reference subwork is
complete under **2D.2**, not a remaining execution gap.

Reader caps: initialized MFT 128 MiB; attribute list 1 MiB each/32 MiB aggregate;
4096 entries and 256 extension records per file; extension reads 32 MiB/open;
index allocation 16 MiB/stream and 128 MiB aggregate; index bitmap 64 KiB; index
graph depth 256. Index bitmaps select active blocks and unused blocks are skipped.
Every MFT record remains structurally validated; inactive records do not trigger
reference resolution. See [detailed bounds](storage-phase1.md#resource-cancellation-and-concurrency-policy).

### Owner-approved 1.0 read-only boundary

The roadmap targets 1.0. VMDK, VDI, QCOW2, DMG, ext2/ext3/ext4, exFAT,
HFS+ and APFS target qualified **read-only subsets where feasible**, not
implemented or blanket read support. Phase 6 must evaluate variants/features,
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
`Managed subset` means the bounded NTFS subset above, not all Windows media;
the Windows codec reference and bounded native passive-media writing are verified.
Remaining default-TxF/CLFS and LFS2 qualification is DEFERRED POST-1.0 under
P1.NTFS.MODERN.1–.6, not passed. Layouts must be structurally valid;
**Phase 2B is COMPLETE / OWNER ACCEPTED UNDER THE REVISED 1.0 SCOPE**.

| Container variant | Layout | FAT12 | FAT16 | FAT32 | Ordinary NTFS |
| --- | --- | --- | --- | --- | --- |
| Raw | Filesystem-only | Qualified | Qualified | Qualified | Managed subset |
| Raw | Primary MBR | Qualified | Qualified | Qualified | Managed subset |
| Raw | GPT | Qualified | Qualified | Qualified | Managed subset |
| Fixed VHD | Filesystem-only | Qualified | Qualified | Qualified | Managed subset |
| Fixed VHD | Primary MBR | Qualified | Qualified | Qualified | Managed subset |
| Fixed VHD | GPT | Qualified | Qualified | Qualified | Managed subset |
| Dynamic VHD | Filesystem-only | Qualified | Qualified | Qualified | Managed subset |
| Dynamic VHD | Primary MBR | Qualified | Qualified | Qualified | Managed subset |
| Dynamic VHD | GPT | Qualified | Qualified | Qualified | Managed subset |
| Fixed VHDX | Filesystem-only | Qualified | Qualified | Qualified | Managed subset |
| Fixed VHDX | Primary MBR | Qualified | Qualified | Qualified | Managed subset |
| Fixed VHDX | GPT | Qualified | Qualified | Qualified | Managed subset |
| Dynamic VHDX | Filesystem-only | Qualified | Qualified | Qualified | Managed subset |
| Dynamic VHDX | Primary MBR | Qualified | Qualified | Qualified | Managed subset |
| Dynamic VHDX | GPT | Qualified | Qualified | Qualified | Managed subset |

Qualification bounds: conventional 512-byte FAT sectors, FAT12
1,474,560-byte volumes only (360K/720K excluded); FAT16/32 within the existing
64 GiB volume, 8 MiB FAT and 1,048,576-cluster limits. Primary MBR and consistent
primary/backup GPT only; partition extents cannot change. VHD uses 512-byte
logical sectors. VHDX 512/512 has 18 FAT rows above; 512/4096 has another 18
independently checked rows (fixed/dynamic, three layouts, three FAT variants).
Together with raw/VHD this is 63 FAT matrix rows. VHDX 4096/4096 is explicitly
rejected; no 4Kn writer is qualified. The naming subset is defined in the
[editing guide](staged-editing.md#current-phase-2b-envelope-and-mandatory-blocker).
Sparse/compressed/encrypted/reparse/ADS and unsupported attribute-list
NTFS features, differencing parents, active-log replay and repair stay gated.
Every newly enabled cell requires all five operations, history/discard,
multi-volume isolation, metadata/allocation and unchanged-region checks, failure
injection, and source hash evidence. Direct commit is now implemented for every
qualified row below (Phase 3, 2026-10-16); see the Phase 3 ledger further down.

The owner approved this documentation correction on 2026-10-15, including a
separate **Phase 2B before Phase 3**, then authorized its implementation.
The subsequent owner-authorized **Phase 2C** adds reader qualification before
Phase 3, with separate writer prerequisites for its newly readable targets.
The ledger reports verified bounded delivery under the revised1.0 scope;
P1.NTFS.MODERN retains unresolved further NTFS work rather than claiming it passed.
The [checklist](../checklist.txt) is authoritative for task status and review gates;
the [technical specification](specifications/Cross_Platform_Disk_Image_Workspace_Tech_Spec_v0.3.txt)
section 6.6 records the same delivery policy.

| Required stack/subset | Staged editing and validated new output | Source commit | Reconstruction |
| --- | --- | --- | --- |
| Qualified unpartitioned raw FAT12/16/32 | Phase 2 verified; Phase 2B broadens naming | **Qualified (Phase 3, 2026-10-16)** | Normally not needed; copy-and-apply Save As |
| Conventional FAT12/16/32 with qualified LFN/mixed-case/Unicode | Phase 2B FAT delivered, bounded subset | **Qualified (Phase 3)** on qualified disk stacks | Normally not needed |
| Raw primary MBR/GPT with qualified FAT or ordinary NTFS | FAT and bounded NTFS subsets delivered; unsupported modern states P1.NTFS.MODERN | **Qualified (Phase 3)** | Normally not needed; preserve partition layout |
| Fixed VHD and dynamic VHD, each filesystem-only/MBR/GPT + FAT/ordinary NTFS | FAT and bounded NTFS subsets per variant; unsupported modern states P1.NTFS.MODERN | **Qualified (Phase 3)** | Normally not needed; retain variant/virtual size |
| Fixed VHDX and dynamic VHDX, each filesystem-only/MBR/GPT + FAT/ordinary NTFS | FAT and bounded NTFS per variant/sector subset; unsupported modern states P1.NTFS.MODERN | **Qualified (Phase 3)** | Normally not needed; validate container metadata/log state |
| Ordinary NTFS in filesystem-only raw | Bounded writer/native-passive evidence passed; remaining modern states deferred P1.NTFS.MODERN | **Qualified (Phase 3)** | Normally not needed; formatting is separate |
| Bounded NTFS 1.2/3.0/3.1 legacy/attribute-list subsets across required disk stacks | **Qualified structural subset:**441 Linux and441 Windows staged/Save As rows, independently verified; exact bounds at the top of this document | **Qualified bounded subset:** separate441-row Linux/Windows commit selections and exact-byte native verification; unsupported modern Windows TxF/LFS2 stays read-only; P1.NTFS.MODERN deferred | Preservation validated; no NTFS formatting; authentic historical authoring remains P1.NTFS |
| ISO 9660/Joliet/supported Rock Ridge | Phase 4 shared staging passes qualified layout rows | Unavailable | Validated new-image rebuild with qualified namespace and metadata preservation |
| Declared UDF 1.02 and ISO/UDF bridge subsets | Phase 4 bounded read and staged editing | Unavailable | Verified new-image rebuild preserving consistent qualified views |
| Qualified BIN/CUE data and mixed-mode layouts | Shared staging on the qualified MODE1/2048 ISO data-track subset | Unavailable | Validated separate new output sets; no source-set replacement |
| Conventional 360K/720K FAT12 subsets | Phase 6 item 6.10 | Phase 6 item 6.10 using Phase 3 safeguards | Normally not needed for raw file edits; create/conversion separate |

Disk coverage applies to structurally valid combinations with declared resource,
sector-size and feature bounds, not every variation an upstream library recognizes.

<a id="phase-3-direct-commit-qualification-implemented-2026-10-16"></a>

### Historical Phase 3 backend qualification (2026-10-16)

The following dated record describes the original backend increment. Its shared
qualification gate, absent UI/startup integration and unstarted legacy/list work
are superseded by the current bounded implementation described above. Its
original native/failure evidence remains historical evidence.

Owner authorized Phase 3 on 2026-10-16 as a scoped child-chat session. The
delivered increment is a container/filesystem-agnostic direct-commit pipeline
(source identity recheck, cross-platform advisory locking, durable backup,
transaction journal, atomic finalize, startup-style crash recovery) that wraps
the already-qualified Phase 2B `Apply`/`ValidateOutput` writer pipeline rather
than reimplementing per-stack writers. `CommitInPlace` is gated on the exact
same qualification as `Modify`/`SaveAs`, so every row above that was already
Save-As-qualified is now also direct-commit-qualified, and the still-blocked
Phase 2C legacy/attribute-list NTFS rows correctly remain non-editable.

Verified via full-matrix tests (not a single combination): 5 containers
(raw, fixed/dynamic VHD, fixed/dynamic VHDX) x 3 layouts (filesystem-only,
primary MBR, GPT) x FAT12/16/32 with qualified LFN, at both 512 and 4096
physical sector sizes for VHDX, plus ordinary NTFS across all 3 layouts and
containers. Each commit is independently re-verified after finalize with
`fsck.fat -n`/mtools (FAT) or `ntfsfix -n`/`ntfscat` (NTFS) against the
now-mutated source, not just the staged temp copy. Failure-injection tests
prove a commit interrupted before finalize leaves the source byte-identical
and recoverable via backup restore, and that recovery never restores a backup
over an already-finalized (successfully committed) source. Locking is verified
cross-process with an independent Python writer holding a real fcntl
byte-range lock.

Known gaps, flagged for owner follow-up rather than silently resolved:
- 3.0.1-3.0.5 (Phase 2C new-target NTFS legacy/attribute-list writer
  qualification) were not attempted this session; those rows stay blocked.
- `CommitRecovery.Discover` (startup crash-recovery detection) is implemented
  and tested as a library primitive but is not yet invoked automatically by
  the UI application host at launch.
- No UI command/button is wired to `CommitAsync` yet; this is a backend-only
  delivery for now.
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
Phase 6 item 6.1 and release closure accept the intentional 1.0 read-only
boundary above without requiring future writers. Required reader blockers and
required core/optical/retro writer gaps still require explicit owner review.

### Retro-floppy priorities

360K (368640-byte conventional raw images) and 720K (737280 bytes) are explicit
retro-computing priorities. The compatibility follow-up verified selected
generated 360K/720K and early-DOS BPB profiles for staging and validated Save As
without replacing the boot sector. It did not qualify direct commit for these
newly admitted profiles, blanket retro support or a full Phase 6.10 exit.

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
