# Build and physical-platform status

## macOS support policy change: unsupported, build-only target, 2026-10-16

Owner decision: the owner has no reliable way to test macOS and will not
pursue macOS verification. macOS is reclassified from "intended supported
target, runtime/signing deferred" to a **standing unsupported, build-only
target**. This is a policy change, not a technical regression: macOS is not
dropped. Cross-compiled `osx-x64` and `osx-arm64` builds/publishes continue to
be produced for every release exactly as before. What changes is that no
macOS physical runtime testing, workflow verification, or compatibility
support is performed, claimed, or scheduled going forward, and documentation
must stop describing macOS physical validation as "pending" or "deferred"
(implying it will eventually happen) and instead state plainly that macOS
builds are untested and unsupported.

This supersedes all prior macOS-related entries below that describe macOS as
an "intended supported target" with testing "deferred" pending owner access;
those entries remain as accurate historical record of what was true on their
dated session, not of the current policy. See the technical specification
section 4.2 amendment and [checklist.txt](../checklist.txt) global rules for
the full policy text. Users should be informed (README, release notes) that
macOS builds are provided without testing or support guarantees and may not
work.

## Phase 3 direct-commit implementation and verification, 2026-10-16

Owner explicitly authorized Phase 3 on 2026-10-16 as a scoped child chat
sharing this session's workspace: "Start a child chat under this one and
start Phase 3." Not Phase 4/publication authority; no commit/push/tag/release
authorized or performed.

Delivered: a container/filesystem-agnostic direct-commit pipeline
(`ExplorerService.CommitAsync`, `SectorWorks.Storage/DirectCommitTransaction.cs`)
wrapping the already Phase 2B-qualified `Apply`/`ValidateOutput` writer
pipeline with source-identity rechecking, cross-platform advisory locking,
durable backup, a flushed transaction journal, atomic finalize (`File.Move`),
and startup-style crash recovery. `CommitInPlace` is gated on the same
qualification as `Modify`/`SaveAs`, so the Phase 2C legacy/attribute-list NTFS
rows correctly remain non-editable (3.0.1-3.0.5 were not attempted this
session).

Test evidence (local Linux Mint 22 x64/X11, `~/.local/share/SectorWorks/dotnet`
.NET 10 SDK, `./scripts/build.sh`/`./scripts/test.sh`):

- Build: `./scripts/build.sh` succeeds, 0 warnings/0 errors, after fixing one
  macOS-only CA1416 analyzer error (`FileStream.Lock`/`Unlock` are not
  supported by .NET on macOS; fixed with an early-return platform guard using
  a sidecar marker-file lock instead).
- Full suite (`./scripts/test.sh`, run completed 2026-10-16): SectorWorks.Tests
  14/14, SectorWorks.DependencyTests 27/27, SectorWorks.UITests 68/68,
  SectorWorks.ServicesTests 156/156, SectorWorks.StorageTests 846 passed/1
  Windows-only skip/847 total -- all green. A follow-up targeted
  SectorWorks.StorageTests run then added two new cross-process
  `SourceWriteLockTests` (concurrent-writer rejection verified against an
  independent Python process holding a real fcntl byte-range lock, and
  reliable release/reacquire), both passing.
- New/extended direct-commit test coverage: `DiskStagedWorkflowTests` gained
  `EveryQualifiedRowDirectlyCommitsInPlaceAndLeavesNoResidue` (full FAT matrix:
  5 containers x 3 layouts x 3 FAT variants x sector sizes, independently
  re-checked post-commit with `fsck.fat -n`/mtools),
  `InterruptedCommitBeforeFinalizePreservesSourceAndIsRecoverable`
  (failure-injection: source stays byte-identical and recoverable after a
  simulated post-Apply failure), and
  `RecoveryNeverRestoresBackupOverAnAlreadyFinalizedCommit` (safety property:
  recovery never undoes an already-successful commit).
  `NtfsStagedWorkflowTests` gained the equivalent full-matrix ordinary-NTFS
  commit test, independently re-checked with `ntfsfix -n`/`ntfscat`.
  `NtfsCompatibilityVersionTests` was corrected to expect `CommitInPlace =
  true` for NTFS 3.1 (already Phase 2B-qualified) while keeping it `false`
  for legacy 1.2/3.0 (Phase 2C read-only) rows.
- Known gaps flagged for owner follow-up, not silently resolved: (1)
  `CommitRecovery.Discover` (startup crash-recovery detection) is a tested
  library primitive with no call site yet in the UI application host's
  startup sequence; (2) no UI command/button is wired to `CommitAsync` yet --
  this is a backend-only delivery; (3) macOS locking uses a narrower sidecar
  marker file (not `FileStream.Lock`), untested on physical macOS hardware,
  since macOS is a standing unsupported, build-only target (see the policy
  change entry above); both Mac
  cross-publishes continue to build, no physical-compatibility claim is made.
- 3.0.1-3.0.5 (Phase 2C new-target NTFS legacy/attribute-list staged
  editing/Save As/new-output writer qualification) were not attempted this
  session. Those rows stay blocked/non-editable; no owner scope decision was
  requested or made to waive them.

