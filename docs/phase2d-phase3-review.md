# Phase 2D and Phase 3 review — resumed November 16, 2026

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

## Historical implementation and evidence snapshots

Earlier present-tense authority/completion statements below are dated records,
not operative gates after the November 17 scope/acceptance decision.



## Current focused closeout — November17

**Ready for owner review; neither phase is complete.** The
[focused decision report](focused-ntfs-diagnosis.md) and
[evidence index](../artifacts/focused-ntfs-20261117/evidence-index.json) supersede
the earlier current-state paragraphs below. Native passive-metadata writing is
implemented/verified; default TxF and broader coherentLFS2 requirements remain
open. ARM64 is NOT VERIFIED — NONBLOCKING BY OWNER DECISION.

Full Linux verification2405pass/oneWindows-onlyskip and all verification stages
pass with privateTMPDIR. Final shared-descriptor preservation hardening then
passes18focused tests,32exact-output revalidations, current standard-user Windows
five-operation runtime/14independent commands, allfour publishes and AppImage.
Initial-source GUI outputs pass four native oracles; the full volume matrix
passes60/60. See the report for exact staged source/package identities and the
native-wrapper/container distinction.15unique container/layout rows qualify
the new subset; supplementary input-retention repeats do not inflate that count.

All current native/GUI work and cleanup are finished, the manual tunnel remains,
and no new privileged work followed owner departure.790native files/148detached
images/eight roots,560profile staging files and82loose Windows duplicates are
removed only after archive/hash verification. Normal preferences/layout remain.
Linux expanded payloads/caches and redundant package trees are compacted with
verified restore mappings. No Phase4 or publication is started.

The following earlier closeout snapshots retain their dated scope; statements
about blanket repair exclusion, unavailable GUI, unrun native controls or ARM64
blocking do not override this focused report.


Current focused follow-up: see the [November17 decision checkpoint](focused-ntfs-diagnosis.md).
Bounded native passive-metadata writing is now implemented and exact production
outputs pass native oracles; broad verification/closeout is in progress.
Historical blanket repair-metadata and ARM64-blocking statements below are
superseded only to that documented extent. Default TxF/LFS2 gates remain open.


## Owner decision — November17, 2026: focused NTFS diagnosis

The owner invokes [the focused assignment](sectorworks_focused_ntfs_diagnosis_sol_prompt.txt).
Physical Linux ARM64 and Windows ARM64 are **NOT VERIFIED — NONBLOCKING BY OWNER
DECISION**. This supersedes earlier physical-ARM64 phase/release blocking policy;
no physical test is marked passed and four-target support/verification work remains.
Affected parent gates include2C.V6/E5,2D.8c/E4,3.7 and Phase2D/3 review/exit criteria.
Required native Windows NTFS safety is not waived. Continuous2D/3 implementation
is resumed; no Phase4, external push, tag or release. The UAC window begins about
05:56EST and conservatively ends07:56EST onNovember17 (10:56–12:56UTC).


**Ready for owner review; neither phase is complete.** Interrupted validation,
verification continuation, native-result interpretation and resumed cleanup are
finished. Required modern Windows NTFS safety and physical ARM64 gates remain
incomplete and unwaived. No Phase 4, release/tag or external push occurred.
Context came from project records, without loading the previous conversation.

The [resumed evidence index](../artifacts/resume-20261116T231137Z/evidence-index.json)
and [live ledger](phase2d-phase3-progress.md) identify exact current evidence.
The prior handoff below retains its dated source/evidence scope; its statements
about unavailable UAC/desktop, unrun R2 and pending R4 validation are superseded
by this section, not silently relabeled as earlier passes.

## Resumed completion and remaining gates

