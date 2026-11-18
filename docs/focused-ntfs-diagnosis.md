# Focused NTFS decision report — November 17, 2026

## Superseding scope/acceptance decision — November 17, 2026

The owner subsequently [approved the 1.0 scope change](sectorworks_1_0_scope_change_and_phase_closeout_sol_prompt.txt).
The technical findings and original source/test identities below are unchanged.
Default TxF/CLFS/KTM and broader LFS2/repair/deleted-state qualification is
**DEFERRED POST-1.0** to **P1.NTFS.MODERN.1–.6**; both physical ARM64 targets are
**NOT VERIFIED — DEFERRED POST-1.0 BY OWNER** under **P1.ARM64.LINUX** and
**P1.ARM64.WINDOWS**. The accepted native passive subset is retained unchanged.
[The exit audit](1.0-scope-closeout.md) closes Phase 2/2B/2C/2D and Phase 3 as
**COMPLETE / OWNER ACCEPTED UNDER THE REVISED 1.0 SCOPE**. Earlier requests for
scope selection/acceptance below are historical, now satisfied by that decision;
no technical gate is relaxed, no deferred test is passed and Phase4 is not started.

## Original focused diagnosis — findings and evidence preserved



**Ready for owner review. Neither Phase 2D nor Phase 3 is declared complete.**
A useful native Windows NTFS subset is now implemented and independently
verified. Default transaction-bearing NTFS still needs a defensible offline
admission rule. No Phase 4, push, tag, release or owner acceptance is inferred.

**Physical Linux ARM64 and Windows ARM64: NOT VERIFIED — NONBLOCKING BY OWNER DECISION.**

## Decisions

| Blocker | Cause and decisive evidence | Disposition / phase effect |
| --- | --- | --- |
| Reduced native repair/deleted control | A blanket `$Extend` exclusion hid further refusals: a valid empty nonresident `$Repair` stream, a zero security ID on the fixed resource directory, named system streams, and format flags treated as pending recovery. Native no-edit/five-operation controls leave the repair/deleted structures unchanged. Separate native formats isolate `0x0400` for TxF disable and `0x1000` for repair-log disable. | **FIXED AND VERIFIED** for the exact bounded passive subset below. This opens genuine Windows-authored staged editing, Save As and commit; it does not pass default TxF rows. |
| LFS/cache requirement for ordinary offline use | All three fresh format-axis controls have two detached **1.1** restart pages, including default TxF-enabled format. Earlier complete lock/flush evidence locates mounted 2.0→1.1 in that interval. The live 2.0 capture was sequential. | **FIXED AND VERIFIED for the clean1.1 workflow**: 2.0 writing is unnecessary for this demonstrated subset, and no host normalization is required. A coherent clean detached 2.0 writer rule remains **UNRESOLVED**, not impossible or waived. |
| Default TxF/CLFS/KTM | Known active controls have a state-zero root and 100%-free TOPS while a child transaction runs. Completed controls retain historical records and file LSNs. Both-client native transport works, but these observations do not identify a persistent prepared/in-doubt-free active checkpoint. | **UNRESOLVED: bounded inactive-state admission**, rather than an access problem or proof that all clean TxF images are unsafe. Transaction-bearing images remain read-only. Required default-TxF/modern-log gates keep the phases incomplete. |

## What changed