No versioned release, commit, push or tag was made. See the
[checklist](../checklist.txt) Phase 3 section and
[support matrix](support-matrix.md#phase-3-direct-commit-qualification-implemented-2026-10-16)
for the full per-row ledger.

## ISO reader compatibility refinement, 2026-10-15

The owner separately authorized both bounded read-only fixes found while
investigating an old Toshiba recovery ISO. This is existing-reader compatibility
work, not Phase 3 or Phase 4 implementation. No source rewriting, optical
editing/rebuild, bootability claim or release publication is authorized.

Five zero-length identification files have unused out-of-range extent addresses
in both ISO9660 and Joliet. Opening now warns about that specific quirk; empty
streams do not seek to their unused addresses. Filename-based exemptions and
relaxed block-device bounds are not used. Nonempty files, directory extents,
endian fields and unsupported allocation remain checked.
Two large clip-art directories also exposed repeated parent-directory parsing
in the pinned VFS's per-entry metadata lookups. Bounded batch ISO9660/Joliet
enumeration removes that repeated I/O without raising the 128 MiB/131072-operation
budget, 16 MiB directory cap or 131072-entry cap. Rock Ridge retains its existing
SUSP-aware metadata path; signed dependency packages remain unchanged.
See [reader details](storage-phase1.md) and [synthetic provenance](fixtures.md#iso-reader-compatibility-regressions).

**102 selected storage tests passed, zero skipped**, including **20 new ISO
cases**, existing malformed/container/Rock Ridge readers and all 49 focused
Phase 2C NTFS cases. The synthetic large-directory tests use two nested
1600-file directories for each of ISO9660 and Joliet and require each listing
to read at most 4 MiB in at most 4096 device calls, with at most 1 MiB/request.
They compare attributes, timestamps and content against the signed reader.
Other cases cover empty-file sync/async/EOF/seek/cancellation, bounds and
allocation rejection, directory size/entry caps, Unicode/duplicate/invalid
metadata and shared-service preview/hex/search/extraction with writes denied.
Evidence: `artifacts/iso-reader-targeted.log` and
`artifacts/iso-reader-tests/iso-reader-targeted.trx`.
This is targeted verification, not a new full-suite result; the Phase 2C
full-run chronology below is historical. Authentic historical-media checks are
now deferred as described below; remaining general platform gates stay open.

The unmodified owner ISO was opened through the normal production reader
without bypassing preflight. Every directory and ordinary stream read:
**164 directories excluding root, 5755 files, five empty files and 542516531
content bytes**, matching independent `isoinfo`/7-Zip totals. Both formerly
failing directories now enumerate. All content was streamed and discarded,
not extracted to files, copied into fixtures, executed or redistributed.
The complete 550324224-byte source SHA-256 matches before/after:
`6736138d36deb7c4f0f5db74772656732ac434ce6ac91fa59dc9a4a5d2755e26`.
Its size and original modification time are unchanged. Successful reads do not
certify original-disc integrity or prove the mastering software's intent.
Final-source probe evidence: `artifacts/iso-reader-owner.log`; the complete
tree/content read took approximately 5.4 seconds on this host, not a portable
performance guarantee.

All six self-contained development publishes and the local Linux x64 AppImage
were refreshed successfully; architecture/legal checks passed. Evidence:
`artifacts/iso-reader-publish.log`. Solution-wide formatting passes.
Final solution build passes with zero warnings/errors
(`artifacts/iso-reader-build.log`); the published Linux executable and
extract-and-run AppImage both pass `--verify-ui` resource checks without
starting a desktop. All five authoritative reference-copy pairs match.
Versioned releases and GitHub publication are untouched. Cross-publishing is
not physical Windows/ARM64/macOS or new GUI qualification.

## Phase 2C authorization and initial reader evidence, 2026-10-15

The owner explicitly authorized Phase 2C reader work and the matching roadmap/
specification change before Phase 3. **Bounded reader implemented; remaining
1.0 verification/review gates open, not phase complete.** On 2026-10-15 the owner
subsequently deferred authentic NT4/Windows 2000/XP-created-media qualification
and dependent reader/writer interoperability checks to post-1.0
**P1.NTFS.1-P1.NTFS.6** in the [checklist](../checklist.txt).
This is a scope decision, not new testing or a passed authentic qualification.
VM-authored fixtures suffice; vintage hardware is not required. Automated
tests, resource bounds, Tower source-hash/extraction tasks, general platform
verification and Phase 3 writer/commit safety remain required for 1.0; macOS
is excluded from general platform verification as a standing unsupported,
build-only target (see the 2026-10-16 policy change above). Required **2B.6** current
Windows-media/security-reference/LFS 2.0 checks are not moved.
Stop for owner review; no Phase 3 authorization follows.
Separate targets are **NTFS 1.2 (NT4), 3.0 (Windows
2000), 3.1 (XP+)** ordinary read subsets with bounded supported attribute lists/
extensions and version-specific metadata. New checklist **2C.1-2C.7**,
verification **2C.V1-2C.V8** and exits **2C.E1-2C.E6** began pending.
Focused tests cover normal/malformed lists and
extensions, split streams, version-dependent layouts, public browse/content
hashes/read-only gates and shared-service browse/preview/hex/search/extraction,
staging/Save As denial and unchanged synthetic-source hashes.
**`./scripts/verify.sh` passed: 1001 passed, one Windows-only security-reference
skip, 1002 total**. Counts: 14 foundation, 27 dependency, 68 UI, 156 Services,
736 storage passes/one skip. Storage took 10m50s. The build has zero warnings/
errors; formatting, all five reference-copy pairs, UI resources and AppRun
contracts pass. Log: local-only `artifacts/phase2c-verify.log`.

Four cases were added after that full run's build: named-list guard, GPT
mixed-volume isolation, modern/legacy extension-header identity and zero-length
nonresident index bitmap. The latest focused run passes
**49/49 on current code (35 attribute-list + 14 version/layout/service)**.
That Phase 2C verification snapshot's inventory was **1006 including the skip**, but **a full
1005-pass run is not claimed**: those four cases were not included in the
completed 1002-case run. Focused evidence:
`artifacts/phase2c-targeted.log` and
`artifacts/phase2c-tests/phase2c-targeted.trx`.
The new mixed-volume case proves qualified FAT staging/Save As preserves the
attribute-list NTFS partition's byte hash and never grants that NTFS volume
editing capabilities.

The final repeat **`./scripts/publish.sh all` passed on latest source** for all six self-contained RID targets and
a new local Linux AppImage, with licenses/notices retained. Log:
`artifacts/phase2c-publish.log`. This includes the last two production fixes. No
versioned release assets were touched and no GitHub publication occurred.
Final checks confirm all 40 protected fixture/versioned-release hashes match
the original pre-cleanup baseline, all five reference-copy pairs match and
final solution formatting passes.
Cross-build subwork **2C.V6a** is verified; parent **2C.V6**
general physical requirements stay open. Cross-publishing does not establish physical
compatibility. Authentic NT4/Windows 2000/XP authoring/runtime evidence remains
incomplete in post-1.0 **P1.NTFS**, not a 1.0 blocker; MFT bootstrap lists
remain an explicitly blocked reader subset.
No new physical GUI/platform-matrix qualification follows from these commands.
Version fixtures are reconstructed legal modern-golden derivatives, not authentic
NT4/2000/XP-authored or native-runtime evidence;
[fixture provenance and exact SI/security layouts](fixtures.md#verified-structural-version-fixtures).
The version cases exclude attribute lists; the separate 35-case attribute-list
suite covers those. Scoped formatting passes and immutable goldens remain unchanged.
Phase 2B results below remain historical.

No authentic NT4 fixture exists yet; synthetic version-field tests alone are
not authentic legacy qualification. Tower's approximately 8.42 GB raw NTFS 3.0
owner image remains outside the repository and must not be copied, modified
or distributed. Initial diagnosis is rejection at a live directory attribute
list in MFT 26 referencing indexing extension 8336.

The new reader now opens Tower **in place as NTFS 3.0**, recursively enumerating
**905 directories and 16,199 files with zero directory failures** through the
retained signed-reader enumeration filters, not every on-disk entry. Root has 24
entries; native `/WINNT/system32` listing has 1851 entries. Writing remains false;
source size and mtime are unchanged. Complete streams for three explicit paths
match independent `ntfscat` SHA-256 results:

| Owner-image stream | Bytes | Independent full-stream SHA-256 comparison |
| --- | --- | --- |
| `/boot.ini` | 191 | Match |
| `/WINNT/system32/ntdll.dll` | 491792 | Match |
| `/WINNT/system32/kernel32.dll` | 743184 | Match |

The service opens `ReadyReadOnly`, with a visible 45-character text preview,
20480-character hex result and one search hit. An initial service selection of
hidden/system `boot.ini` failed because existing DiscUtils hidden/system/metafile
enumeration filters were retained. Repeating with enumerated ordinary entries
succeeded; no filter or pre-existing presentation behavior was changed.
The low-level explicit-path stream probes above are not a claim that every
on-disk entry appears in service listings.

No owner content was extracted or persisted; the private image was not copied.
Source size/mtime remain unchanged, **not a full-image hash comparison**.
The local session JSON `phase2c-owner-result.json` records the stream hashes and
service metrics; its `SourceUnchanged` flag represents the size/mtime check,
not whole-image SHA-256. The observed owner-image writer rejection also reports
BPB hidden-sector/absolute-offset mismatch, with no source normalization.
These are bounded local read results, not authentic authoring provenance,
physical-platform matrix completion, full extraction or volume-health certification.
The final owner probe on latest source repeats the same counts, three native
stream hashes, service workflows and source size/mtime checks successfully.
No full-image hash, extraction/content persistence or filter behavior change
was introduced by that repeat.

The independent per-directory `ntfsinfo` "Value too large" warning is
**resolved as a tool-size limitation** (**2C.6/2C.V5**), not corruption evidence:
the pinned public ntfs-3g **2022.10.3**
[`ntfs_attr_readall`](https://github.com/tuxera/ntfs-3g/blob/2022.10.3/libntfs-3g/attrib.c#L6988-L7000)
rejects non-BITMAP attributes larger than **65536 bytes** with `EOVERFLOW`;
[`ntfsinfo`](https://github.com/tuxera/ntfs-3g/blob/2022.10.3/ntfsprogs/ntfsinfo.c#L1955-L1959)
calls that helper for the whole `INDEX_ALLOCATION`. Tower's in-bounds single
contiguous index is **344064 bytes**, exceeding that limit. Native root listing
succeeds independently. These pinned source paths explain that warning only:
neither corruption nor complete volume
health follows from a native-tool warning or its explanation.

The initial reader also handles a resident `INDEX_ROOT` compression flag as the
directory's default compression setting, and zero-length nonresident index
streams. Compressed file contents remain denied; these cases do not broaden
writer qualification. A separate `NtfsReadValidation` preflight/resolver is
implemented; signed reader packages stay unchanged. Strict writer validation
retains its old default path; only its diagnostic now names the Phase 2B writer
subset/Phase 3 instead of Phase 1. MFT bootstrap lists remain explicitly rejected.
Final review checks the modern extension record identity at offset 44 only when
USA offset is at least 48; legacy offset 42 must not be misread as a record number.
Zero-length nonresident index bitmaps now avoid an invalid empty-run read.
Both tightly coupled fixes have current focused regression coverage.
Resource caps and active-index/inactive-MFT handling are recorded in the
[reader bounds](storage-phase1.md#resource-cancellation-and-concurrency-policy).

Newly readable legacy/attribute-list variants remain noneditable until Phase 3
**3.0.1-3.0.5** staged/Save As/new-output writer qualification precedes **3.7**
direct commit. Existing qualified Phase 2B editing remains intact; actual
Windows-authored-media/security-reference and LFS 2.0/cached-metadata write
gates remain incomplete in **2B.6**, not moved or scope-demoted.
SSH access below is infrastructure only. Windows/ARM64 runtime gaps remain
open; macOS is a standing unsupported, build-only target (2026-10-16 policy
change, see top of this document). No Phase 3 or release is authorized.
See [planned reader ledger](support-matrix.md#phase-2c-ntfs-reader-qualification-plan).

## Local artifact cleanup, 2026-10-15

Owner-authorized cleanup removed reproducible project `bin`/`obj` trees,
byte-identical release-download checks, empty test/inspection directories and
superseded pre-freeze Phase 2B synthetic image/conversion files. Earlier-run
screenshots, small inputs and logs remain; final FAT
`artifacts/phase2b-release-x11-<variant>/` and managed NTFS
`artifacts/ntfs-managed-x11-<ntfs-variant>/` evidence remains intact.
All six current publish trees, AppImage, versioned release assets, immutable
goldens, owner samples, source/provenance materials and local access records
were retained. Cleanup does not rerun or change historical qualification.
Build the test harness again before using its direct executable commands.

## Windows 11 Home x64 SSH access, 2026-10-15

The owner prepared a physical Windows 11 Home x64 laptop with OpenSSH Server
and reports working password SSH as the dedicated standard local `swtest`
account. After the owner enrolled a dedicated Ed25519 public key, the agent
verified unattended access with an explicit identity, `IdentitiesOnly=yes`,
`BatchMode=yes`, publickey-only authentication, `StrictHostKeyChecking=yes`
and `-T`: remote `whoami` returned the expected laptop/`swtest` identity,
exit code 0. This is access evidence, not application runtime evidence.
The existing client trust entry was used; independent local server-fingerprint
comparison has not been confirmed. Do not bypass host-key verification.

The owner selected a passphrase-free automation key stored outside the
repository/Dropbox, in an owner-only directory with owner-only key files.
The enrolled entry restricts the source to the Linux PC and disables PTY/
forwarding. Password authentication remains unchanged. Connection addresses,
hostname, key material and machine-specific enrollment are not committed.
See the [copyable SSH setup and troubleshooting guide](windows11-openssh-setup.txt)
for firewall/source restrictions, the distinct host/client keys, standard-user
ACLs, enrollment, strict unattended verification and revocation.

No SDK/Git installation, source transfer, Windows build/test run, Windows
security-reference execution, Windows-authored NTFS qualification or GUI
automation has been performed as part of this setup. Prepare/inspect tools,
then run finite command-line verification separately. GUI smoke requires an
interactive logged-in desktop, not just SSH. All Phase 2B Windows runtime and
modern-log gaps below remain incomplete; Windows ARM64/macOS evidence is
unchanged. No Phase 3 or recurring automation is authorized.

## Managed NTFS repair: current qualification and remaining gaps

The owner authorized implementation after the bounded investigation. The
isolated managed backend/security component now passes the 383-case targeted
suite: **382 passed, one explicit Windows-only security-reference skip**.
The 21 NTFS disk rows include actual Services history/preview/hex/search/
extraction/Save As, qemu interoperability and unchanged source hashes.
Native fixtures separately qualify all eight 512-byte-sector cluster sizes
512 through 65536, including complete cluster-dependent MFT mirrors,
resident/nonresident transitions, index/MFT growth, independent SI/FN/index/
DOS-alias/security preservation, full-volume deletion, near-full allocation with
exactly 16 free clusters, cancellation and real I/O/flush/validation failures.
Existing FAT qualification remains separate and unchanged.

Final `./scripts/verify.sh` passes **956 tests, one explicit Windows-only skip,
957 total**: 691 storage passes/one skip, 156 Services, 68 UI, 14 foundation and
27 dependency tests. Locked restore, Release build with zero warnings/errors,
formatting, UI resources, AppImage contracts and authoritative reference-copy
comparisons pass. All six final self-contained publishes and the AppImage
prototype pass; ELF/PE/Mach-O architecture headers, repaired/original assembly
presence, matching legal inventory and owner/test-media exclusion are checked.
Published Linux x64 and both AppImage FUSE/extraction-and-run resource checks pass.

Six actual Mint 22 x64/X11 workflows pass for NTFS filesystem-only raw,
primary-MBR raw, fixed/dynamic VHD and fixed/dynamic VHDX. They use newly
`mkntfs`/`ntfscp`-authored disposable media, rendered commands, all five edits,
history/review, staged preview/extraction, Save As/reopen, cancelled close and
discard. Each source hash remains unchanged. Independent qemu conversion,
bounded `ntfsfix -n`, `ntfsinfo`, `ntfscluster`, `ntfscat` and exact comparison
against the staged replacement bytes pass for all six outputs.
Evidence and app-window-only screenshots are local under
`artifacts/ntfs-managed-x11-<ntfs-variant>/`; owner media is not used.

**Phase 2B remains partial.** Actual Windows-authored NTFS fixtures and Windows
security-reference execution have not yet been prepared/run, not passed.
A Windows x64 SSH host is now accessible as recorded above. Clean LFS 1.1 has
synthetic structural coverage only; LFS 2.0/cached-metadata safety remains
blocked. This is not a claim that normal Windows volumes are writable, nor an
owner-approved NTFS read-only scope reduction for 1.0.
Windows/ARM64 runtime remains unverified; macOS runtime/signing remains expressly
deferred. Cross-publish evidence is build evidence only. Formatting, replay/reset,
repair, source commit and recovery stay disabled.

## Historical Phase 2B FAT prerelease delivery and physical evidence

Verification host: Linux Mint 22 x86-64/X11, SDK 10.0.402 / runtime 10.0.13,
2026-10-15, before the subsequent managed NTFS repair. At that release
**Phase 2B was partial/blocked:** FAT editing was qualified in
the [declared envelope](staged-editing.md#current-phase-2b-envelope-and-mandatory-blocker);
ordinary NTFS Add/mkdir were blocked by portable ACL inheritance, independently
of formatting. Existing-file mutation probes did not close that mandatory exit.

Final `./scripts/verify.sh` passed: **561 tests, zero skips**, comprising 309
storage/integration, 156 services/platform, 66 UI and 30 foundation/dependency.
Locked restore, zero-warning/error Release build, formatting, UI resources,
AppImage contracts and all authoritative reference comparisons pass.
All six final self-contained cross-publishes and AppImage packaging pass;
ELF/PE/Mach-O architecture headers, legal files and owner-sample exclusion
were checked. Linux x64 and both AppImage FUSE/extraction-and-run resource
diagnostics pass. No tests were disabled to obtain these results.

The 63-row FAT matrix covers raw/fixed VHD/dynamic VHD/fixed VHDX/dynamic VHDX,
filesystem-only/primary MBR/GPT, FAT12/16/32, and separately VHDX physical sectors
512 and 4096 with 512 logical sectors. The same rows also exercise actual
Services staging, all five operations, ordered review/undo/redo/discard,
browse/preview/hex/search/extraction, validated Save As and unchanged source
hashes. Independent qemu raw conversions and FAT checks are separate evidence
from the managed reader. 4Kn remains rejected.

Actual X11 rendered-command workflows passed for primary-MBR raw, fixed/dynamic
VHD and fixed/dynamic VHDX with mixed-case directories and Unicode file names.
All exercise five operations, history/review, preview/extraction, Save As/reopen,
cancelled close and discard; generated source hashes stay unchanged.
Final FAT outputs pass `fsck.fat -n` and mtools. Independent fixed/dynamic VHDX
checks use qemu-authored fixtures and qemu output reopen/conversion, not the
historical synthetic fixed-reader fixture. Available tools: qemu 8.2.2,
dosfstools 4.2 and mtools 4.0.43. Fixed VHD requires explicit qemu `-f vpc`
because raw auto-detection is ambiguous.

Earlier failed VHDX attempts were safely rejected (fixed BAT qualification and
upstream dynamic ContentStream.Flush); they are not counted as success. The
corrected writer disposes the container and flushes the underlying file before
independent reopen. Local successful evidence is in
`artifacts/phase2b-lfn-x11-{raw-mbr,vhd-fixed,vhd-dynamic}/` and
`artifacts/phase2b-final-x11-vhdx-{fixed,dynamic}/`.
After code freeze all five were independently repeated in
`artifacts/phase2b-release-x11-{raw-mbr,vhd-fixed,vhd-dynamic,vhdx-fixed,vhdx-dynamic}/`;
qemu conversion/checks, `fsck.fat -n`, mtools long-name listing and `mtype`
content equality all pass.
The smoke supplies dialog paths; native-picker/manual accessibility review
remains incomplete. No Windows/ARM64 Phase 2B runtime claim follows from the
owner's historical Phase 1 reports.

| Runtime identifier | Phase 2B target | Physical workflow evidence |
| --- | --- | --- |
| linux-x64 | Self-contained portable archive and AppImage prototype | Mint 22/X11 qualified FAT staged/Save As workflows |
| linux-arm64 | Self-contained cross-publish | Phase 2B unverified |
| win-x64 | Self-contained cross-publish | Phase 2B unverified |
| win-arm64 | Self-contained cross-publish | Unverified |
| osx-x64 | Self-contained cross-publish | Runtime/signing explicitly deferred |
| osx-arm64 | Self-contained cross-publish | Runtime/signing explicitly deferred |

The six-target build is not physical compatibility, signing/notarization,
Wayland, full-distro or production qualification. macOS remains intended and
its earlier launch report remains unresolved. Owner samples remain ignored,
hash-identical and absent from packages.
The earlier date-failure regression expected a later fingerprint error; stricter
source/output qualification now catches invalid dates first. Its expectation
was updated to require that structural/timestamp failure, not weakened to
accept output. Four additional regressions prove valid-but-changed untouched
attributes/dates cannot publish despite matching trees/content hashes.

## Phase 2 target policy

On 2026-10-14 the owner authorized Phase 2 and explicitly retained **macOS as
an intended supported target**, despite not having a Mac available for reliable
testing. Both macOS cross-publish targets remain required. Runtime/workflow,
bundle signing and notarization validation are deferred until suitable access
is available; this does not block Phase 2. Successful cross-publishes do not
establish physical compatibility or resolve the earlier launch report.

The physical results below are historical Phase 1 evidence, not Phase 2 write
workflow validation on Windows or ARM64.

## Phase 2 verified evidence

Verification date: 2026-10-14. Host: Linux Mint 22 x86-64, X11.
SDK 10.0.402 / runtime 10.0.13.

Latest locked restore, Release build with zero warnings/errors, **313 tests**
with zero skips, formatting, UI resources and all authoritative reference-copy
comparisons passed. Counts: 141 services/platform, 79 storage, 63 UI (including
private staging-platform primitives), and 30 foundation/dependency tests.
No tests were disabled to obtain these results.

| Runtime identifier | Phase 2 self-contained cross-publish | Phase 2 physical runtime |
| --- | --- | --- |
| linux-x64 | Passed; ELF x86-64 header checked | Mint 22/X11 staged-edit workflow passed |
| linux-arm64 | Passed; ELF AArch64 header checked | Not retested for Phase 2 |
| win-x64 | Passed; PE x86-64 header checked | Not retested for Phase 2 |
| win-arm64 | Passed; PE AArch64 header checked | Unverified |
| osx-x64 | Passed; Mach-O x86-64 header checked | Explicitly deferred per owner |
| osx-arm64 | Passed; Mach-O arm64 header checked | Explicitly deferred per owner |

All six existing publish folders were refreshed with the final code and legal
notices. The published Linux x64 executable also passes `--verify-ui`.
No Mac runtime, signing, notarization or physical compatibility claim is made.

The real X11 smoke creates its own clean disposable FAT12 image; it exercises
all five edit types, staged markers, ordered review, undo/redo, preview,
extraction, Save As, independent output reopen, cancelled close and discard.
The original generated source hash is unchanged. The final output passes
`fsck.fat -n` (dosfstools 4.2) and `mdir` (mtools 4.0.43), including valid renamed
file and directory-special-entry dates. The app-only screenshot and output are
in `artifacts/phase2-verified-x11/`.

The owner-requested short-name case refinement also passes FAT12/FAT16/FAT32
round trips, native FAT checks, all four display-flag combinations, mixed-case
lookup, case-insensitive conflicts, parent-case preservation, replacement,
case-only file/directory rename with descendant edits and undo/redo, and
exact-spelling failure injection. A new rendered-command test exercises lower
names and case-only rename through Save As. The existing real X11 workflow was
rerun with the refined code; its output passes `fsck.fat -n` and mtools. Artifacts:
`artifacts/fat-case-x11/`. No case-specific native-picker review is claimed.

A disposable copy of the owner's `starter.img` now qualifies and successfully
saves a new lowercase `test.txt`, retaining all five original names/file hashes,
including `ctmouse.exe`, and unchanged original/source-copy SHA-256.
Its original blank boot-sector label warning remains identical in source and
output: `fsck.fat -n` returns 1 for both, so this image is not described as a clean
native-check fixture or silently repaired. mtools confirms the original/new
lowercase names. Diagnostic output is local-only in
`artifacts/fat-case-starter-check/`; the owner image is ignored and not bundled.

Automated round trips independently cover the qualified FAT12, FAT16 and FAT32
write envelopes with both native FAT tools available locally. Regressions cover
incorrect content/tree/allocation/dates, cancellation, output-name races, source
mutation, capacity/name/read-only conflicts, oversized metadata, private spool
permissions, bounded imports, quota limits and abandoned-redo cleanup.
Immutable golden hashes still pass. The owner's ignored diagnostic image and
binary sample retain their original hashes and are absent from publish output.

Native picker interaction, Windows/ARM64 Phase 2 runtime, broader filesystem/
power-loss testing and other desktop environments remain unverified. These
results do not establish bootability, exact-layout preservation, production
readiness or Phase 3 recovery. See [qualified scope and reproduction](staged-editing.md).

## Owner-authorized Linux x64 AppImage packaging

On 2026-10-14 the owner separately authorized an AppImage for the current
development build and normal Linux x64 publish artifacts, not the rest of
Phase 5. Existing SquashFS/desktop tools were present; appimagetool was absent.
With owner approval, official appimagetool 1.9.1 and an explicitly pinned
type2-runtime were installed user-locally and SHA-256 checked.

`publish.sh linux-x64` and `publish.sh all` now invoke AppImage packaging;
other RID publishes do not require these tools. Packaging never downloads
tools/runtime automatically. Missing or changed tools fail explicitly.
The installer rejects an upstream runtime download if its bytes change from
the reviewed pin; update pins deliberately rather than accepting a moving tag.

The release AppImage retains all 277 original Linux x64 publish files
byte-for-byte plus seven runtime notice/source-reference files. No owner
images or diagnostic output is bundled. Compression is limited to two workers;
host ownership/extended attributes are not copied. The temporary launcher
SVG reuses the original disk glyph and is not final product branding.

Verified on Mint 22/X11: FUSE execution and extraction-and-run UI resources,
normal publish generation and checksum, argument/working-directory/exit-status
contracts, missing/tampered-tool failures, and actual GUI startup with a
synthetic image path containing spaces. The image opened, the GUI closed
cleanly, and the synthetic source SHA-256 was unchanged. Local smoke artifacts:
`artifacts/appimage-gui-smoke/`.

AppImage is not a sandbox, a static GUI bundle or full distro/Wayland coverage.
Native desktop/system libraries remain required. Runtime notices and upstream
source/build references are included under `licenses/AppImage/` in the payload.
See [commands and prerequisites](../README.md).

The verified AppImage was added to the existing private
[v0.2.0-phase2 prerelease](https://github.com/efflixi/SectorWorks/releases/tag/v0.2.0-phase2)
without moving its source tag or replacing the six archives. The checksum
manifest now covers seven packages; GitHub sizes/digests for all eight assets
match local files. The downloaded AppImage is byte-identical and passes its
resource check.

## Phase 1 evidence

Verification date: 2026-10-14 (development host local date).
Host: Linux Mint 22 x86-64, X11. SDK 10.0.402 / runtime 10.0.13.

Locked restore, Release solution build (zero warnings/errors), all 196 solution
tests (zero skips), formatting, document-copy comparisons and display-free
`--verify-ui` pass. The solution now includes storage, services/platform and
rendered UI tests in addition to the thirty foundation tests: 61 storage,
62 services/platform (including 11 preferences tests) and 43 UI tests.

| Runtime identifier | Final Phase 1 self-contained cross-publish | Physical validation |
| --- | --- | --- |
| linux-x64 | Passed | Local Mint 22/X11 golden workflows passed with unchanged source hashes; owner also reports Phase 1 working |
| linux-arm64 | Passed | Owner reports Phase 1 working on Raspberry Pi 500 |
| win-x64 | Passed | Owner reports Phase 1 working; Windows 7 x64 also reportedly runs this build (unsupported) |
| win-arm64 | Passed | Not run on Windows ARM64 |
| osx-x64 | Passed | Runtime unverified; macOS launch blocker reported, tested architecture unspecified |
| osx-arm64 | Passed | Runtime unverified; macOS launch blocker reported, tested architecture unspecified |

### Owner-reported physical testing, 2026-10-14

The owner reports that Phase 1 works well within its existing limitations on
Linux x64, Windows x64 and Linux ARM64, the latter on a **Raspberry Pi 500**.
These are human test reports, not agent-executed automated suites on those hosts.
Exact additional Linux distributions/versions, display backends, Windows
versions (apart from Windows 7 x64), image sets and per-workflow results were not
supplied. Do not infer exhaustive platform, filesystem or desktop-integration
coverage. Raspberry Pi 500 evidence does not complete Raspberry Pi 5-specific
performance or Raspberry Pi OS qualification.

The current build also reportedly works on **Windows 7 x64**. Record this as
functional but unsupported/best-effort; .NET 10 does not support Windows 7.
Service-pack level, installed prerequisites and launch environment were not
reported. This observation does not establish future compatibility or alter the
modern runtime baseline.

**macOS testing is blocked at launch.** The owner reports that the unsigned
distribution was blocked even after attempting to disable security. Successful
runtime/workflow verification has therefore not occurred. macOS version, CPU
architecture and exact diagnostics were not supplied; signing/packaging is a
reported concern, not a confirmed sole cause. Investigate the launch diagnostics,
binary/library signatures and distribution packaging on a Mac before claiming
compatibility. Do not treat disabling system security as a verified workaround.

### First hex page regression and fix, 2026-10-14

The owner narrowed the failure to initial page loading: selecting the Hex View
tab displayed no bytes until Next/Previous was used. Files fitting in one page
had no paging workaround. This was reproduced in UI tests: tab selection did not
invoke the hex read command. The tab now loads the current page, and an explicit
hex request cancels pending text preview so a late result cannot switch back.
Text encoding was not the cause; raw hex needs no text decoder.

Nineteen new regressions cover tab activation, empty/short/partial-row/exact-page/
multipage files (0, 1, 15, 16, 17, 1494, 4096, 4097 and 9000 bytes), Next/Previous,
single initial reads and late text-preview completion. Real generated ISO binary
files pass through the storage service; displayed hex bytes are checked against
the generated input. All 43 UI tests pass. Headless sessions share a test
collection to avoid concurrent access to Avalonia's process-global platform.

The supplied LEVEL01.OPL is 1494 bytes, SHA-256
`0e9f3bafd9af34ae918777daf34f2a712ea398b2835837a1bf9ab9422f630ec2`.
A disposable local ISO containing that file verified every hex row against its
bytes. A real Mint/X11 workflow selected the actual Hex View tab, displayed the
first page, searched/navigated back and selected it again, then extracted the
file with the same hash. No original VHD was needed, no content was executed,
and the supplied file was unchanged. The sample is ignored, not redistributed
as a golden fixture. The fix has local validation; earlier owner platform
reports describe the preceding build and are not retests of this fix.
Full local verification passed with 196 tests, zero skips, a zero-warning build,
format/resource checks and synchronized specifications. All six self-contained
publish folders were refreshed successfully with the fix; this does not resolve
the existing macOS signing/launch blocker.

The production executable was also launched with an image argument and closed
cleanly via its own window's close message. Parent integration repeated all nine
minimum-size workflows with the final service stack and hardened smoke assertions,
independently confirming every source hash remained unchanged. Screenshots
capture only SectorWorks window content, not the shared desktop.

Owner-requested refinements also passed physical X11 runs on the FAT12 golden
and the original Compaq diagnostic floppy: actual Preferences save/reload,
selection-preserving sorting and synchronized TreeView navigation. Preferences
tests use isolated settings, not the user's real configuration. Compaq README
extraction matched its 1156-byte length and SHA-256; the original image's hash
remained unchanged. Its legacy text encoding is explicitly unsupported by plain
text preview; Hex View remains available. All six refined cross-publishes pass.

Skia-backed UI tests exercise 100/125/150/200% scaling, twenty vector icons at
16/18/20/24 logical pixels, a 1000x640 minimum window and text contrast >=4.5:1.
Linux/X11 physical smoke additionally used process-local 125/150/200% scales.
A 50,000-entry grid realizes at most 100 rows. These results are not Windows,
macOS, Wayland or ARM64 validation.

Native picker operation, pointer splitter dragging, clipboard integration and
exhaustive assistive-technology testing require human desktop review. Smoke
injects extraction destinations rather than operating native dialogs. The owner
reports above add Windows x64 and Raspberry Pi 500 runtime evidence, but do not
complete that detailed review. Windows ARM64, successful macOS runtime execution,
explicit Wayland coverage, the full Linux distribution matrix and Raspberry Pi
5-specific validation remain unverified. No minimum OS compatibility, production safety,
bootability preservation, write/rebuild or audio claim follows from these tests.

See [UI evidence and reproduction](ui-phase1.md),
[storage limits](storage-phase1.md) and [support matrix](support-matrix.md).
DataGrid 12.1.2 is a separately versioned MIT managed control compatible with
Avalonia 12.1.4; Avalonia.Headless 12.1.4 is test-only. Updated package inventory
and original vector-icon attribution are retained in the legal documentation.

## Foundation evidence (historical)

Verification date: 2026-10-13 (development host local date).
Host: Linux Mint 22 x86-64, X11 session. SDK 10.0.402 / runtime 10.0.13.
Foundation solution verification passed: locked restore, Release build
(zero warnings/errors), all 30 tests (zero skipped), formatting and supplied
document comparisons. Dependency tests include all 14 immutable golden hashes,
read/write probes and bounded malformed-input child processes.

| Runtime identifier | Release self-contained cross-publish | Physical validation |
| --- | --- | --- |
| linux-x64 | Passed on Linux host | Foundation console diagnostic executed; no GUI/workflows yet |
| linux-arm64 | Passed on Linux host | Not run on ARM64 hardware |
| win-x64 | Passed on Linux host | Not run on Windows |
| win-arm64 | Passed on Linux host | Not run on Windows ARM64 |
| osx-x64 | Passed on Linux host | Not run on Intel Mac |
| osx-arm64 | Passed on Linux host | Not run on Apple Silicon Mac |

At foundation completion there was no Avalonia window. Those results did not verify
desktop startup, image browsing/extraction, minimum OS versions, dialogs,
clipboard, DPI, audio, packaging, signing or notarization. Raspberry Pi 5,
Wayland and additional distro testing remain future work. No physical platform
checklist items have been marked complete from these cross-publishes.

## Avalonia dependency review

Avalonia 12.1.4 uses the MIT license, XAML and managed .NET APIs. It provides the
desktop/MVVM foundation required by the specs; CommunityToolkit.Mvvm 8.4.2 (MIT)
supplies observable properties/commands without imposing a storage/UI coupling.
The upstream Avalonia repository has active release development; version and
license evidence comes from pinned NuGet metadata and retained upstream license.
No Avalonia upstream test suite was run here. Our own ten architecture/MVVM
foundation tests cover layer references and observable property notifications.

Rendering/text shaping necessarily introduce native dependencies. No extra native
storage dependency is introduced. Inspected resolved assets and published binary
headers confirm:

| Dependency | Linux x64 / ARM64 | Windows x64 / ARM64 | macOS x64 / ARM64 |
| --- | --- | --- | --- |
| SkiaSharp 3.119.4 | ELF assets for both | PE assets for both | Universal Mach-O with both slices |
| HarfBuzzSharp 8.3.1.3 | ELF assets for both | PE assets for both | Universal Mach-O with both slices |
| Avalonia.Native 12.1.4 | Not used | Not used | Universal Mach-O with both slices |
| Avalonia ANGLE 2.1.27548.20260419 | Not used | PE assets for both | Not used |

The .NET runtime/apphost is likewise target-specific ELF, PE or Mach-O with the
requested 64-bit architecture. Asset availability is not proof of physical
compatibility. Managed storage and UI assemblies do not introduce 32-bit targets;
transitive packages may contain unused x86/ARM32 assets.

Host `ldd` checks on published linux-x64 Skia/HarfBuzz showed no missing dynamic
libraries. Dependencies include glibc, libfontconfig, libfreetype and related
font/image libraries. X11, OpenGL, input and desktop integration dependencies
need actual window testing in Phase 1/5. .NET also needs its documented system
libraries (including ICU/OpenSSL as applicable). Do not infer a minimum OS or
runtime deployment requirement solely from successful cross-publishing.

Native licenses include bundled notices in addition to wrapper MIT licenses;
see [third-party notices](../THIRD-PARTY-NOTICES.txt) and [retained licenses](licenses/).
Trimming and AOT are explicitly disabled until separately evaluated.

## Local command error checks

- Missing SDK override: exit 127, setup stage explicitly identified.
- Unsupported RID: exit 2, arguments stage explicitly identified.
- Intentional invalid build switch: nonzero MSBuild exit propagated, build stage
  explicitly identified.
- Documentation copies were byte-compared with supplied originals.

Initial cross-publish exposed a RID/lock mismatch. This was fixed by declaring
all six RIDs solution-wide and restoring their committed lock graph without a
single-RID override. All six subsequent publishes passed; scripts do not bypass
locked restore to hide failures.

## Resolved audio scope

On 2026-10-13 the owner confirmed that the detailed checklist governs audio scope.
BIN/CUE support and functional CD-DA playback belong to Phase 6. Phase 1 reserves
the optical/audio structure and pane tabs without implementing these workflows.
The UI specification and supplemental guide were corrected, with repository
documentation copies kept synchronized. The optical/audio mockup remains the
approved later-phase visual direction. Phase 1 was subsequently authorized on
2026-10-14; it reserves those panes without implementing playback.