| Requirement | Current result |
| --- | --- |
| Current R4b Linux verification | **2387 passed, one Windows-only skip, 2388 total**, zero test failures. The original command failed afterward creating an MSBuild temporary directory. Formatting passes with a fresh private `TMPDIR`; the unreached UI-resource stage and shell/AppRun/five-copy checks also pass. This is completed staged verification, not a claimed second full `verify.sh` run. Original failure retained; its root cause remains unestablished. |
| Exact R4b Windows GUI outputs | Save As and committed images pass **56 independent Linux NTFS commands**. Five original files retain content, identities and native file metadata; the sixth matches the replacement payload. Primary/backup boot sectors and qualified log are preserved. Every inspected input/output stays unchanged during validation. |
| Final Windows published UI | Retained current-package screenshots/receipts establish native picker, navigation, preview/hex, staging/history, Save As, confirmed commit/history refresh, real restarted cleanup/stale-stage refusal, native/optical read-only behavior and preferences after restart. Exact output validation now closes the interrupted gap. No completed workflow was needlessly repeated. |
| Windows initialization | Ten explicit private-store diagnostics pass, separate-process reload succeeds, and normal-user application-data resolution is absolute. These explicit-path/GUI observations do not claim a second Windows account or a whole new OS user profile. Failed diagnostic exception-filter and null-ExitCode wrapper attempts remain evidence. |
| Elevated LFS R2 | Completed after legitimate UAC: child exit0, both captures **133103616 bytes**, seed unchanged, detached. Interpretation now accounts for every changed sector. Mounted pages are2.0; locked/detached pages1.1. Corrected capture is complete; LFS2 writing remains unqualified. |
| NoTxF controls | R1 safely failed full format on a thin-provisioned VHD. Fresh quick-format R2 succeeded and detached; native read-only CHKDSK reports no problems. Format-time TxF disable removes observed transaction logs/data, but repair metadata remains outside the qualified writer subset. No writer gate changed. |
| 2D.3–.5 / 2D.11d,h / 2B.6 | **Blocked.** Persistent both-client/prepared/in-doubt selection, TOPS/`$TXF_DATA`, repair/deleted metadata update obligations and independent LFS2/cache rules lack a defensible portable contract. No native-source edit/Save As/commit pass is claimed. Required Windows writing stays in1.0. |
| Physical ARM64 | **Unavailable.** Established access inventory provides the Windows x64 target only. No physical ARM64 endpoint is available; no network discovery/emulation substitutes for it. |

The337-member prior final code manifest still matches current source. This
continuation changed documentation/evidence only; bounded441-row writing,
commit/recovery, existing FAT/NTFS qualification and all current runnable
application payloads remain intact. Authentic historical authoring remains
P1.NTFS, separate from required current-Windows safety.

## Native interpretation and investigated alternatives

R2 uses one positively identified generated-volume handle for complete reads
before and after successful `FSCTL_LOCK_VOLUME`. Microsoft documents
[the lock](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_lock_volume)
as flushing cached data and treating NTFS as dismounted. No separate
`FSCTL_DISMOUNT_VOLUME` call ran. The transition is located in the lock/flush
interval, not at a finer proven internal instant. The mounted LFS2 capture is
a sequential live read; the exclusive after-lock observation already has1.1
pages. There is still no coherent unchanged-log LFS2 writer oracle.

Mounted-to-locked changes cover183 sectors:122 CLFS base, one CLFS container,
52 NTFS log and eight MFT. Locked-to-detached changes cover189 sectors, including
root index, MFT, NTFS log and CLFS data. Every changed sector maps to an observed
allocated stream. Five ordinary user data streams, volume bitmap and MFT mirror
remain unchanged. Payload agreement is not metadata quiescence; locked and
detached files remain separately sealed evidence.

NoTxF R2 used documented format-time `/TXF:disable`, `/NoRepairLogs`, small-record
and short-name switches only on a new generated volume. Its inventory has no
observed `$Txf`, `$TxfLog`, `$Tops` or `$TXF_DATA`, but retains `$RmMetadata`,
`$Repair:$Config` and `$Deleted`. Microsoft's
[format documentation](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/format)
does not define their offline preservation/update obligations. The product
therefore still refuses this control for writing; no metadata was stripped.

The detached relative-container route already solves CLFS transport, with16
matching known-state payload observations. Repeating protected-path R8 privilege
probes is unjustified after verified activation/restoration gave the same
denials. Native backup/control APIs describe runtime or opaque metadata, rather
than a portable persistent format contract.
[Snapshot transaction status](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_txfs_transaction_active)
is defined for read-only snapshot volumes, not arbitrary detached raw bytes, and
does not specify ordinary offline metadata updates. No recovery/remount/reset or
authentication bypass was used to turn these routes into writing proof.
No further UAC capture was needed merely to repeat completed observations.
Implementing acceptance now would require an unsupported safety assumption;
this is a qualification blocker, not proof that a safe bounded verifier is
impossible. Detailed results are in the resumed native interpretation report.

