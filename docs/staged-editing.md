# Staged editing and Save As: Phase 2 and partial Phase 2B

Phase 2 is authorized independently of Phase 3 direct commit and Phase 4 ISO
rebuild. Sources stay read-only. A modified workspace is not a modified source
image, and saving a new image must never be described as committing the source.

## Target and preservation policy

The initial write qualification is raw IMG containing supported FAT. Every
advertised FAT variant and partition arrangement requires its own round-trip
evidence; reader support alone does not enable editing. VHD, VHDX, NTFS, ISO,
unknown layouts and legacy floppy variants remain read-only unless separately
qualified. In particular, the existing 720K Compaq/early-DOS read compatibility
does not establish safe write support.
The initial envelope is historical; the qualified Phase 2B FAT expansion below
supersedes only the corresponding restrictions. The managed NTFS subset below
is a further owner-authorized expansion, not blanket NTFS support.

Bootability and exact-layout preservation are not advertised. Do not use
valuable bootable originals as write-test media. Tests use generated fixtures
or disposable copies, never the owner's diagnostic image or binary sample.

macOS remains an intended supported target. The owner explicitly deferred Mac
runtime/signing validation while authorizing Phase 2 on 2026-10-14. Six-target
cross-publishing is build evidence only, not physical compatibility evidence.
See [platform status](platform-status.md).

## Review boundary

Undo, redo and discard concern the in-session staged state. Save As creates a
separate destination; it does not grant permission to overwrite the original.
Direct commit, recovery journaling and ISO rebuilding remain later-phase work.

### Phase 2C reader-only expansion and Phase 3 writer prerequisites

