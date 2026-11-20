# SectorWorks agent entry point

## 0.6 development prerelease

The owner authorized the 0.6 development prerelease from the verified Phase 6
snapshot and asked to reuse the completed regression. See the
[prerelease notes](docs/prerelease-v0.6.md) for the downloads and verification
scope. Phase 6 qualification findings remain open as recorded below.
Phase 7 requires separate authorization.

## Phase 6 review — November 19–20, 2026

The owner authorized Phase 6 implementation and verification. The
[Phase 6 report](docs/phase6-additional-formats.md) and
[evidence index](artifacts/phase6-review/evidence-index.json) record bounded
reader coverage, conventional raw 360K/720K FAT12 writer qualification,
targeted compatibility dispositions and four local review packages.

Final Linux verification passed 2,552 tests with one Windows-only skip.
The first runner returned 143 before StorageTests completed. Its other
four suites passed; the same StorageTests build passed in three disjoint,
detached selections. Keep these receipts distinct. The Windows laptop
passed focused and desktop checks; its default unfiltered test tool gaps
are recorded separately from the exact, independently verified mixed
outputs. The verified standard-user app and test payloads were cleaned.
Preserve the manual tunnel and existing NTFS and ARM64 decisions.

Phase 6 stopped for owner review. The qualified PMFX reader opens and
extracts files directly. The vendor restore comparison remains open because
DiskGenius Free would not save the required PMFX. The owner offered a
licensed work-PC check; the review folder includes generated VHDs and
reproduction steps. Keep the legacy Ghost and specific unsupported or
damaged-media rows visible. Phase 7 and publication require authorization.

## Phase 5 — owner-authorized BIN/CUE and optical expansion

Phase 5 is verified and STOPPED for owner review. The owner authorized only
this phase, with the laptop available at standard-user
privilege and no UAC approval available. The [Phase 5 report](docs/phase5-bin-cue.md)
tracks the qualified parser, player and rebuild scope. Stop for review before
Phase 6. Keep phase deferrals and earlier media safety gates intact.

## Remaining phase order — owner decision

The [checklist](checklist.txt) now sequences **Phase 5 — BIN/CUE and Optical
Expansion**, **Phase 6 — Additional Formats and Filesystems**, then **Phase 7 —
Linux and ARM64 Hardening**. Broad platform testing follows the feature work
and covers the combined product; feature-specific verification remains in
Phases 5 and 6. Physical ARM64 stays deferred to P1.ARM64. No phase
implementation or release is authorized by the reorder.