## Current packages and cleanup

The existing four R4 runnable archives were reverified: **1130 decoded members**
match their manifests. Product bytes are unchanged, so rebuilding identical
payloads was unnecessary. Use [Linux x64](../artifacts/review-20261116/SectorWorks-review-linux-x64.tar.gz),
[Linux ARM64](../artifacts/review-20261116/SectorWorks-review-linux-arm64.tar.gz),
[Windows x64](../artifacts/review-20261116/SectorWorks-review-win-x64.zip),
[Windows ARM64](../artifacts/review-20261116/SectorWorks-review-win-arm64.zip),
[Linux x64 AppImage](../artifacts/publish/SectorWorks-linux-x64.AppImage) and
[generated fixtures](../artifacts/review-20261116/SectorWorks-review-generated-fixtures.zip).
The [resumed review manifest](../artifacts/review-20261116-resumed/review-manifest.json)
connects these seals to the updated source export/reports without replacing
immutable preceding manifests. Cross-publishes remain distinct from physical ARM64.

- Windows GUI cleanup removed142 sealed files /217869070 logical bytes, including
  only the intentional stale test registration. The changed source hash stayed
  unchanged during registration removal. Normal preferences/layout remain as171
  bytes of user settings; no normal-profile reset occurred.
- Finite legitimate UAC cleanup removed53 sealed files /289431024 logical bytes
  from six exact resumed native roots; seed unchanged. First launch was blocked
  by script-file execution policy before entry. The retained correction executed
  the same sealed body inline; machine policy remains Restricted, with no ACL,
  account or security change.
- Linux removed227 archived expanded files /1331364517 logical bytes
  (939470848 allocated), plus45 disposable probe/cache files /1879640 bytes.
  A2875320-byte supplementary archive retains derived media/metadata/probe
  source; every decoded member and restore mapping is checked.
- All198 original resumed evidence members were reverified before cleanup.
  Required dependencies, runnable packages, source checkpoints, prior evidence,
  immutable fixtures and owner media remain. Older native roots remain historical
  evidence; no broad cleanup or new owner-image access occurred.
- Final Windows check:15 remaining historical generated disks, zero attached,
  zero query errors, no test/application processes. Desktop control is released;
  the manually controlled tunnel remains. Linux task processes/mounts are clear.

Dropbox was settled before stop and remains stopped through all final work.
Its final restart result is reported in chat, with no subsequent commands/writes.
Review the generated-fixture walkthrough at the end of this report, the exact
GUI/native evidence and the retained safety/physical blockers. **Stop for owner
review; neither incomplete phase is accepted and Phase4 remains unauthorized.**

## Retained prior handoff — historical evidence and implementation detail


**Partial completion; stopped for owner review. Neither Phase 2D nor Phase 3
is complete.** The independently achievable bounded writer, commit UI, startup
recovery, available-platform verification and runnable packages are delivered.
Required modern Windows transaction/log safety and physical ARM64 evidence remain
blocked. Final corrected Windows desktop revalidation is also unavailable after
the visible connection closed. No safety gate was waived. The
[live ledger](phase2d-phase3-progress.md) retains failed attempts and chronology;
the [evidence index](../artifacts/autonomous-20261116/evidence-index.json) connects
source seals, results, packages, compact archives and restore mappings.

## Status and retained requirements

| Requirement | Current result |
| --- | --- |
| Windows TxF/CLFS native access | Both clients can be queried on complete detached generated copies without elevation. Six retained known-state clients produced 16 restart/forward payload observations matching the original raw streams. |
| TxF/KTM persistent recovery/update rule | **Blocked.** Transport and byte agreement do not establish transaction interpretation, prepared/in-doubt recovery, TOPS or `$TXF_DATA` preservation/update obligations. Transaction-bearing media stays noneditable. |
| LFS 2.0/cache safety | **Blocked.** Both actual mounted restart pages are 2.0 and detached pages are 1.1. The mounted capture is short by 4096 bytes and is not a coherent snapshot. The corrected bounded harness compiles and refuses standard-user entry before copying; its elevated capture has not run. |
| Current Windows-authored ordinary NTFS edited-output qualification | **Blocked by the preceding independent safety rules.** Generated portable NTFS writing on Windows is separate evidence. |
| New bounded legacy/list writer | Linux 441-row staged/Save As and commit selections pass. Both Windows 441-row selections and all 21 mixed-volume sets pass independent Linux verification. The bounded public expansion is enabled; final verification results are separated by source below. |
| Commit UI and startup recovery | Implemented. Actual published Linux/Windows startup recovery passes matching-source cleanup and stale-stage refusal with unchanged hashes. Published Linux legacy/list staging/Save As/commit and independent native checks pass. Published Windows R3 Save As and committed bytes both pass independent native checks. Corrected R4 Linux AppImage first-run and restarted recovery pass; R4 Windows desktop recheck is unavailable. |
| Physical ARM64 | Unavailable through established access; not waived. Four-target cross-publishing is not physical qualification. |
| Phase completion | **Neither phase is being declared complete.** Required Windows-native gates are retained. Independent Phase 3 delivery is retained for review; no later phase starts. |