The owner explicitly authorized Phase 2C and its roadmap/specification change
on **2026-10-15**. This is an authorized/in-progress **reader qualification**
milestone before Phase 3, not an expansion of the writer envelope below.
Separate planned targets are **NTFS 1.2 (NT4), 3.0 (Windows 2000) and 3.1 (XP+)**,
including bounded supported attribute-list/extension and legacy metadata reads.
All new checklist **2C.1-2C.7**, **2C.V1-2C.V8** and **2C.E1-2C.E6** items began
pending; implemented bounded reader tests/enumeration are now recorded separately in
[platform evidence](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15),
not full phase or writer qualification.
The reader's current **49/49 focused tests (35 attribute-list + 14 version/layout/service)**
include read workflows/source hashes, denial of staging/Save As for new variants,
named-list guard, modern/legacy extension identity, empty nonresident
index bitmap and GPT mixed-volume
isolation: qualified FAT Save As preserves the read-only attribute-list NTFS
partition byte hash without granting NTFS editing. Full verification passed
1001 tests/one Windows-only skip, 1002 total; four later cases were added after
that run's build. Current inventory 1006 including the skip is not a claimed
full 1005-pass run.
Final repeat all-six self-contained publish/local Linux AppImage passes on
latest source, as does the bounded owner read-only probe. Legal notices are retained;
no versioned release/GitHub publication changed. Cross-builds are not physical
compatibility. Phase 2C remains partial/blocked on authentic legacy media and
stops for owner review. These selections are not additive. Signed reader packages and
strict `NtfsValidation.Validate` writer defaults remain unchanged; the writer
diagnostic now names the Phase 2B subset/Phase 3. MFT bootstrap lists are explicitly
unsupported, with exact [reader bounds](storage-phase1.md#resource-cancellation-and-concurrency-policy).
No authentic NT4 fixture exists; a synthetic version-field mutation is not
authentic legacy qualification.

**Preserve all existing qualified Phase 2B FAT/NTFS editing.** Newly readable
attribute-list/legacy volumes remain noneditable, with per-volume reasons and
composed caps; an editable sibling does not qualify them. Phase 3 must first
qualify their staged add/replace/delete/rename/mkdir, overlay/history, capacity/
naming checks and validated Save As/new-output NTFS writing under **3.0.1-3.0.5**,
including authentic disposable fixtures, allocation/MFT/index/attribute-list/
security/metadata preservation, source hashes and failure/independent-tool
evidence. Only then may the corresponding **3.7** direct-commit row be qualified.
Phase 3 remains unauthorized; reader success must not enable writes.

The required actual Windows-authored NTFS/security-reference and LFS 2.0/
cached-metadata write gates stay incomplete in **2B.6**. They are neither moved
into Phase 2C nor demoted to read-only 1.0 scope.
The approximately 8.42 GB raw Tower NTFS 3.0 owner image remains outside the
repository and must not be copied, modified or distributed. Initial diagnosis
is a live directory attribute list at MFT 26 referencing indexing extension
8336. The native warning is now diagnosed as a native-tool whole-attribute
size limit, not corruption evidence; initial in-place reader enumeration passes.
It is not a write fixture or proof of safe editing after reader acceptance.
See the [planned reader ledger](support-matrix.md#phase-2c-ntfs-reader-qualification-plan).

### Roadmap and authorization

On 2026-10-15 the owner approved a separate **Phase 2B** before Phase 3 to close
the existing disk-format editing gaps. It requires its own implementation
authorization, which the owner supplied on 2026-10-15. Phase 2's historical
completed status and all restrictions below remain
unchanged as historical evidence; implementation authorization is separate from
roadmap approval.

Phase 2B explicitly delivers shared staged editing and validated Save As for
raw, fixed/dynamic VHD and fixed/dynamic VHDX with structurally valid
filesystem-only, primary MBR and GPT layouts containing qualified FAT12/16/32
or ordinary NTFS. It includes FAT LFN/mixed-case/Unicode writing and portable
modification of existing ordinary NTFS volumes, separately from formatting.
Every variant/layout/filesystem row needs its own allocation, metadata,
preservation and failure evidence; reader support and successful file hashes
alone are insufficient.

Phase 2C separately qualifies new NTFS reads before Phase 3. Phase 3 first
qualifies its new-target staged/Save As/write prerequisites, then direct commit
per required disk stack, rather than stopping after one combination.
ISO/Joliet/Rock Ridge and a declared UDF/bridge
subset receive shared staged/rebuild qualification in Phase 4. BIN/CUE receives
staged data-track editing and safe output-set reconstruction in Phase 6.
Conventional 360K/720K writing remains assigned to Phase 7 item 7.10.

See the [delivery matrix](support-matrix.md#required-editing-delivery-roadmap)
and [authoritative checklist](../checklist.txt) for exact assignments and gates.
Unachievable required rows stay blocked pending an explicit owner-approved
scope decision with a named follow-up milestone, not silently deferred.

The [1.0 read-only boundary](support-matrix.md#owner-approved-10-read-only-boundary)
excludes writers for Phase 7 VMDK/VDI/QCOW2/DMG/ext/exFAT/HFS+/APFS additions,
without reducing Phase 2B or optical/retro writing requirements. No future
writer milestone is needed for those intentional read-only additions; exact
reader feasibility blockers still need owner review.

## Current Phase 2B envelope and mandatory blocker

**Phase 2B is partial/blocked, not complete.** Qualified conventional FAT12,
FAT16 and FAT32 now use the shared five-operation staged workflow and validated
Save As across raw, fixed/dynamic VHD and independently authored fixed/dynamic
VHDX, each with filesystem-only, primary MBR and consistent GPT layouts.
Capabilities are per volume. Multiple qualified volumes can be staged together;
an unqualified sibling remains read-only and its virtual bytes are preserved.

Disk logical sectors must be 512 bytes. VHDX physical-sector configurations
512/512 and 512/4096 are separately tested; 4096-logical-sector (4Kn) writes are
disabled. Virtual disk/volume bounds are 64 GiB, 8 MiB per FAT and 1,048,576 data
clusters. FAT12 remains limited to conventional 1,474,560-byte volumes with
18-sector/two-head geometry, including when bounded inside a partition.
FAT volumes must exactly fill their selected extent. BPB hidden sectors must
equal the selected partition's absolute LBA (zero without a partition); primary
and backup boot metadata must agree. No source normalization or repair is offered.

FAT naming supports short names and checked LFN/mixed-case/Unicode names in
**NFC BMP form**, with at most 255 UTF-16 units per on-disk name and 240
characters for the complete staged path. Surrogates/emoji, decomposed names,
control/format/private-use/unassigned characters, reserved device names,
leading/trailing spaces, trailing dots and FAT-forbidden punctuation are
rejected, not normalized. Existing short aliases must be unambiguous ASCII in
the qualified alias alphabet; CP437 aliases outside that subset remain read-only.
Lookup is case-insensitive; output spelling is checked ordinally. Existing
aliases and LFN sequences are retained for untouched entries and replacements;
new names receive collision-checked aliases. Metadata editors remain unavailable.
Nonminimal/invalid LFN records, unknown/reserved entry metadata and invalid
ordinary modification dates remain read-only. Supported creation/access dates
may be unspecified but are retained exactly; no timezone is invented for FAT.

Untouched entries retain supported metadata. Rename and replacement preserve
creation/access/modification dates and supported attributes. Ancestor case flags and timestamps are
restored after dependency operations. LFN slots, checksums, aliases, mirrored
FATs, unique reachable chains/no lost or crosslinked clusters, and FAT32 primary/
backup boot/FSInfo summaries are checked independently of file hashes.

Save As copies the physical container, extracts only selected bounded volumes
to owned disposable scratch files, and patches only changed 512-byte virtual
sectors. It retains container variant, virtual size, supported sector sizes,
partition metadata and untouched virtual regions. Dynamic block allocation can
grow for changed data, not because the whole virtual disk was rewritten.
Container checks reject unknown optional metadata, overlapping BAT payloads,
partially present VHDX blocks, parents, active logs and structural warnings.
Source container identity metadata is retained; VHDX write GUID/sequence fields
may update as required by its writer, with an empty output log. No bootability,
bit-identical layout, repair, conversion, direct commit or crash recovery claim
follows from this byte-preservation evidence.

### Managed NTFS subset and remaining mandatory qualification

The owner authorized implementation after the bounded investigation. Ordinary
NTFS add/replace/delete/rename/mkdir now uses the same overlay, history,
per-volume isolation and validated disk Save As path. The original signed
DiscUtils reader/container/FAT packages remain unchanged. The isolated
`SectorWorks.ManagedNtfs` source repair uses `SectorWorks.NtfsSecurity`, not
Windows principal APIs or a native driver.

The positive disk matrix has 21 NTFS rows: raw/fixed-dynamic VHD/fixed-dynamic
VHDX, each filesystem-only/primary MBR/GPT, plus VHDX physical 4096 variants.
Logical sectors are 512; native-authored fixtures additionally exercise cluster
sizes 512/1024/2048/4096/8192/16384/32768/65536. NTFS 3.1, matching primary/final
backup boot bytes, original correct absolute hidden sectors, clean volume state,
bounded MFT/index metadata and independently consistent allocation are required.
Disk/volume size remains at most 64 GiB. Existing FAT restrictions are unchanged.

The shared NFC BMP naming/path subset above also applies to NTFS; no name
normalization is performed. New children derive owner/group from their image
parent, not the host identity. Basic allow/deny/audit ACL inheritance preserves
null/absent/empty ACL distinctions, inheritance generations, generic file rights,
creator SID substitution and propagation semantics. Object/callback/conditional
or otherwise unsupported ACE semantics are rejected before allocation.
This is image permission preservation, not host-account access checking.

Replacement and rename preserve file identity, supported attributes, security
bytes, DOS aliases, SI timestamps, and independently distinct `$FILE_NAME` and
directory-index timestamps, including aliases. Original descriptor bytes are
compared independently; canonical serialization is only used for new security
records. MFT/cluster bitmaps, reachable unique runs, directory indexes, complete
MFT mirror extent and shared `$Secure` hashes/IDs/duplicate records/`$SII`/`$SDH`
are validated independently of tree/content hashes.

Editable metadata snapshots are bounded to 64 MiB/100000 entries/depth 128;
the existing parser's 128 MiB/131072-operation budget also applies. Capacity
reserves conservative MFT/index space and 512 KiB for each distinct planned
security descriptor, including paired `$SDS` block boundaries. This is not a
promise to fill every reported free byte. Full-volume add rejection/deletion,
near-full replacement with exactly 16 free clusters, index/MFT growth, actual
write/flush failures and cancellation are regressed. Optional new ObjectIDs are
explicitly disabled; existing ObjectIDs are never stripped to qualify.

Sparse/compressed/encrypted/reparse/link/ADS/attribute-list user features,
unsupported extended metadata, active quotas, ObjectID indexes and unsafe
security remain gated. Blank logs and strictly checked clean LFS 1.1 restart
pages are accepted without replay/reset/normalization; the log stream is hashed
before/after. Both restart pages must independently pass USA, geometry, bounds,
client, LSN and clean-state checks. Ambiguity, dirty state and LFS 2.0 remain
read-only. LFS 1.1 evidence is synthetic, not actual Windows media.

**Phase 2B remains partial:** Windows-authored reference fixtures, actual Windows
security-reference execution, and LFS 2.0/cached-metadata write safety are not
qualified. Normal Windows volumes must not be claimed writable merely because
native-created fixtures pass. These are retained required qualification gaps,
not an owner-approved NTFS read-only scope reduction for 1.0. Formatting, source
commit/journaling/recovery, log replay, repair and partition changes remain out
of scope. See [repair evidence](dependency-evaluation.md#managed-ntfs-repair-implementation-and-qualification).

## Historical Phase 2 initial envelope

The initial writer intentionally accepts only **filesystem-only, unpartitioned
raw FAT12/FAT16/FAT32 images**. Each must have conventional 512-byte sectors,
two mirrored FATs, an extended BPB, a valid boot-sector trailer, zero hidden
sectors and a declared size exactly matching the raw image. FAT12 writing is
further limited to conventional "1.44 MB" (1,474,560-byte),
18-sector/two-head floppy geometry;
360K/720K remain read-only. Partitioned raw, VHD/VHDX and optical media are not
included in this write envelope.

Writable qualification also requires consistent BPB/root volume labels, clean
mirrored allocation tables without lost/crosslinked clusters, and no opening
warnings. FAT32 requires valid FSInfo and matching backup boot metadata. Images
are not silently repaired to make them writable. Some original read-test goldens
omit auxiliary metadata and intentionally remain read-only; write tests prepare
separate clean disposable fixtures without changing the immutable archives.

The whole existing tree must use DOS 8.3 short names consisting of ASCII
letters, digits, underscore and hyphen (with one optional extension separator).
The basename and extension can independently be entirely uppercase or lowercase:
`CTMOUSE.EXE`, `ctmouse.exe`, `ctmouse.EXE` and `CTMOUSE.exe` are supported.
FAT's short-name case flags preserve this display spelling without long-name
records. Mixed case within either component, such as `CtMouse.exe`, requires
long-name records and remains unsupported.

Lookup, duplicate detection and operation targets are case-insensitive.
Lookups may use arbitrary casing, but new/renamed names must have representable
display case. Parent paths retain their actual spelling, including when callers
use a differently cased path. Case-only file and directory renames are supported,
including undo/redo and descendant edits; exact no-op renames remain rejected.
New names reject reserved device names regardless of case.

Long-name trees remain browseable but read-only for editing. Live LFN records
are rejected even when their display name equals their short alias. There is no
silent truncation, uppercase normalization or long-name loss. This case support
is an owner-requested Phase 2 refinement, not a feature deferred to Phase 3.
Phase 3 does not automatically broaden the naming or container write envelope.

These are deliberate initial writer restrictions, not changes to the broader
Phase 1 reader. NTFS formatting already fails on the development host because
of upstream Windows principal/SID APIs; portable NTFS modification remains
unproven and disabled.

The writer's resource envelope is narrower than the reader's: at most 64 GiB
raw image length, 8 MiB per declared FAT table and 1,048,576 data clusters.
These bounds apply before constructing the dependency writer; limiting data
clusters alone would not bound a maliciously padded FAT table.

## Logical overlay and history

An ordered change list and bounded tree snapshots provide add, replace,
recursive delete, file/directory rename and directory creation. Metadata changes
are reserved but not implemented. Renaming a directory changes the staged paths
of its descendants while unchanged file content continues to come from the base.
Imported/replacement content is snapshotted into temporary storage rather than
depending on the host file remaining unchanged later.

Directory browsing, text/hex preview, search and extraction consume the staged
view. Entries carry a textual changed marker; deleted entries disappear from the
browser but remain in Pending Changes. Undo/redo changes the active operation
prefix; a new operation after undo replaces the redo branch. Discard clears the
overlay/history. Pending changes are retained after successful Save As because
the workspace still represents the original source plus its overlay.

Editing limits are 100,000 current entries, depth 128, image paths up to 240
characters, 1,024 active commands and 1,000,000 retained snapshot nodes. Each
import is bounded by both the FAT 4 GiB-minus-one file limit and image capacity.
Allocation estimates deliberately reserve directory-growth margins; a
near-full image can be rejected conservatively rather than promised to fit.
Undo history retains required payloads; abandoned redo payloads are released.
Retained import payloads, including undo/redo history, are capped at 8 GiB per
workspace. The incoming payload is reserved alongside the current redo branch
before copying, bounding peak storage as well as retained state. Unknown files
or failed cleanup in an owned workspace-data directory block further staging
with an explicit path rather than deleting unrecognized data.

Production import payloads live under the managed local application-data
directory, `SectorWorks/WorkspaceData/<workspace-id>/`, not the working
directory. Free-space preflight uses the actual staging filesystem. Tests and
the physical smoke inject separate disposable staging directories. Staging is
session-local, not a persistent recovery journal; normal discard/close cleans
owned payloads, while crash leftovers must not be interpreted as recoverable
commits.
Platform enforces Unix `0700` directories and `0600` capture files; captured
payloads are sealed read-only (`0400`). Windows uses the inherited user-profile
ACLs. Final-directory links/reparse points and unenforceable Unix permissions
are rejected before selected bytes are written.

The UI distinguishes staged/modified state from source data, exposes Pending
Changes and warns before discarding pending work on close. Saving does not
silently switch the workspace to the new output.

### Desktop workflow

1. Open an image and inspect the selected volume's capabilities. An unsupported
   stack remains browseable, with editing commands disabled and a reason.
2. Navigate to the destination directory. Use Add or New Folder; use a single
   selected entry for Replace, Rename or Delete. New/renamed names must satisfy
   the current qualified FAT naming rules above.
3. Open Changes to review ordered operations. Preview, hex, search and extraction
   show the current staged state. Undo/Redo and Discard affect only that state.
4. Choose Save As and a new destination. Wait for copying, applying, flushing and
   reopen/hash validation to finish. A visible error is not a successful save.
5. Open the new output separately to inspect it. The original workspace remains
   modified until its pending changes are discarded or the workspace is closed
   with confirmation.

Directory deletion is recursive. Replacing a file snapshots its replacement
contents, not an executable action. Adding a host directory recursively,
drag-and-drop import, text editing and metadata editing are not implemented by
these commands.

## Save As safety contract

- Destination must be a new path, never the source or an existing file/directory.
- Capacity and expected output/storage space are checked before writing.
- A uniquely named create-new temporary image is written beside the destination.
  Copy/apply/validation work is performed off the UI thread with bounded buffers.
- Source devices remain read-only; only the temporary copy is passed to the
  replaceable Storage writer.
- Qualified opening establishes a streamed source SHA-256 baseline. Save As
  rechecks the source path, compares copied physical container bytes against
  that baseline, and
  rechecks the source again before publication. Detected external changes abort
  the save. This is not Phase 3 write locking, commit or recovery.
- Output is flushed to disk, closed and independently reopened through the
  reader pipeline. Its container/filesystem identity, complete expected tree,
  file lengths and SHA-256 content hashes must match the staged view.
- The writer's required output-validation contract compares supported original
  entry metadata with the ordered changes after this independent reopen.
  Valid-but-changed timestamps/attributes/aliases cannot pass merely because
  tree/content hashes and allocation are correct.
- FAT output allocation is checked independently of content hashes: mirrored
  tables, reachable unique chains, exact file allocation and absence of lost
  clusters must agree. Unchanged file hashes alone cannot prove this.
- Only after validation is the temporary image moved to the new destination
  without overwrite. Failure/cancellation must not report success or remove the
  source or prior destination.
- Normal failure cleanup removes the incomplete output. If cleanup fails, the
  error/log identifies the residual temporary image explicitly. Abrupt process
  termination can leave a hidden `.sectorworks-save-*.img` file; Phase 3 recovery
  discovery/journaling is not implemented. Do not treat residual files as
  validated images.
  Bounded-volume scratch files use `.sectorworks-volume-*.img`; cleanup failures
  identify their residual paths too. They are never advertised as saved images.

Free-space checks are advisory: other processes can consume space afterward,
and I/O/flush errors still abort the operation. Copying a raw image requires its
full logical length on the destination; no sparse-preservation claim is made.
The general `Validate` capability still does not advertise a standalone,
exhaustive filesystem validator. The mandatory bounded Save As checks described
here are not filesystem repair, boot certification or proof against every
hostile-media defect.

## Dependency findings retained as regressions

The independent checks found issues that tree/hash comparison alone missed:

- DiscUtils truncating a replacement file to zero leaked its old first cluster.
  The writer explicitly releases the old file and recreates it, restoring
  supported attributes, rather than relying on that truncation path.
- FAT32 free-space summaries needed updating in both primary and backup FSInfo.
- Readable test images lacking root-label/auxiliary metadata were not suitable
  write fixtures. Runtime qualification rejects them rather than repairing them.
- A deliberately injected lost allocation must prevent publication even when
  all expected file hashes still match.
- DiscUtils rename copied entries without their modification-date field. The
  writer restores file/directory modification times and completes invalid
  special-entry dates only in new/renamed directories; changed entries with
  invalid dates fail output validation.
- Rename-directory followed by adding a child exposed a stale dependency parent
  cache. Reopening the bounded adapter between ordered operations prevents that
  cache from changing target identity.
- DiscUtils writes uppercase short names and resets parent-directory case flags
  when updating directory modification times. Restore display flags on the
  changed entry and its ancestors after disposing each writer adapter. Case-only
  renames update these flags rather than attempting a move onto the same FAT
  identity. Exact-spelling output-tree checks reject lost case even if every
  file hash matches.

These findings reinforce the distinction between readable content, consistent
allocation metadata and independently verified output. The approved dependency
version is retained behind the replaceable writer boundary; the release-quality
review remains required.

## Recorded Phase 2B managed NTFS final verification

These are retained Phase 2B results, not a Phase 2C test run or new legacy/
attribute-list qualification. The reader-only plan above begins pending.

The owner-authorized repair's final `./scripts/verify.sh` passes **956 tests,
one explicit Windows-only security-reference skip (957 total)**. Counts:
691 storage passes/one skip, 156 Services, 68 UI, 14 foundation and 27 dependency.
Locked restore, zero-warning/error Release build, formatting, UI resources,
AppImage contracts and all authoritative reference comparisons pass.
All six self-contained publishes and the Linux x64 AppImage prototype pass
architecture/legal/payload checks; Linux published and both FUSE/extraction-and-run
AppImage resource diagnostics pass. Corresponding source/provenance and original
owner-sample hashes verify unchanged.

Six actual Mint/X11 NTFS workflows and their separate native output checks pass;
see [platform evidence](platform-status.md#managed-ntfs-repair-current-qualification-and-remaining-gaps).
The 21 disk rows, eight native cluster sizes, full/near-full media, metadata,
multi-volume and real I/O/flush/validation tests cover the declared subset only.
Windows-authored media/reference and LFS 2.0/cached-metadata qualification remain
mandatory gaps, not passed by these builds or synthetic LFS 1.1 tests.
Phase 2B remains partial; NTFS writing is still required for 1.0.

## Historical Phase 2B FAT prerelease verification and reproduction

On Linux Mint 22 x64/X11, 2026-10-15, before the managed NTFS repair:

- `./scripts/verify.sh`: **561 passing tests, zero skips**, locked restore,
  zero-warning Release build, formatting/resources and authoritative copies.
- 63 direct disk matrix rows and the same 63 actual shared staged workflows
  pass, plus naming/metadata/fullness/growth, multivolume and failure regressions.
- `./scripts/publish.sh all`: all six self-contained targets and Linux x64
  AppImage pass architecture/legal/sample-exclusion and local resource checks.
- All five real X11 container workflows pass after code freeze. Outputs pass
  independent qemu conversion (plus VHDX checks), bounded `fsck.fat -n`, mtools
  long-name listing and `mtype` content equality; source hashes are unchanged.
- NTFS remained blocked at that release, not passed or skipped into completion. Its disposable
  probes/native checks are evidence for the documented owner decision.

Final logs are local-only `artifacts/phase2b-final-verification.log`,
`artifacts/phase2b-final-precommit-publish.log`, and
`artifacts/phase2b-release-x11-<variant>.log`; screenshots and generated media
are in the matching directories. Repeat only in a **new** artifact directory:

The [2026-10-15 cleanup](platform-status.md#local-artifact-cleanup-2026-10-15)
retains these final logs and release-workflow media, but removes superseded
pre-freeze image copies and local build outputs. If the harness executable is
absent, run `./scripts/build.sh` before the direct command below.

```sh
source scripts/common.sh
"$DOTNET" tests/SectorWorks.UITests/bin/Release/net10.0/SectorWorks.UITests.dll \
  --phase2b artifacts/my-phase2b-smoke artifacts/my-phase2b-smoke/window.png raw-mbr
```

Supported harness variants are `raw-mbr`, `vhd-fixed`, `vhd-dynamic`,
`vhdx-fixed` and `vhdx-dynamic`. The harness requires a real graphical session
and supplies dialog paths; native-picker/manual accessibility review remains
incomplete. For native checks, convert VHD with explicit qemu `-f vpc` or VHDX
with `-f vhdx`, then extract the bounded partition before running FAT tools.
No source mounting or owner sample is used. Windows/ARM64 Phase 2B runtime is
unverified; macOS physical runtime/signing remains expressly deferred.

## Historical Phase 2 verification and reproduction

On Linux Mint 22 x64/X11, 2026-10-14:

- `./scripts/verify.sh`: locked restore, zero-warning Release build,
  **313 passing tests / zero skips**, formatting, UI resources and unchanged
  authoritative specification/design copies.
- `./scripts/publish.sh all`: all six self-contained outputs refreshed.
  Native executable architecture headers checked for each target; published
  Linux x64 resource diagnostic passed.
- Qualified FAT12/FAT16/FAT32 round trips independently pass `fsck.fat -n` and
  `mdir` when those tools are present (both were present and executed locally).
  Tests also assert unchanged source hashes and opaque boot/tail markers.
- Final real X11 smoke passes staging/history/review, staged extraction,
  validated new output, close cancellation and discard. Native FAT checks pass
  on its output, with valid dates. Screenshot reviewed against the desktop
  references; Pending Changes remains usable after Save As.
- The short-name case refinement adds all-four-flag and FAT12/FAT16/FAT32
  round trips with native tools, case-insensitive path/conflict checks,
  replacement and parent-case preservation, case-only file/directory rename
  with descendants and undo/redo, LFN rejection including identical aliases,
  and failure injection for lost spelling despite matching hashes.
  Rendered UI commands preserve lower names and case-only renames through save.
- A disposable copy of the owner's `starter.img` accepts Add and validated
  Save As, preserving all original file hashes/names and source SHA-256.
  Its pre-existing blank boot-label warning remains unchanged; native `fsck`
  returns 1 for source and output, not a claimed clean check or automatic repair.
  Neither the owner image nor its diagnostic output is redistributed.

Original smoke artifacts are in `artifacts/phase2-verified-x11/`; the refined
code's repeat is in `artifacts/fat-case-x11/`. Owner-image diagnostic output is
in `artifacts/fat-case-starter-check/`. To repeat the generated smoke,
use a **new** artifact directory; fixture creation refuses existing directories:

```sh
source scripts/common.sh
"$DOTNET" tests/SectorWorks.UITests/bin/Release/net10.0/SectorWorks.UITests.dll \
  --phase2 artifacts/my-phase2-smoke artifacts/my-phase2-smoke/window.png
fsck.fat -n artifacts/my-phase2-smoke/modified.img
mdir -i artifacts/my-phase2-smoke/modified.img ::/STAGED
```

The smoke requires a real graphical session and injects picker paths rather
than automating native file dialogs. It generates synthetic, nonbootable data,
never the ignored owner samples. Other-platform Phase 2 runtime and native
desktop integration remain review work. macOS runtime/signing is expressly
deferred, not silently treated as physically tested.