The [validator](../src/SectorWorks.Storage/NtfsWriteValidation.cs#L301) requires
NTFS 3.1,512-byte sectors, 4096-byte clusters/index blocks, 1024-byte MFT records,
exactly `0x1400` volume flags, and the existing two clean LFS 1.1 restart-page
checks. It requires exactly `$Repair` beneath `$RmMetadata`, empty repair data,
the observed eight-byte configuration, and an empty `$Deleted` index with a
zero bitmap and at most 64 KiB of fully accounted preallocation. TxF/TOPS/unknown
children, `$TXF_DATA`, pending flags and other unsupported features still fail.
The format comparison also shows that `/NoRepairLogs` **does not disable general
self-healing**: its native query stays 0x9. No stronger interpretation is assumed.

[Empty-run parsing](../src/SectorWorks.Storage/NtfsValidation.cs#L203) now accepts
only exact empty lengths/VCN bounds. The native zero-security-ID exception is
limited to the independently identified resource directory; user records retain
their security checks. [Output validation](../src/SectorWorks.Storage/NtfsImageWriter.cs#L224)
compares complete protected records, every allocated byte and their resolved
shared security descriptors. Allocation, namespace, security indexes, mirror,
boot sectors and unchanged log validation remain intact. No system metadata was
removed. Windows mounting, formatting, replay and repair are not application
prerequisites.

Add/replace/delete/rename/mkdir update ordinary data, MFT/directory/security
allocation as before. There are no offline open-file deferred deletions and no
transaction feature in this admitted state. Empty repair/deleted metadata is
preserved rather than synthesized or blindly exempted from validation.

The [regressions](../tests/SectorWorks.StorageTests/NtfsNativePassiveMetadataTests.cs)
include neighboring rejected states and alteration of allocated but logically
unused bytes. The native fixture is local-only, ignored by Git and excluded
from application packages; [provenance](../fixtures/qualification/native-notxf.provenance.json)
and archive restore mappings preserve it. Missing local input produces explicit
NOT VERIFIED skips, not a native qualification pass.

## Validation and its limits

- Full Linux verification: **2405 passed, one Windows-only skip, 2406 total**;
  zero build warnings/errors; format, UI resources, shell/AppRun and five
  authoritative-copy checks pass with a fresh private `TMPDIR`. This resolves
  execution of verification; the original MSBuild temporary-directory failure's
  underlying cause is not established.
- That full run built the initial native fix. Final review then added the
  shared-descriptor preservation assertion. Final build/format, **18/18 focused
  regressions**, all four publishes and the AppImage pass after that hardening.
  The final complete validator passes **32 retained exact output volumes**.
  This is staged final validation, not a second claimed full-suite run.
- **15 unique container/layout rows** cover raw, fixed/dynamic VHD and
  fixed/dynamic VHDX × filesystem-only/MBR/GPT:30 Save As/commit outputs and 210
  independent Linux NTFS commands. A supplementary repeat sealed every physical
  input; it is not 15 additional distinct capability rows. The initial retention
  gap and first QEMU size-rounding refusal remain recorded.
- Windows native matrix: **60/60** read-only/writable cases over 30 byte-verified
  output volume streams. All 30 read-only VHD oracles retain their entire hash.
  Native wrappers verify **filesystem bytes**, not blanket host mounting of every
  original container. Fresh decoding of all 30 original outputs matches those
  exact oracle streams.
- The published standard-user Windows GUI performs all five operations, Save As
  and confirmed commit; four exact native oracle cases pass. After final
  hardening, the matching final Windows storage assembly again passes the
  standard-user workflow, and its two exact outputs pass 14 independent Linux
  checks. The earlier GUI package remains archived with its own identity.
- Windows version observed: 26200.9974, x64. Four self-contained packages and the
  Linux x64 AppImage are review candidates, not physical ARM64 verification or a
  published release.

[Evidence index](../artifacts/focused-ntfs-20261117/evidence-index.json) and
[package manifest](../artifacts/focused-ntfs-20261117/packages/manifest.json)
identify source, assemblies, raw successes/failures, archives and restore maps.

## Precise remaining technical obstacle

Ordinary offline edits must not invalidate file IDs, namespace/data allocation
or LSN references that a later TxF/KTM decision can undo or redo. Pending or
prepared transactions require refusal under the no-replay policy. Inactive
history may be preservable; complete transactional recovery is not required
merely to support that subset.

What is missing is a versioned, persistent **empty-active-checkpoint predicate**
covering both CLFS clients, consistent base/restart selection, prepared/in-doubt
states and TOPS/`$TXF_DATA` references. Root state/free-TOPS shortcuts are falsified;
counting every physical historical record as active is also wrong. Protected-path
privilege probes are already exhausted, and detached-copy native transport is
already solved. Repeating those probes would not establish this predicate.
A prepared/in-doubt persistent contrast and a defensible interpretation of the
current checkpoint/reference fields are the specific missing evidence/model.
This report does not claim that NTFS makes ordinary inactive-TxF editing
impossible. [Diagnostic entries](../artifacts/focused-ntfs-20261117/diagnostics.json)
record exact inputs, checks, hypotheses and distinguishing results.

Owner review must either retain those requirements as incomplete or explicitly
accept a narrower 1.0 native subset. No scope reduction is assumed here.

## Recovery, failed attempts and cleanup

The VS Code interruption did not reset the original 05:56–07:56EST UAC window.
The departure update tightened the conservative cutoff to 07:48EST; the final
approved cleanup finished before departure. No new privileged work followed.

The first matrix wrapper attached but returned zero associated disks; its
identity gate stopped before filesystem I/O. A native-container/header route
with bounded enumeration passes 60 cases. The precise contribution of the old
wrapper geometry/header versus enumeration timing was not isolated; no claim
of NTFS corruption or universal container incompatibility follows. Both attempts
are retained. Archive timeout/assembly-load and PowerShell array-check failures
are also retained, never counted as successful cleanup.

The owner's D: alert was five stale Explorer “device is not ready” dialogs after
generated-drive detachment. They were captured and dismissed; no D: test volume
or task app remained. Verified Windows cleanup removes 790 files/148 detached
images/eight native roots, then 560 profile staging files and 82 loose duplicates;
normal 86-byte preferences/85-byte layout persist, with zero workspace payloads
and recovery registrations. Two final cleanup receipts remain deliberately.
Linux expanded evidence and disposable build trees are losslessly archived,
hash-verified and compacted; exact measurements are in the evidence index.
The manually controlled tunnel is retained. Dropbox restart occurs only after
all final writes/checks/cleanup; its returned result is reported in chat.