No UAC prompt will be requested while the owner is unavailable today. No account,
ACL, UAC policy, privileged service/schedule, persistent tunnel or real disk was
changed. No Phase 4 or later work, release, tag or external push is authorized.

## Requirement-by-requirement closure ledger

| Checklist group | Evidence or remaining action |
| --- | --- |
| 2D.1 / 2D.2 | Authority/source baseline retained; portable security reference regressed in final Windows626 selection. Codec agreement is not full Windows inheritance/interoperability. |
| 2D.3 / 2D.5 | Native author/source-read-only evidence retained. Required edited native-source rows remain blocked by TxF and independent LFS/cache rules. Final R3 portable FAT/ordinary NTFS regression passes: 168 writer and 9 two-commit growth cases, with 186 exact outputs independently verified. |
| 2D.4 | Actual attached/detached log distinction observed; short capture retained as failed; corrected elevated capture remains unavailable today. No safe writer predicate established. |
| 2D.6 / 2D.7 | Existing bounded reader and approved Tower source/hash/selected-content evidence retained. No repeated owner-image access or broader health/write claim. |
| 2D.8a / 2D.8b | R3 full Linux verification passes 2386/one skip. Final R4 affected suites pass as detailed below. Windows R3 published GUI passes; R4 desktop recheck remains unavailable. Two R3 UI Linux-tool authoring failures are retained. |
| 2D.8c | Physical Linux ARM64 unavailable through established access; exact requirement remains blocked. Windows ARM64 physical validation is also unavailable. |
| 2D.8d / 2D.9 | Final four publishes/AppImage, decoded package/evidence integrity and owned-resource cleanup pass; inaccessible desktop-profile test remnants are explicitly retained. Owner acceptance remains required. |
| 2D.10 | Delivered Windows handle/log/preference repairs retained; current Services210 passes. Runtime tooling fixes do not qualify native transaction/log rows. |
| 2D.11 | Detached both-client transport/payload comparison achieved without elevation; persistent TxF/KTM/TOPS/TXF_DATA rule remains blocked. |
| 3.0.1–3.0.5 | Structural441-row writer subset and its separate staged/new-output evidence pass; authentic legacy authoring remains under the existing P1.NTFS deferral. |
| 3.1 / 3.2 | Source identity and held platform locks retained; final boundary/Services/storage tests pass. No protection is claimed against arbitrary external path replacement or writers ignoring advisory locks. |
| 3.3 / 3.4 | Backup/journal/catalog and real startup UI implemented; bounded intent audit added. Published Linux/Windows R3 and corrected Linux R4 AppImage startup cleanup/refusal pass after actual owned process kills. |
| 3.5 / 3.6 | Validation/atomic finalize/history refresh integrated. Four fresh actual process interruptions across Linux/Windows are archived before UI; published cleanup/refusal passes, including the corrected Linux AppImage. No power-loss guarantee. |
| 3.7 | Linux441 and Windows441 exact commit outputs pass after own Save As prerequisites; mixed21 preservation sets pass. The final previously qualified 84-input Windows regression/growth passes, with 876 independent checks of 186 exact outputs and all original input hashes unchanged. Modern native-source rows remain blocked. |
| Phase3 exit / owner review | Full phase completion is not claimed while required native Windows/platform gates remain open. Final runnable packages and the acceptance walkthrough below are ready; final Windows desktop recheck and owner acceptance remain open. |

## Native investigation routes and limits