The owner added Phase 6 checklist items 6.12 and 6.13 on November 19, 2026.
Item 6.12 targets read-only MDF/MDS, CloneCD CCD/IMG/SUB, ImageDisk IMD,
Nero NRG, Norton Ghost GHO, and standalone raw CD data tracks. Item 6.13
tracks the compatibility audit's rejected BIN/CUE, ISO and raw FAT cases.
Use the [audit roadmap](docs/support-matrix.md#phase-6-compatibility-audit-additions)
for the planned scope. Phase 6 implementation still requires owner authorization.

## Phase 4 scope — November 17, 2026

Phase 4 optical rebuild is verified and STOPPED for owner review. See the current
[Phase 4 report](docs/phase4-optical-rebuild.md) and [evidence index](artifacts/phase4-review/evidence-index.json).
Keep ISO/UDF staging and reconstruction separate from direct disk commit; optical
source replacement is unavailable. Preserve the bounded ISO/Joliet/Rock Ridge,
UDF 1.02 and bridge predicates, fixed qualified boot-member bytes/LBAs and all
source/namespace/metadata checks. No blanket bootability or exact-layout claim.
Do not use owner recovery media as fixtures. Windows verification is standard-user
and required no UAC. Physical ARM64 deferrals and accepted Phase 2/3 scope remain.
Stop at Phase 4 review; Phase 5 or later work and push/tag/release are not authorized.

## Accepted revised 1.0 scope and phase closeout (November 17, 2026)

The owner invoked [the scope-change/closeout assignment](docs/sectorworks_1_0_scope_change_and_phase_closeout_sol_prompt.txt).
It explicitly supersedes former 1.0 default-TxF/LFS2 and physical ARM64 gates,
and satisfies affected review requirements conditionally. The exit audit in
[the closeout report](docs/1.0-scope-closeout.md) found no independent open exit:
Phase 2 (its historical envelope), 2B, 2C, 2D and Phase 3 are **COMPLETE / OWNER
ACCEPTED UNDER THE REVISED 1.0 SCOPE**. This is not a whole-product 1.0 release.

Retain every qualified FAT/NTFS reader/writer, structural legacy/list row and
the verified native passive NTFS3.1 subset: 512-byte sectors, 4096-byte clusters/
index blocks, 1024-byte MFT records, exactly `0x1400` volume flags, paired clean
LFS1.1 pages and the exact empty repair/deleted-state/preservation predicate.
Default TxF-enabled/transaction-bearing images and other unqualified states
remain read-only. No gate, log or system metadata is removed or normalized.

The unchecked [checklist](checklist.txt) sibling backlog is **P1.NTFS.MODERN.1–.6**
(default TxF/CLFS/KTM checkpoint/active/prepared/in-doubt admission, TOPS/
`$TXF_DATA` obligations, unsupported repair/deleted states, coherent detached
LFS2/cache safety and dependent writer rows). It is DEFERRED POST-1.0, technically
unresolved, not passed/impossible. Preserve existing **P1.NTFS.1–.6** authentic
legacy qualification. Native detached-copy transport is solved; state-zero/
free-TOPS shortcuts are falsified; historical records are not automatically active.
Use retained diagnostics/archive/restore maps, not repeated access probes.

Physical Linux ARM64 / Windows ARM64: **NOT VERIFIED — DEFERRED POST-1.0 BY OWNER**,
tracked separately as **P1.ARM64.LINUX** / **P1.ARM64.WINDOWS**. Owner has hardware
and will test later; do not describe permanent hardware absence. Preserve code/
build/package support for exactly four Windows/Linux RIDs; macOS stays removed.
Cross-publishing is not physical testing. Neither deferral blocks 1.0 or phase
completion; unrelated future x64 hardening/security/UX/optical work stays required.

STOP after this scope/documentation closeout. Phase 4 is next available for
explicit owner authorization; no implementation, new probes/UAC/ARM64 tests,
external push, tag, release or publication is authorized. Earlier dated notes
below retain historical findings/authority only where not superseded here.
Dropbox lifecycle, cleanup, protected media, safety and manual tunnel rules remain.

## Focused native NTFS closeout for owner review (November17,2026)

The [focused assignment](docs/sectorworks_focused_ntfs_diagnosis_sol_prompt.txt)
is delivered and STOPPED for owner review. Read the [decision report](docs/focused-ntfs-diagnosis.md)
and [evidence index](artifacts/focused-ntfs-20261117/evidence-index.json) before
proposing further work. Do not automatically repeat probes or start Phase4.
No push, tag, release or owner acceptance is authorized by this closeout.

The bounded native TxF/repair-log-disabled NTFS3.1 subset is implemented and
verified: exact0x1400flags, clean pairedLFS1.1, empty repair/deleted state,
complete protected-record/allocation/security-referent preservation. Native
60/60volume oracles, published standard-user GUI results,15unique container/
layout rows and final standard-user Windows runtime checks pass. Full Linux
verification2405pass/oneWindows-onlyskip precedes a final stricter descriptor
assertion;18focused tests,32exact-output revalidations and final builds/format/
four publishes/AppImage pass afterward. The local native fixture is ignored by
Git and excluded from application packages; missing-input skips are NOT VERIFIED.

Default TxF-bearing admission and broader coherentLFS2 safety remain unproved;
neither phase is complete. Physical Linux/Windows ARM64 remain **NOT VERIFIED —
NONBLOCKING BY OWNER DECISION**. Older ARM64-blocking/blanket-repair exclusions
below are historical to the extent superseded by the focused report.

Original UAC window05:56–07:56EST was not reset by the crash. Departure notice
moved the conservative cutoff to07:48EST; final approved cleanup completed before
the owner left. No further elevation is available or inferred. All148checked
current generated disks are detached and eight native roots removed only after
790file hashes/archive/decoded-media checks. Task app/profile/payloads/recovery
registrations are gone; normal preferences/layout and the manual tunnel remain.
Two small final cleanup receipts remain on Windows. Large local evidence has
verified compact archives and restore maps; do not remove historical evidence.

Dropbox was stopped under the owner's explicit override of observed paused
sync. All final writes/checks/cleanup precede the required final `dropbox start`;
its returned outcome is recorded in chat. A later task must inspect actual
client state again rather than infer it from this pre-restart record.

PowerShell5 learning: wrapping a root-array `ConvertFrom-Json` pipeline in
`@(...)` can produce one nested item. Direct assignment returns the expected
array. Validate actual manifest counts/checkers before UAC; preserve failed
receipts and never equate a wrapper result with native/cleanup success.

## Resumed closeout for owner review (2026-11-16, November17 UTC)

The crash-interrupted assignment has completed its independently achievable
follow-up. Current R4b tests2387 pass/one skip; formatting succeeds with a fresh
private TMPDIR after the retained MSBuild temporary-directory failure, and all
remaining verification stages pass. Exact current Windows GUI Save As/commit
outputs pass56 independent NTFS commands. All337 prior final code-manifest
members remain unchanged. Corrected LFS R2 and NoTxF R2 results are interpreted;
modern Windows TxF/repair/LFS2 writing and physical ARM64 remain blocked.
Neither phase is complete. See the current review/ledger/resumed evidence index.

Resumed Windows GUI/native task roots and stale test registration are removed
only after hash/archive checks; user preferences/layout and historical evidence
remain. The manual tunnel remains; no application/test process or generated-disk
attachment remains. STOP for owner review before Phase4; do not automatically
resume probes or infer acceptance/publication authority from this closeout.

LFS learning: successful FSCTL_LOCK_VOLUME flushes cached data and NTFS treats
the volume as dismounted. In R2, both mounted pages are2.0, both locked pages1.1;
unlock/close/detach changes metadata again. Locked and detached captures are
separate evidence. A live sequential2.0 capture is not a coherent offline writer
oracle. Format-time NoTxF/NoRepairLogs controls still retain repair metadata;
do not infer its update semantics or remove writer gates.

Finite cleanup learning: the host's Restricted PowerShell policy can reject a
script-file launch before any script receipt exists. Preserve that failed launch;
execute the reviewed exact command body inline through normal UAC when authorized,
without changing execution policy. Missing output is not a cleanup success.

## Native detached-copy learning (2026-11-16)

The resumed assignment established a standard-user alternate CLFS evidence
route: copy complete sealed generated BLF/container streams into a fresh
user-local directory when both metadata copies have independently verified
`%BLF%\` relative container names. Native queries then read both `TxfLog` and
`KtmLog`; fresh Ordinary/Committed/RolledBack observations match original raw
payloads. Protected live-path access remains denied; no privilege/ACL change
was used. Normal Windows CLFS opening automatically added authentication
suffixes and `.cnpf` files to the disposable copies. Preserve and hash the
entire before/after files; never strip suffixes or use changed copies as input
evidence. This establishes transport/payload observations, not persistent
quiescence, prepared/in-doubt handling, TOPS/$TXF_DATA update obligations or
offline writer safety. See [live ledger](docs/phase2d-phase3-progress.md).

The interactive MCP account and restricted SSH transport account use different
profiles. Determine the actual process profile for private state/path guards;
do not assume the SSH profile is the interactive user's. Identities and access
inventory remain local-only.

## Resumed after Windows update/reboot (2026-11-16, evening)

The owner invoked [the resume assignment](docs/sectorworks_resume_phase_2d_3_after_windows_reboot_sol_prompt.txt).
Continuous Phase2D/3 work resumes from the delivered source and evidence; the
prior STOP below is historical. The owner attributes the interruption to a
Windows update/reboot and has restored the manually controlled tunnel; it was
not a deliberate withdrawal. UAC approval is available again. Prepare finite,
generated-only privileged actions, announce and trigger legitimate UAC, then
continue after approval. Preserve standard-user application qualification and
all existing native safety gates. No Phase4, security weakening or publication.

## Current autonomous Phase 2D / Phase 3 authority (2026-11-16)

**Current disposition: independently achievable delivery is prepared and STOPPED
for owner review. Neither phase is complete.** Retained modern Windows native
safety, physical ARM64 and corrected Windows desktop evidence remain open; see
[the final review](docs/phase2d-phase3-review.md). Do not automatically resume
native probes, implement Phase 4 or infer owner acceptance from this handoff.

The owner explicitly invoked [the autonomous assignment](docs/sectorworks_phase_2d_and_phase_3_autonomous_sol_prompt.txt).
It supersedes older intermediate review stops and authorizes the remaining
Phase 2D and Phase 3 implementation, diagnostics, verification and review
packages continuously. Independent Phase 3 work may proceed around a retained
2D blocker; unsafe writer/commit rows remain disabled and incomplete. Stop for
owner review after all achievable work in both phases, with no Phase 4 work,
release/tag or inferred external push authority.

For this assignment, generated-media native tests may be run autonomously,
including normal Windows elevation through legitimate available mechanisms
and a narrowly scoped local elevated test runner if necessary. Inspect actual
tokens; retain separate standard-user product qualification. Do not disable
UAC/security, weaken protected ACLs or authentication, persist/recreate the
owner's visible tunnel after withdrawal, touch physical disks or expand
protected owner-media permissions. Bundle only indispensable unavailable
credential/UAC/physical actions after concrete setup. Historical owner-launch
restrictions below describe their dated scope and are superseded for this
assignment. All Dropbox, cleanup, portable-storage and proof requirements remain.
Track progress and evidence in [the live ledger](docs/phase2d-phase3-progress.md).

## Owner working rules imported from WinVOX (2026-11-16)

The owner explicitly adopted the Dropbox lifecycle and routine task cleanup
rules from WinVOX. These apply before every task, including documentation-only
work. Minimal instruction/status discovery for the preflight is permitted.
Keep this repository's existing layout, protected media and evidence records;
this adoption does not move documentation or authorize editing WinVOX.

### Dropbox lifecycle: preflight, isolation, final restart

Dropbox has previously caused conflicting source/build artifacts. Where the Dropbox client is installed and usable, pause it for the task even if some outputs are outside the synchronized tree.

1. Inspect the client and synchronization state using the established local procedure. Confirm synchronization is settled BEFORE issuing stop. Do not interrupt an observed active transfer, force a misleading ready state, or change sync configuration to get past this check. If settling is delayed, use bounded observation with updates; report a concrete blocker rather than waiting silently forever.
2. Record the initial state, then stop Dropbox and verify that it stopped before ordinary project reads/copies/changes/builds/tests. This newer status-before-stop procedure supersedes the older instruction to make stop the first shell command.
3. Keep Dropbox stopped through implementation, imports/builds/tests, evidence extraction, history/report updates, packaging, integrity checks, checkpoints, and owned-process cleanup. Do not restart between tool calls or test phases.
4. Establish task-wide best-effort finalization/recovery before risky work. A shell trap protects only the shell it belongs to; a trap on one short command is not protection across an entire agent session. Avoid arrangements that restart synchronization while later commands still write. Preserve enough state to recover after interruption. Do not promise recovery from an external host/agent crash.
5. Finish ALL agent-initiated shell work and file writes, prepare final response facts and paths, finalize evidence, and complete cleanup before restart. Ensure no deferred trap/cleanup will perform more commands or writes afterward. The final shell command must be exactly:

   ```sh
   dropbox start
   ```

6. Do not run any further shell commands, status checks, listings, hashes, tool-initiated file edits, packaging, or cleanup after that command. Report the returned restart result in chat. If restart fails, disclose the failure and leave recovery to the user or a new explicitly authorized task; do not silently run additional repair commands.

If Dropbox is absent, inaccessible, or not applicable to the execution host, record the actual condition and use appropriate local isolation; do not invent status or install it merely to satisfy this rule. An already stopped installed client is a distinct observed state, not a synchronization sample. The normal owner expectation is to start the usable client at task end unless the user explicitly requests it remain stopped. If current instructions make safe handling impossible, explain the conflict and continue only work that does not depend on unsafe access.

### Learnings: routine task cleanup

As part of every authorized task, clean up task-created disposable caches, redundant staging/extracted build trees, temporary test worlds, and closed task-owned VM disks before handing off and restarting synchronization. Preserve owner data, required source/build dependencies, deliverables, prior releases, recovery checkpoints, and evidence from successful, failed, and abandoned attempts; losslessly archive bulky evidence or repeated snapshots where useful, retaining original-path/restore mappings and verifying hashes and exact decoded media content before removing expanded originals. Keep historical evidence traceable through those archive mappings, and never alter authoritative raw evidence merely to reduce its size or make validation pass. Keep cleanup proportionate to the task, record what was retained or removed and any measured reduction, and complete it before the final integrity check and Dropbox restart to reduce file count and synchronization work.

For SectorWorks, preserve immutable fixtures, required pinned SDK/dependency
assets, owner images, prior releases, source recovery checkpoints and all
qualification evidence. Clean only task-created disposable resources after
stopping their exact owned processes. Preserve failed and successful Windows
probes; archive bulky generated evidence only with verified hashes and restore
mappings. Record cleanup in the existing platform/evidence records. Never
remove the Tower image, supplied ISO/PMFX inputs, owner VM state or an earlier
task's artifacts as routine cleanup. Do not infer a broad repository cleanup
from this standing rule.

Before substantial edits, preserve a recoverable source checkpoint including
pre-existing dirty/untracked source; leave restricted owner media in place.
Track task-wide restart/recovery state outside synchronized storage. A trap in
one short-lived shell does not protect an entire tool session. Complete all
checks, history/evidence writes, checkpoints and owned-resource cleanup before
the final restart command; run no commands or file edits afterward.

## Current platform and Windows test access (owner decision, 2026-11-16)

Support, build and distribute only Windows/Linux on x64 and ARM64: exactly
`linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`. Earlier platform-target
policies are superseded. Keep authoritative specification/design copies equal.
Upstream transitive package identities and required license texts remain intact;
immutable historical evidence and prior releases are not rewritten or deleted.

The local Linux machine remains the primary development and default test host.
Run ordinary local builds, automated tests and Linux UI checks here.
Use the verified `windows_laptop` MCP tools as the default connection whenever
Windows testing is needed, including SectorWorks UI workflows. The owner
explicitly authorizes ordinary desktop interaction for requested testing;
perform preparation, execution, inspection, diagnosis and cleanup autonomously.
Read [Windows desktop testing](docs/windows-desktop-testing.md) before using it.
Start GUI work with `Snapshot`, use observed controls, verify results through
screenshots/UI state, and release desktop control when done or waiting on the
owner. Prefer background `PowerShell` for command-line work and avoid overwriting
shared clipboard contents or unrelated existing documents. Ask only when a
specific required action cannot be completed with the available authorization,
permissions, credentials or hardware. Reuse established permissions rather than
asking again for routine test steps.

This access policy does not mark any SectorWorks GUI/platform gate passed, grant
account elevation, authorize a privileged broker/service/schedule or allow real
disks, log repair/reset/replay, owner-media expansion, remaining Phase 3 features
or publication. Keep source-integrity, validation and phase boundaries intact.
Connection addresses, keys and bearer tokens stay local-only outside Git and
synchronized storage. SSH remains a fallback for transfer/transport failures;
its key retains `-T` and the original source-IP restriction while permitting
the owner-authorized MCP tunnel destination.

Before **every task**, read `rules.txt`, `checklist.txt`, the full current
technical specification, and the current UI specification and design references
when relevant. These are mandatory project instructions, not optional context.

- [Rules](rules.txt)
- [Authoritative checklist](checklist.txt)
- [Technical specification](docs/specifications/Cross_Platform_Disk_Image_Workspace_Tech_Spec_v0.3.txt)
- [UI specification](docs/specifications/SectorWorks_UI_Look_and_Feel_Spec_v0.2.txt)
- [Additional UI guide](docs/design/SectorWorks_UI_Design_and_Prototype_Guide_v0.2.docx)
- [Disk/optical mockup](docs/design/UI01.png)
- [Disk/filesystem mockup](docs/design/UI02.png)
- [Architecture and MVVM conventions](docs/architecture.md)
- [Local commands and SDK](README.md)
- [Windows 11 Home SSH setup and unattended access](docs/windows11-openssh-setup.txt)
- [Current build and physical-platform evidence](docs/platform-status.md)

The supplied root specifications remain authoritative; documentation copies must
stay byte-identical. Do not silently edit specifications or mockups to resolve
disagreements; report conflicts and obtain owner direction first.
Owner confirmed on 2026-10-13 that the checklist governs audio scope:
BIN/CUE support and functional CD-DA playback belong to Phase 5. Phase 1 reserves
the optical/audio structure and pane tabs without implementing those workflows.

Owner also approved keeping DiscUtils 0.16.13 for development on 2026-10-13.
Do not treat fork evaluation as a prerequisite to starting development.
Re-evaluate the choice in the release-quality checklist before production
release, retaining the documented NTFS formatting limitation and all per-phase
safety/capability verification requirements. Keep storage integration replaceable.

Authorization applies only to the requested phase. Run verification, update the
checklist truthfully, report limitations, and stop for user review. Do not
implement Phase 1 while carrying out foundation preparation.


Owner approved the editing-roadmap correction on 2026-10-15: Phase 2B is a
separate staged-editing/Save As expansion milestone before Phase 3. It covers
the required raw/VHD/VHDX, filesystem-only/MBR/GPT, FAT/ordinary-NTFS stacks and
FAT long names. Phase 3 must qualify direct commit per required stack.
ISO/UDF/bridge, BIN/CUE and conventional 360K/720K delivery is explicitly
assigned to Phase 4, Phase 5 and Phase 6 item 6.10 respectively. See the checklist and
[planned capability matrix](docs/support-matrix.md#required-editing-delivery-roadmap).
Owner separately authorized Phase 2B implementation on 2026-10-15, after the
1.0 scope correction. The subsequent Phase 2C authorization below does not
authorize Phase 3 or any later phase.
Keep historical Phase 2 completion limited to its verified envelope; required
unverified rows stay incomplete/blocked unless the owner explicitly approves
a scope decision with a named follow-up milestone.

Owner explicitly authorized the Phase 2C roadmap/specification change and
reader implementation on 2026-10-15, before Phase 3. Qualify NTFS 1.2 (NT4),
3.0 (Windows 2000) and 3.1 (XP+) read-only subsets, including bounded supported
attribute lists/extensions and legacy metadata. The bounded reader is implemented;
remaining 1.0 verification/review gates stay open, not phase complete.
Owner explicitly deferred authentic NT4/Windows 2000/XP-created-media
qualification and dependent reader/writer interoperability checks on 2026-10-15
to the checklist's post-1.0 P1.NTFS.1-P1.NTFS.6 milestone. These are not 1.0
blockers or passed tests; VM-authored media suffices, without vintage hardware.
Keep automated tests, safety bounds, Tower source-integrity tasks, general
platform testing and Phase 3 writer/commit safety in 1.0. Do not waive 2B.6 current Windows-
authored-media/security-reference/LFS 2.0 gates or infer Phase 3 authorization.
Stop for owner review. Checklist 2C.1-2C.7, 2C.V1-2C.V8 and 2C.E1-2C.E6 began
pending. Implemented bounded attribute-list/version reading has 49/49 current
focused passes (35 attribute-list, 14 version/layout/service).
Full verification passed 1001/one Windows-only skip, 1002 total;
four focused cases were added after the full run's build. That snapshot's inventory
was 1006 including the skip, not a claimed full 1005-pass run. Final repeat then-configured
self-contained publish/local Linux AppImage passes on latest source, as does the
bounded owner read-only probe. No versioned release or GitHub publication changed.
Cross-publishes are not physical compatibility; 2C.V6a is verified and parent
2C.V6 stays open.
Exact checked subitems and open
gates are tracked in the checklist/platform record. Synthetic version-field tests are not authentic legacy qualification;
no authentic NT4 fixture exists yet.
Newly readable attribute-list/legacy volumes must remain noneditable until
Phase 3 items 3.0.1-3.0.5 qualify staged editing, Save As and new-output NTFS
writing BEFORE direct commit under 3.7. Preserve ALL existing qualified Phase 2B
editing; actual Windows-authored-media/security-reference and LFS 2.0/cache-safety
write gates remain incomplete in 2B.6, not moved or scope-demoted.
The approximately 8.42 GB raw Tower NTFS 3.0 owner image stays outside the
repository: do not copy, modify or distribute it. Initial rejection concerns
the live directory attribute list at MFT 26 referencing indexing extension 8336;
the independent native index warning is now diagnosed as ntfs-3g's 65536-byte
readall limit versus the 344064-byte index, not corruption evidence.
Initial in-place enumeration succeeds, not full content/health qualification.
Reader acceptance or a tool-limit diagnosis never establishes write safety.
See [Phase 2C plan](docs/support-matrix.md#phase-2c-ntfs-reader-qualification-plan).
Keep the signed reader and strict writer validation defaults unchanged.
The separate read preflight resolves bounded ordinary lists/extensions; MFT
bootstrap lists remain explicitly unsupported. Follow the documented list/
extension/index budgets and bitmap-selected active-index traversal, while still
structurally validating inactive MFT records without resolving their references.

The current roadmap targets 1.0. VMDK/VDI/QCOW2/DMG/ext2/ext3/ext4/exFAT/HFS+/APFS
target qualified read-only subsets where feasible. Their writers, modified Save
As, commit/rebuild and creation/formatting are outside 1.0, not unassigned gaps;
no future writer milestone/date is required or promised. Phase 6 reader
evaluation/fixtures/tests remain required. Detection is not browsing/extraction;
document exact reader blockers for owner review, never silently drop them.
Apply read-only caps across containers and filesystems compositionally. Internal
parsers determine support, not the host OS's native mounting capabilities.

Owner separately authorized both bounded ISO reader refinements on 2026-10-15:
empty-file unused-extent compatibility and efficient ISO9660/Joliet directory
enumeration without raising budgets. The Toshiba recovery ISO remains local,
unmodified, untracked and undistributed, never a fixture or package input.
No optical writing/rebuild, Phase 3 or release-publication authority follows.
See [reader evidence](docs/platform-status.md#iso-reader-compatibility-refinement-2026-10-15).

Windows 11 Home x64 unattended SSH command access was verified on 2026-10-15.
This is infrastructure only, not Windows build/test, security-reference,
Windows-authored NTFS or GUI qualification. Keep those checklist gates open.
Keep private keys outside the repository/synchronized storage and connection
addresses/key inventory in local-only records, not committed documentation.
Use explicit-key, publickey-only BatchMode and strict host checking for finite
remote commands; the enrolled restricted key requires no PTY (`-T`).
Do not bypass a host-key mismatch or elevate the standard testing account.
The interactive desktop is now available through the verified MCP connection.
Historical SSH-only evidence does not establish GUI qualification. This setup does not
authorize Phase 3, real-disk operations or recurring automation.

## Phase 2D authority and retained closeout (2026-10-17)

Owner authorized Phase 2D documentation/implementation after a read-only preflight.
The prerequisite gate was resolved in this child chat before actual work.
Use checklist 2D.1-2D.9/2D.E1-2D.E4 to close retained 2B/2C requirements;
reassignment never marks the historical tasks passed. This supersedes stale
"Phase 3 unauthorized/unimplemented" current-state wording above: retain the
owner-authorized 2026-10-16 backend increment and its evidence, but do not
continue remaining Phase 3 work. No Phase 4/5/6/7, PMFX or publication authority.

Windows strict-key access is restored; keep the account standard and connection
inventory local-only outside synchronized storage. User-local pinned tools,
project transfer and disposable generated test media are authorized. Owner will
launch narrowly bounded finite privileged Windows-native virtual-disk steps;
never elevate the account/agent, accept arbitrary privileged commands, use real
disks or install a service/schedule. BCL codec comparison does not prove complete
inheritance or Windows filesystem interoperability. Qualify current Windows
ordinary NTFS media and clean LFS 2.0/cache safety without replay/reset/repair,
gate removal or weakened allocation/security/metadata checks.

Tower permission: in-place Linux full before/after SHA-256 and bounded workflows,
representative selected temporary extraction <=64 MiB in private local storage
outside repository/Dropbox, followed by cleanup. No image copy/mount/transfer/
modification/distribution. Other owner-media restrictions remain unchanged.
Authentic historical authoring remains P1.NTFS; MFT-bootstrap lists unsupported;
newly readable legacy/attribute-list volumes NONEDITABLE until Phase 3 3.0/3.7.
Preserve every existing qualified FAT/NTFS write. ARM64 is currently unavailable:
owner approved proceeding, not waiving 2D.8c; keep it explicitly blocked.
Stop at 2D verification/owner review with exact evidence and concrete blockers.

Current 2D evidence: Windows codec 271/271; reader 66/66 each on Linux/Windows x64;
bounded Tower full before/after hashes and 1235021-byte selected extraction/native
comparisons pass, private outputs removed. Full Linux verify 1136 passes/one
Windows-only skip; then-configured publishes/local AppImage pass. These close named
reader/Tower subitems, not full 2D or its remaining platform/review gates.
Windows native author/source-readonly reopen pass, but edited output is untested.
Native writable-attached fsutil reports LFS 2.0; detached raw/read-only report 1.1,
with exact transition not raw-captured. TxF/CLFS metadata independently blocks
ordinary writer qualification; do not strip or blindly preserve it as proof.
Modern-log/cache-safety investigation continues by owner direction with all
writer gates intact. See checklist/current platform evidence for exact chronology.
Owner subsequently selected bounded verified closeout/STOP for review after
the focused investigation found no sufficient TxF/CLFS offline quiescence/
update predicate. These requirements stay in 1.0, incomplete and unwaived.
Broader Windows execution: storage 400 pass/57 fail; Services 153 pass/3 fail.
Missing native tools, application log contention, direct-commit sharing and
preference error/cleanup-test mismatch remain explicit blockers, not platform
passes. No source-integrity assertions completed in failed Windows commit cases.
Do not continue implementation/remote probes or remaining Phase 3 automatically.

Owner then explicitly resumed the recommended corrective pass: repair retained
Windows commit-handle lifecycle and shared logging, verify preference failure/
cleanup semantics, and separate managed Windows writer execution from independent
Linux checks of the exact generated outputs. The fresh read-only preflight found
existing standard-user/tooling/generated-media permissions sufficient; no new
owner-media access or privileged Windows steps are needed. This supersedes the
immediate implementation stop only for those repairs/qualification. TxF/CLFS,
LFS 2.0 and unavailable ARM64 gates remain required and incomplete. Remaining
Phase 3 features and later phases stay on hold; no publication is authorized.

The final Global-session-corrected source passes Windows32focused,177Services,
392reader/security/safety,12guards,168writer and9growth selections, zero skips;
186exact outputs pass876independent Linux commands and84inputs stay unchanged.
This closes retained runtime/tooling defects, not Windows-authored TxF/CLFS,
LFS2/cache safety, GUI or ARM64 gates. Exact current source/media manifests and
compact verified archives: docs/platform-status.md and docs/windows-writer-handoff.md.
Final complete Linux verification passes1178/oneWindows-only skip (1179total),
zero buildwarnings/errors, format/resources/AppRun/fivecopies; allsixpublishes/
localAppImage pass. Correctivebatch is complete and STOPPED for ownerreview.
Do not infer permission
to continue remaining Phase3 or claim full Phase2D completion.

Owner subsequently authorized committing and pushing the verified changes on
2026-10-17, explicitly without a release. This permits source commit/push only,
not tags/releases, further phase implementation or waiver of retained blockers.
Keep owner media, generated payloads and private access records out of Git.

Owner resumed retained Windows-native TxF/CLFS work on 2026-10-21 under
checklist 2D.11 and authorized the proposed decoding, safety-rule investigation
and conditional writer implementation/verification continuously, pausing only
for needed decisions/evidence/owner-only actions. Writer expansion still
requires a defensible persistent recovery/update rule; no Phase 3/later-phase,
repair/reset/replay, privileged SSH or publication authority follows.

## Learnings

Resolve default application-data paths with
`Environment.GetFolderPath(LocalApplicationData, DoNotVerify)` and require an
absolute result. An absent first-run XDG directory made the older lookup return
empty and crashed startup; never fall back to the working directory. Recovery,
staging, logging, preferences and layout share the guarded resolver. Keep the
isolated child-process fresh-profile regression and actual AppImage startup/
Save As/commit/restart checks. Captured payloads finish immutable0400 inside
private0700 session directories; the shared spool parent is not the payload
security boundary.

A child started with Start-Process inside a finite SSH command can end when the
SSH session closes. Keep necessary test wrappers in the finite foreground SSH
session; preserve partial attempts and check for existing output before retry.
A denied CIM metadata query is not failed SSH transport or evidence that account
elevation is required. Never recreate the owner's deliberately visible desktop
tunnel when it has stopped listening. Cast Get-Content pipeline strings to plain
strings before ConvertTo-Json; provider-attached properties can otherwise expand
into megabytes of unrelated metadata. Preserve raw failed observations locally
and derive small summaries instead of rewriting them.


Use the actual native metadata filename `$TxfLog.blf`, preserving the leading
`$`; the older research spelling without it caused all R4 CLFS opens to fail.
The Microsoft SDK defines `CLFS_RECORD_TYPE` as UCHAR and `ClfsClientRecord`
as 3, unlike an older third-party enum: use byte-by-reference and validate
known-byte native reads before owner-only probes. Native control authoring
success and successful CLFS queries are separate outcomes; the batch must
report missing queries as failures, retaining the reports and unchanged account.

R5 completed all three native controls, then failed the final coverage assertion
at batch line 26 because all 16 protected CLFS opens returned access denied,
even in the owner-local administrator run. This is not a third-control failure
or evidence that the standard SSH account needs elevation; retain the detached
outputs/reports and do not rerun or alter permissions without a justified new method.

R6 successfully opens all metadata attributes, but GENERIC_READ fails for six
metadata targets and both CLFS clients before record decoding; attribute access
is not data-read permission. The owner administrator token has backup/restore
privileges present but disabled, while the standard token lacks them; this is
not proof of the denial's cause. Owner-launched R7 failed its dictionary-result
checker; cleanup detached the image and source/seed hashes match, but no usable
activation/restoration result was captured. Owner R8 subsequently completed:
existing backup privilege was enabled/restored in the isolated finite child,
with original queried token state verified; restore privilege stayed disabled.
All 18 before/18 with-backup rows match, including six metadata data-read and
both CLFS opens denied with error 5, so no protected restart/record oracle ran.
Exact output/seed hashes and seven independent Linux reads pass; this closes
the bounded backup-candidate evaluation only. No further owner probe is
justified yet; keep both-client recovery/update and all writer gates open,
and never infer another privilege/account/ACL change from these denials.

Native C# Dictionary<string,object> entries are not PSObject.Properties:
test IDictionary keys separately from JSON-deserialized object properties.
Regress actual native rows and JSON rows, including missing native errors and
cleanup failures, before another owner-only probe. Persist stage observations
before assertions and retain partial results on failure; a parent wrapper line
alone cannot certify the native cause or whether privilege activation/restoration ran.

CLFS raw base field 64 is not the native `LastFlushedLsn` in the verified
generic control; keep raw fields unlabeled until independently established.
New generic logs can include authentication-related extra bytes beyond native
logical sizes: bound/refuse unknown full-file suffixes, retain complete source
hashes and label any diagnostic prefix extraction explicitly. Never strip,
regenerate or bypass authentication to qualify writing, and do not treat a
generic API self-test as TxF/KTM recovery proof. See
[native and suffix evidence](docs/dependency-evaluation.md#native-r4-results-and-corrected-oracle).

Zero-length ISO files can have unused out-of-range extent addresses: tolerate
only the empty-file case, keep other structural checks, warn and return an
empty stream without seeking. Never whitelist marker filenames or relax
block-device bounds; the pinned ISO extent stream seeks even for an EOF read.
The pinned VFS reconstructs parent directories for each path-based entry
property, so large nested ISO/Joliet listings need bounded batch metadata
decoding rather than increased parser budgets. Preserve the separate
Rock Ridge/SUSP path and regress metadata, names, content, caps and cancellation.

DiscUtils FAT writer success and reopened file hashes are insufficient write
validation: Phase 2 exposed a truncation-related lost cluster and incomplete
FAT32 auxiliary metadata despite correct file contents.
Use the qualified writer's allocation/metadata checks and independent
`fsck.fat -n`/mtools checks where available; do not replace these with tree/hash
checks alone.
Keep write fixtures separate from immutable read goldens, whose readable layouts
may intentionally fail stricter writer qualification.
See [current editing restrictions and evidence](docs/staged-editing.md) before
broadening FAT capabilities, especially the NFC BMP/ASCII-alias subset and
legacy floppy exclusions.

FAT lowercase short names use NT-reserved display flags, not necessarily LFN
records. DiscUtils resets ancestor-directory case flags during timestamp updates;
restore the changed entry and its ancestors after disposing the writer adapter.
Keep FAT lookup case-insensitive but output spelling validation ordinal/exact.
Phase 2B qualifies LFN/mixed-case/NFC BMP names separately; do not normalize
unsupported names or change untouched aliases. Partitioned FAT qualification
must compare the original BPB hidden-sector count against the absolute partition
LBA, including its FAT32 backup, without rewriting it to qualify the volume.

Existing-volume NTFS creation fails independently of formatting: DiscUtils ACL
inheritance calls unsupported RawSecurityDescriptor APIs on Linux and can leave
an indexed Security-ID-0 file before throwing. `ntfsfix -n` may still pass;
retain bitmap/index/security checks. Owner reaffirmed NTFS writing as required
for 1.0; the bounded investigation found binary SID/ACL/ACE/descriptor APIs fail
too and installed NTFS directory creation uses file inheritance. The owner
subsequently authorized the managed repair's implementation and qualification,
superseding that investigation's stop point, not the Phase 2B/Phase 3 boundary.
Keep the isolated source-repair assembly distinct from unchanged signed packages;
only independently qualified ordinary-volume subsets may enable writing.
Do not demote required NTFS writing to read-only or treat narrower probes as
complete delivery.
See [dependency evidence](docs/dependency-evaluation.md).

Existing `$SDS` descriptor hashes cover the original stored bytes; canonicalize
new insertions only, not the input before checking an existing record's hash.
Self-relative component order/alignment can differ while the descriptor remains
valid, so a canonical reserialization is not necessarily its original byte span.

Imported NTFS code must retain checked address/count arithmetic. Intentional
bitmap low-byte complements and signed run-delta reinterpretation need narrowly
scoped unchecked conversions; run lengths remain unsigned/bounded and header
zero is a one-byte terminator. MFT mirror extent is
`max(4 * recordSize, clusterSize)`, not always four records; regress large-cluster
fixtures and native backward/full-volume runs before broadening the writer.

Pre-XP NTFS FILE records with USA at 42/attributes at 48 use bytes 44..47 as
fixup slots, not NTFS 3.1 MFT identity; check header offset 44 only with the
extended layout (USA offset >=48).
Resident `INDEX_ROOT` compression flags can mean directory defaults, and empty
nonresident metadata may have no runs/highest VCN 0 or maximum only when
allocation/data/initialized lengths are all zero; never widen writer defaults.
The pinned `ntfsinfo` 2022.10.3 readall 64 KiB guard can mislabel a large
`INDEX_ALLOCATION` corrupt; compare bounded index graphs and independent native
directory/stream reads rather than assume corruption.
See [reader bounds](docs/storage-phase1.md#resource-cancellation-and-concurrency-policy)
and [pinned evidence](docs/platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15).

DiscUtils VHDX ContentStream.Flush is unimplemented. The qualified writer uses
synchronous payload/BAT writes, disposes the container, flushes the underlying
file to disk, then independently reopens and checks metadata and virtual bytes.
Preserve that ordering; a successful tree read is not equivalent to a durable
container flush.

Direct commit must suspend the original reader before source-write acquisition
and validate against a reopened, hash-checked backup, not disposed reader state.
Linux uses `F_OFD_SETLK`: ordinary process-owned `FileStream.Lock` loses
protection when any reader of that inode closes, so retain the unrelated-reader/
independent-writer regression. Windows uses deny-write sharing plus one
`FileRenameInfoEx` replace/POSIX operation; never unlock early, widen ordinary
reader sharing or substitute `File.Replace` with its partial-failure/merge
semantics. The native filename buffer must preserve exact UTF-16 code units and
include a trailing NUL excluded from its byte-length field.

Dynamic containers can grow physically after an ordinary qualified edit:
source/backup hashes use input length, committed-output hashes use actual output
length, and successful commit must refresh the session baseline/reset staging.
An earlier journal stage does not prove finalize never happened; recovery must
refuse a source that differs from its backup instead of rolling back a possibly
completed atomic replacement. Keep exact-input Windows execution and independent
Linux validation of the same output hashes; Linux authoring/check tools are not
a product Windows runtime dependency or permission to skip coverage.
See [commit architecture](docs/architecture.md) and
[portable verification](docs/windows-writer-handoff.md).

Named mutexes need the `Global\` namespace on Linux as well as Windows:
unprefixed .NET mutexes do not exclude writers in a different Linux session.
Establish the test child's session before CLR startup (`setsid --wait`), not
from managed `Main`, because the runtime can retain its original mutex session
namespace. Keep the actual log-gate before/after regression and independent-
session append/rotation coverage, not just same-session worker processes.