Retained protected-path R5/R6 failures distinguish attribute access from data
access; R8 positively enabled and restored the finite child's backup privilege
and still received access denied. That route was not repeated or treated as a
reason to elevate the standard account. The alternate complete detached-copy
route does read both clients: six ordinary/committed/rolled-back controls yield
16 native restart/forward payload observations matching raw source streams.
Native opening changes only the disposable copies, adding authentication suffixes
and `.cnpf` files; complete before/after files and hashes are retained, never
stripped or propagated into the product. Generic CLFS transport and known-state
payload agreement still do not decode persistent TxF/KTM semantics.

A separate bounded attached/detached LFS experiment captured actual 2.0 restart
pages versus detached 1.1, but its raw extent is 4096 bytes short. The failed
attempt detached safely and retained source/seed hashes. Corrected R2 uses one
handle, positively checks the single virtual extent/length, requests extended
DASD I/O and bounds lock/read/unlock. It compiles and safely refuses standard-user
entry before copying; no elevated R2 result exists. No native replay/reset/repair,
metadata deletion, authentication bypass or speculative offline writer acceptance
was used. The prepared/in-doubt and persistent update obligations remain a
research blocker beyond merely obtaining that corrected capture.

## Exact expanded writer coverage

The feature set has **21** members: NTFS **1.2, 3.0 and 3.1**, each with ordinary
metadata, or resident/nonresident attribute lists containing moved file data,
continued file-data extents, or a moved root-directory index. These are structural
synthetic fixtures, not authentic NT4/Windows 2000/XP-authored media. The existing
post-1.0 historical-authoring deferral remains unchanged.

Each feature is composed with **21** disk rows:

| Container | Filesystem-only / primary MBR / GPT | Logical sector | Physical sector rows |
| --- | --- | --- | --- |
| Raw | Each independently | 512 | 512 |
| Fixed VHD | Each independently | 512 | 512 |
| Dynamic VHD | Each independently | 512 | 512 |
| Fixed VHDX | Each independently | 512 | 512 and 4096 |
| Dynamic VHDX | Each independently | 512 | 512 and 4096 |

This produces **441** workflows per operation selection: **420 new rows** and
**21 existing ordinary NTFS 3.1 rows**. New legacy/list layouts require 4096-byte
clusters/index blocks and 1024-byte MFT records. The <=64 GiB image and existing
reader, list, index, metadata, path and cancellation bounds remain in force.
MFT-bootstrap attribute lists, compression/sparse/encrypted/reparse features,
unknown metadata/streams, active quota accounting, dirty/recovery-needed logs,
LFS 2.0 and transaction-bearing metadata remain excluded.

The NTFS 1.2 writer fixture has an empty `$Quota`, no `$Extend`, 48-byte standard
information, per-file security and legacy FILE headers; it is separate from the
intentionally unqualified reader fixture. Allocated extensions require a unique
base owner, matching references/sequence, zero extension link counts and fully
accounted extents/bitmaps. Existing qualified FAT/ordinary NTFS rows remain
required regressions.

Every expanded matrix case exercises all five operations, directory rename and
recursive path behavior, shared browse/preview/hex/search/extraction, Pending
Changes, undo/redo/discard, validated Save As and unchanged-source hashes.
Commit cases perform Save As first, then two source commits with history/baseline
refresh and independent final-byte checks. Separate tests cover actual MFT/index
growth, near-full allocation, malformed ownership, invalid timestamps/names,
source sharing/races, low space, cancellation and write/flush/validation failures.
The 21 mixed-volume cases edit a qualified primary while refusing and preserving
a readable dirty NTFS sibling and every byte outside the primary volume.

## Repairs and recovery model

The isolated managed backend now retains legacy SI/header layouts and allocated
MFT identities. It fixes SI offsets, USN/quota serialization and the synthesized
directory flag. Product output validation compares original SI and stored
security bytes, aliases, timestamps, allocation, mirror, directory/index/list
ownership, boot records and the unchanged qualified log; tree/content hashes
alone are insufficient. Signed upstream reader packages remain unchanged.

Commit is exposed through the Image menu, toolbar and Ctrl+S; Save As uses
Ctrl+Shift+S. Confirmation explains backup, temporary validation, replacement,
cancellation and retained artifacts. Modify/Save As qualification does not grant
commit automatically: the writer contract defaults commit to denied and exposes
a separate per-volume decision. Source host read-only/link checks apply as well.

Recovery scans at most 256 registrations in the private application-data
`SectorWorks/CommitRecovery` catalog, not arbitrary disks. Journals are bounded
at 32 KiB and require canonical source/sibling identities, stages and baseline
hashes. Writes use an exclusively created, flushed temporary followed by rename;
serialization is checked before commit creates artifacts. Journal intent records
an operation count, digest of the full ordered plan and at most eight previews,
including captured payload hashes and excluding import-host paths. This is audit
information, not authenticated recovery authority or replay instructions.

Cleanup never overwrites a source. Before-finalize cleanup requires source and
backup to match the recorded baseline; a stale stage cannot undo a replacement.
Catalog-only interruption cleanup takes the source lock and removes only its
stale pointer. Changed/ambiguous or invalid records and cleanup failures remain
visible. Arbitrary orphan `.write-*` files are not followed or automatically
removed. Actual process-kill tests and real application restart differ from
power-loss qualification; neither establishes a general power-loss guarantee.

## Evidence and packages

Runnable review packages now exist in [the review directory](../artifacts/review-20261116/):
Linux x64/ARM64 tar archives, Windows x64/ARM64 ZIPs and a separate generated-only
fixture bundle. The [package manifest](../artifacts/review-20261116/package-manifest.json)
records every decoded application member, SHA256 and legal/media exclusions.
The [Linux x64 AppImage](../artifacts/publish/SectorWorks-linux-x64.AppImage)
also passes its packaged resource check. Cross-publishes remain distinct from
physical ARM64 validation. The [source review export](../artifacts/review-20261116/SectorWorks-review-source.zip)
preserves current tracked and untracked source, including pre-existing owner
changes. Its decoded-member manifest and the package manifest identify exact
bytes; owner inputs, build caches, private access records and generated outputs
are excluded. Earlier candidates remain identified separately.

### Verification by source and platform

| Source / host | Result |
| --- | --- |
| R3 Linux full `./scripts/verify.sh` | **2386 passed, one Windows-only BCL skip, 2387 total**: Storage 2059 plus skip, Services 210, UI 76, dependency 27, foundation 14. Zero build warnings/errors; format, resources, AppRun and five reference-copy comparisons pass. |
| R4 Linux affected suites | **Services 211/211, UI 76/76, reader/layout 49/49**; the missing-XDG fresh-profile child also passes independently and after the final RuntimeProbe formatting change. No full R4 solution rerun is claimed. |
| R3 Windows standard user | Foundation 14, dependency 27, Services 210 and selected Storage 626 pass. Full UI: **74 pass / 2 fail**, both missing Linux `mkntfs` fixture-authoring commands before image open. Failures remain evidence. |
| R4 Windows standard user | Release build: zero warnings/errors. **Services 210 pass / one Linux-only XDG skip (211 total); portable UI 73/73; reader/layout 49/49**. Portable UI selection excludes the three Linux-native fixture-authoring cases; it does not erase earlier failures. |
| Final R3 portable Windows writer regression | **168 writer + 9 growth cases**; all 84 source inputs unchanged. **186 exact outputs / 876 independent Linux commands** pass. These are distinct from the expanded 441-row selections. |
| Final R4 packaging | Four self-contained RID publishes and Linux x64 AppImage pass; every decoded review-package member matches its manifest. Physical ARM64 remains untested. |

R3 source ZIP SHA256:
`8FEEA52D28C12C0AB6F5A8FBA683F73D711D742F695B5A00A72E1C6CA06F8B54`.
R4b source ZIP SHA256:
`5CF876F6C1F69E16CAB7ED82E4CE132022842EDB0C5E6940F2AF6D301C0B3050`.
Final product sources match R4b. R4 corrects first-run application-data lookup
using `LocalApplicationData` with `DoNotVerify`, rejecting relative fallback;
it covers staging, recovery, logs, preferences and layout. It also removes the
stale reader-only diagnostic annotation. Writer/commit algorithms are unchanged
from the full R3 run. The final RuntimeProbe change is formatting only and is
recorded separately in the final source/code manifests.

### Actual published application results

Linux R3 published UI passes native file selection, navigation, staging/history,
Save As, confirmed commit, preview, read-only ISO and startup cleanup/refusal.
The first fresh-profile AppImage attempt failed at application-data resolution;
its logs and screenshot remain archived. Corrected R4 AppImage then passes from
a nonexistent XDG data directory: native pickers, directory/file staging, Save As,
confirmed commit, cleared history and independent NTFS checks of exact saved and
committed bytes. A further actual before/after-replacement process-kill pair and
AppImage restart cleans only the unchanged-source case and refuses the stale
Validated record after replacement; both source hashes remain unchanged during
recovery. The AppImage ran with `APPIMAGE_EXTRACT_AND_RUN=1` in the FUSE-restricted
agent context. Linux used a task-owned nested display; the owner's desktop stayed
locked. The application and nested display are closed.

Windows R3 published UI passes native picker, navigation, staged/history changes,
Save As, confirmed commit/history refresh and actual restarted recovery cleanup/
refusal. Both returned saved and committed bytes pass independent Linux NTFS
checks. The captured native Windows VHD browses read-only, with editing/commit
refused for unsupported `$Extend` transaction/unknown metadata; its full hash
stays unchanged. The application was closed and desktop control released.
The visible desktop tunnel later closed; it was not recreated. The corrected R4
package was transferred, hash-verified and extracted without desktop launch;
its command-line suites pass, but no final R4 Windows GUI pass is claimed.

### Cleanup and retained evidence

All expanded evidence was removed only after archive/member hashes and restore
mappings were checked. Main Linux cleanup removed **3507 files / 89,011,368,941
logical bytes (88,889,184,256 allocated bytes)**. Standard-user Windows cleanup
removed **3300 files / 88,080,916,508 logical bytes** from seven exact generated
media roots, verifying closed-file hashes and rejecting links/unexpected names.
Supplemental Linux cleanup removes a task-created failed build cache (1058 files,
1,810,863,368 bytes), two duplicate extracted FAT images (33,554,432 bytes), and
one Python cache (23,866 bytes); failed AppImage evidence and oversized provider
metadata are retained losslessly in verified archives. These are logical counts,
not a claim of equal physical disk-space recovery on every filesystem.

Compact source snapshots, failed/successful raw evidence, decoded maps, pinned
SDK/dependencies, current runnable publishes and earlier qualification evidence
are retained. The standard SSH account cannot access the desktop user's profile;
two small UI test roots and one intentionally stale changed-source recovery
registration therefore remain there, with evidence already archived. No ACL or
privilege change was attempted. Small failed native-capture/runner roots and
reproduction source/tool trees also remain on Windows. No test image is left
attached by this task. Owner images, prior releases and immutable reader fixtures
were not changed or removed; no new Tower access was necessary. Task-wide recovery
and source checkpoints remain outside synchronized storage. Dropbox stayed stopped
through finalization; its final restart result is reported in the handoff chat.

### Remaining actions, bundled for later availability

- Restore the owner-visible Windows desktop connection when desired, then check
  the already-deployed corrected R4 UI and remove only the retained task-profile
  test roots/stale registration after checking their recorded identities.
- If native investigation resumes, run the already-prepared corrected bounded
  LFS capture through legitimate UAC. Its result alone will not establish TxF/KTM
  persistent recovery/update safety; prepared/in-doubt, both-client and TOPS /
  `$TXF_DATA` obligations still require defensible evidence before writing.
- Provide established physical ARM64 access and obtain exact reader/staging/UI
  evidence. Cross-publishes are available for that work.
- Review/accept the bounded delivery and decide how to pursue the retained
  required native gates. Neither full phase completion nor Phase 4 follows
  automatically.


## Owner acceptance walkthrough

Use the generated-only fixture bundle and disposable copies:

1. Open a qualified FAT or bounded legacy/list NTFS image. Stage a directory and
   file, inspect preview/Pending Changes, then undo and redo.
2. Use Save As and check that the source and pending changes remain. Use Ctrl+S,
   inspect confirmation, commit, and check the cleared history and refreshed tree.
3. Inspect the real interrupted-process/startup screenshots and source hashes in
   the evidence. Tools → Interrupted Commit Recovery defaults to keeping artifacts.
   Do not copy active path-bearing journals between machines; fresh interruption
   cases must be created against disposable images on the current host.
4. Open the captured Windows transaction-bearing example and an optical fixture;
   inspect their disabled edit/commit controls and concrete reasons.

Owner acceptance and any further native elevated run remain separate actions.
No later phase begins automatically.
