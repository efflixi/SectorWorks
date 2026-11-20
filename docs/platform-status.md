# Build and physical-platform status

The [v0.5.5 prerelease report](prerelease-v0.5.5.md) records the prior published
development prerelease regression and package checks. The [v0.5.4 compatibility report](prerelease-v0.5.4.md)
covers the preceding CUE, ISO, and FAT compatibility changes.

## Phase 6 review build

Linux x64 completed 2,552 passes with one Windows-only skip after the
unfinished storage suite was recovered in three disjoint sections.
The Windows 11 x64 laptop passed 78 focused checks with four platform
skips and opened the published app for PMFX extraction and APFS
browsing. The default Windows suite had missing test utilities and
other recorded setup gaps. The configured mixed-volume rerun passed all
21 cases, and Linux independently checked its 63 exact outputs.

Four Windows/Linux x64/ARM64 packages built and passed archive
validation. Physical ARM64 remains deferred. See the
[Phase 6 report](phase6-additional-formats.md) and
[review evidence](../artifacts/phase6-review/evidence-index.json).

## UI and package review before Phase 6

The owner tried the [local UI and packaging review build](../artifacts/ui-packaging-review/)
on a Windows 11 Pro work PC and reported that the application and the new UI
features worked in normal use, including the read-only notice and drag-to-open
workflow. This records an owner-reported native Windows smoke test. CPU
architecture and Windows build number are unspecified.

The [review evidence index](../artifacts/ui-packaging-review/evidence-index.json)
records the earlier automated checks and the unavailable laptop connection
during agent verification.

The owner also reported that the CD metadata review build worked as intended
on their Windows work PC. This covers the user-triggered MusicBrainz lookup
and use of confirmed track names in WAV exports. The laptop was unavailable
for the v0.5.5 regression. See the [metadata review evidence](../artifacts/cd-metadata-review/evidence-index.json).

## Phase 5 — BIN/CUE and CD audio

Published Windows x64 and Linux x64 AppImage desktop runs browsed and edited a
generated mixed-mode set, published a fresh folder, and independently verified
exact selected data and preserved audio/CUE bytes. Windows standard-user
endpoint metering and Linux loopback confirmed output from direct sector
playback. The Windows endpoint stayed muted, no UAC prompts were needed, and
no source disk was mounted. Four Windows/Linux x64/ARM64 review packages
build; physical ARM64 remains **NOT VERIFIED — DEFERRED POST-1.0 BY OWNER**.
See the [Phase 5 report](phase5-bin-cue.md).

## Phase 4 — optical rebuild

The final local review candidates build and publish for all four Windows/Linux
x64 and ARM64 RIDs, plus a Linux x64 AppImage. Published Windows x64 and Linux
x64 desktop runs used ordinary host permissions and independent verification of
the exact rebuilt outputs. Windows required zero UAC prompts. The published
Linux x64 components also rebuilt representative generated no-emulation BIOS
and UEFI images that booted under QEMU. This does not qualify arbitrary boot
metadata or exact-layout preservation.

See the [Phase 4 report](phase4-optical-rebuild.md) and [evidence
index](../artifacts/phase4-review/evidence-index.json). Physical Windows and
Linux ARM64 remain **NOT VERIFIED — DEFERRED POST-1.0 BY OWNER**. No macOS
package, external publication or Phase 5 implementation.

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
**NOT VERIFIED — NONBLOCKING BY OWNER DECISION**; no physical pass is inferred.


## Resumed closeout, November16 evening / November17 UTC

**Ready for owner review; Phase2D and Phase3 remain incomplete.** The
[current review](phase2d-phase3-review.md) and
[resumed evidence index](../artifacts/resume-20261116T231137Z/evidence-index.json)
supersede the earlier handoff's pending R4 desktop/R2 statements below.

Current R4b Linux tests finish2387 passes/one Windows-only skip,2388total, zero
failures. The original verification command then fails in MSBuild temporary
directory creation. A fresh private TMPDIR passes formatting; the unreached UI
resource stage and shell/AppRun/five reference-copy checks pass. This is staged
continuation with retained failure, not a second complete verify.sh run; the
original environmental cause remains unestablished. All337 earlier final-code
manifest members still match; no product/tool source changed in this continuation.

Retained current R4b Windows GUI evidence covers native picker/navigation,
preview/hex, staging/history, Save As, confirmed commit/history refresh, real
restarted cleanup/stale-stage refusal and preferences restart. Both exact images
now pass56 independent Linux NTFS commands, including preserved original file
metadata/content, replacement bytes, qualified log and boot sectors. Ten private
store rows plus separate-process reload pass; explicit-path checks do not claim
a new OS account. Current Windows CLI counts remain210Services/oneLinux-only
skip,73portableUI,49reader; earlier missing-mkntfs failures remain historical.

Completed elevated LFS R2 has two full133103616-byte captures, unchanged seed,
exit0 and detach. Both mounted restart pages are2.0; both locked/detached1.1.
All183 mounted-to-locked and189 locked-to-detached changed sectors map to MFT,
NTFS log, CLFS and/or root-index streams. The held lock provides an exclusive
flushed observation already in1.1, not a safe2.0 writer oracle. NoTxF R1 failed
safely; fresh quick-format R2 succeeds but retains repair/deleted metadata even
without observed TxF logs/data. Both persistent transaction/repair semantics and
independent LFS2/cache safety remain blocked; product refusals are unchanged.

Allfour R4 packages reverify1130 decoded members; AppImage/generated fixtures
retain separate seals. Physical ARM64 remains unavailable through established
access. Current source export/report seals are in the resumed review manifest.

Resumed cleanup: Windows195files/507300094logical bytes from current GUI/six
native roots, stale test pointer removed with source unchanged, normal171-byte
preferences/layout retained. Linux227archived expanded files/1331364517logical
bytes plus45disposable cache files/1879640bytes removed. Every original198
resumed evidence member and supplementary decoded archive verifies before
removal. Prior evidence/dependencies/packages/owner media remain. Finite cleanup
first failed before entry under Restricted script-file policy; the same sealed
body succeeds inline under legitimate UAC without changing policy or ACLs.

Final Windows observation:15remaining historical generated disks detached,
zero query errors, no task application/test processes, standard application
token; desktop control released. Manual tunnel retained. Dropbox remains stopped
through final integrity/packaging; its final restart result is reported in chat.

The remaining sections preserve dated evidence, not current pending work.

## Autonomous Phase 2D / Phase 3 assignment, 2026-11-16

**Stopped for owner review; Phase 2D and Phase 3 remain incomplete.** The
continuous assignment delivered the independently achievable bounded NTFS writer,
commit UI, startup recovery and review packages without waiving native Windows or
physical-platform gates. See the [final review](phase2d-phase3-review.md),
[chronology](phase2d-phase3-progress.md) and
[evidence index](../artifacts/autonomous-20261116/evidence-index.json).

Dropbox reported **Up to date** at 11:43:42 UTC before stop, and was verified
stopped before project work. The initial outside-sync checkpoint preserves 388
tracked/untracked files and pre-existing owner changes. Final source export,
raw evidence, compact archives, restore maps and cleanup receipts preserve the
result; private connection/recovery records stay outside synchronized storage.
The earlier 386-file policy-task checkpoint below remains historical.

### Final bounded writer and platform results

- **441 rows** = three NTFS versions × seven ordinary/list feature layouts × 21
  disk/layout/sector rows. Linux staged/Save As441 and commit441 pass. Windows
  Save As441 independently passes3465 commands before commit; its441 committed
  outputs separately pass3465 commands. All441 original inputs stay unchanged.
  Mixed21 sets preserve dirty siblings and every outside-primary byte.
- Final R3 Windows retained regression: **168 writer +9 genuine growth cases**,
  **186 exact outputs /876 independent Linux checks**, all84 inputs unchanged.
- Full R3 Linux verification: **2386 pass /one Windows-only skip,2387 total**;
  zero warnings/errors, format/resources/AppRun/five reference copies pass.
- R4 missing-profile-path correction: Linux **Services211/UI76/reader49** pass;
  Windows **Services210/one Linux-only skip,portableUI73,reader49** pass, Release
  build zero warnings/errors. No full R4 solution run is claimed. The separate
  fresh-profile child passes again after final RuntimeProbe-only formatting.
- R3 Windows foundation14/dependency27/Services210/Storage626 pass. Full UI
  **74 pass/2 fail** from unavailable Linux `mkntfs` authoring; raw failures stay.
- Final R4 **four publishes plus Linux x64 AppImage** pass; decoded runnable
  packages and generated-only fixtures are verified. Physical ARM64 is unavailable.

R3 source ZIP SHA256:
`8FEEA52D28C12C0AB6F5A8FBA683F73D711D742F695B5A00A72E1C6CA06F8B54`.
R4b source ZIP SHA256:
`5CF876F6C1F69E16CAB7ED82E4CE132022842EDB0C5E6940F2AF6D301C0B3050`.
Final product sources match R4b; final source export includes subsequent tool-only
formatting and finalized documentation. R4 does not change writer algorithms.

Actual R3 published Linux/Windows file picker, navigation, staging/history,
Save As, confirmed commit and restarted recovery cleanup/refusal pass. Exact
Windows saved and committed bytes pass native Linux checks. The first fresh-data
AppImage launch failed; the corrected R4 AppImage passes first-profile startup,
full edit/Save As/commit and another real before/after-replacement kill/restart
pair with unchanged recovery source hashes. Linux uses a closed task-owned
nested display and extraction-mode AppImage in the FUSE-restricted context.
Windows app/control were closed/released before the visible tunnel disappeared;
no tunnel was recreated. Final R4 Windows package is deployed and CLI-tested,
but **its final desktop recheck remains unavailable**.

### Native blockers and cleanup

Detached complete CLFS copies yield16 matching restart/forward payloads across
six known-state clients without elevation. This does not establish persistent
TxF/KTM both-client recovery/update, prepared/in-doubt, TOPS or `$TXF_DATA` rules.
The mounted LFS2.0 capture is4096 bytes short; detached1.1 is not proof of cache
safety. Corrected bounded capture compiles/refuses standard-user entry; elevated
R2 remains unrun because the owner cannot approve UAC today. No further UAC was
requested. Modern Windows-authored transaction/log writer rows stay disabled;
no account, ACL, authentication, UAC policy or real disk was changed.

Main Linux cleanup:3507 files/89,011,368,941 logical bytes removed after verified
archive/member/restore checks. Windows:3300 files/88,080,916,508 logical bytes
removed from seven exact generated roots. Supplemental disposable build/cache
cleanup and failed-evidence compression are recorded in the review/index.
Required dependencies, snapshots, raw failed/successful reports and all compact
archives remain. Two Windows desktop-profile task roots and one stale recovery
registration remain because the restricted SSH token cannot access that profile
and desktop control is closed; no elevation/ACL workaround was attempted. All
task-owned applications/displays and native image attachments are closed.
No new owner-media access, release, commit, push, tag or later phase occurred.

## Owner workflow and platform policy, 2026-11-16

The local Linux machine remains the primary development and default test host.
For Windows-version tests, the verified `windows_laptop` MCP connection is now
the default command/desktop interface. The owner authorizes routine preparation,
testing, inspection and cleanup autonomously, requesting help only for concrete
privilege, credential, unavailable-hardware or scope blockers. The actual
Notepad capture/launch/type/unsaved-window check is recorded in
[Windows desktop testing](windows-desktop-testing.md); it is infrastructure
verification, not a SectorWorks GUI pass. Product GUI/platform and TxF/LFS gates
remain incomplete.

The owner adopted WinVOX's Dropbox preflight/final-restart and routine cleanup
rules into `AGENTS.md`. Dropbox reported **Up to date** before stop, and a
subsequent check confirmed it was not running before ordinary edits/builds.
A verified outside-sync checkpoint preserves **386** tracked/untracked source
files and existing dirty changes; restricted owner inputs remain in place.
Dropbox stays stopped through verification, evidence, documentation and cleanup;
`dropbox start` is the final shell command, with its outcome reported in chat.

Build/distribution scope is now **Windows and Linux only**, each x64/ARM64:
`linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`. Earlier target policies are
superseded. The shared publisher and runtime matrix are narrowed; platform-only
sidecar locking and alternate extraction flags are removed. Windows deny-write
sharing/atomic replacement and Linux descriptor-owned locking/no-follow
extraction retain their safety checks. Required transitive dependency identities
and license texts remain unchanged. Authoritative technical/UI/design references
are updated together; mockups remain unchanged.

The two retired current publish trees were losslessly archived outside Dropbox,
with every decoded file hash and original restore path checked before removing
the expanded copies: **562 files / 231990590 bytes**, archive **92769677 bytes**.
Prior releases, immutable qualification reports and owner media are untouched.
The first lock-graph audit correctly found the opt-in ReaderProbe outside the
solution; its separate target-lock refresh/build is included in final validation.
This is a tooling-coverage finding, not a product-test failure. Original failed
check and subsequent results are retained separately.

Current evidence: ignored `artifacts/platform-policy-20261116-105327/`.
Final `./scripts/verify.sh` passes **1184 tests / one existing Windows-only
BCL skip (1185 total)**: Storage 898/one skip, Services 177, UI 68, dependency
27 and foundation 14. Release build has **zero warnings/errors**; formatting,
AppRun contracts, UI resources and all five authoritative copy comparisons
pass. Storage took **21m18s**; Services **7m48s**. The opt-in ReaderProbe's
separate restore/build also passes with zero warnings/errors. All **15** lock
files retain byte-equivalent dependency graphs for the remaining target
sections and unchanged dependency versions.

`./scripts/publish.sh all` passes for exactly the four supported RIDs and the
Linux x64 AppImage, including its local resource execution check. Independent
ELF/PE header, legal-file, payload-exclusion and AppImage checksum checks pass.
Both retired publisher arguments refuse with exit **2** before restore/build.
These are local Linux verification and cross-publish results, not fresh Windows
product GUI/native-volume, ARM64 runtime or complete Phase 2D qualification.

After build-server shutdown and a check that no process mapped these output
trees, cleanup removed **30 reproducible bin/obj trees: 4093 files / 7620656705
bytes**, plus two task temporary files. Important test/app assembly hashes,
logs, the original failed lock audit and successful follow-up audit are retained.
Pinned global SDK/NuGet dependencies, the four publish deliverables/AppImage,
prior releases, immutable fixtures/evidence and protected owner inputs remain.
The build caches are reproducible with the documented restore/build commands.
The final source checkpoint, archive mappings and recovery state remain outside
synchronized storage; the final integrity checks run after cleanup.

Dated reports below retain their original verification/source scope. No phase
advance, source commit/push or release occurred. Dropbox restart is the final
shell command after all checks and writes; its returned result is reported in
chat rather than followed by another documentation write.

## Resumed TxF/CLFS investigation, 2026-10-21

**Latest follow-up:** R6 completed and detached. All eight attribute opens
succeed, while six protected-metadata GENERIC_READ opens and both CLFS opens
return access denied. Native handle identities match the independently read
NTFS records; exact output/seed hashes and **seven Linux reads** pass. The owner
token has backup privilege present but disabled. See
[R6 results](#owner-executed-r6-results-attribute-access-is-not-data-access).
A subsequent owner-launched R7 backup candidate failed a PowerShell dictionary
checker; the image detached and full output/seed hashes match. See
[R7 failure](#r7-owner-execution-failed-dictionary-result-check).
The [corrected R8 probe](#owner-executed-r8-results-backup-restored-access-still-denied)
has now completed: backup privilege activation/restoration and original token
state verification pass, while before/with-backup access results are identical.
The same six metadata data-read opens and both protected CLFS opens return
access denied. Exact output/seed hashes, raw handle identities and seven
independent Linux reads pass. Backup privilege alone is insufficient for this
read path; no protected native restart/record query ran. The bounded candidate
evaluation is complete, while the native-oracle route remains blocked.
The generic CLFS positive test and **19 Python tests** remain their passed
snapshots. Persistent recovery/update rules and writer qualification remain
incomplete. Preserve all single-use evidence; do not repeat earlier commands.

The owner explicitly resumed the remaining Phase 2D Windows-native TxF/CLFS
compatibility work after mandatory context recovery. The completed runtime
corrective snapshot below remains historical evidence. This new increment
investigates retained generated Windows bytes and strengthens refusal tests;
it does not complete Phase 2D, expand any writer gate or continue Phase 3.

The new opt-in `scripts/inspect-clfs.py` decodes bounded base-log descriptor
and stream/container metadata, checks the observed CRC32/sector envelopes,
reports both copies without selecting a recovery state, and keeps persistent
quiescence **unproven** and writer qualification **false**. It has no product
dependency or release-payload integration. See
[reference discrepancies and remaining proof obligations](dependency-evaluation.md#resumed-txfclfs-structural-investigation-2026-10-21).

Actual local execution used **only** the retained legal generated Windows NTFS
partition and native `ntfscat -i 33` extraction, not owner media or a mounted
volume. The partition is **133103616 bytes**, SHA256 before/after:
`49782d4011f7fa91524245f10ec677c3105d95ea782855c5d39a8bae0377ee36`.
The independently extracted **65536-byte** base stream matches the earlier raw
inventory/native evidence:
`1b1606171a86214092d641d428e48d99b9c9ff210126481a9b92dee8804438f9`.
The observer's own full source hashes agree before/after as well.
Both metadata copies decode, at **2048/33280**, dump counters **19/20**;
physical/TxfLog/KtmLog streams and two **2 MiB** containers are observed.
No log/container was modified and no native transaction/disk control was run.
Subsequent nonprivileged Windows harness validation is recorded below; it is
not a TxF inactivity or cache-safety result.

Evidence under `artifacts/phase2d-txf-clfs-20261021/`:
`base-observations.json`, `source-integrity.json`, `generated-txf-base.blf`,
and focused `tests/txf-write-safety.trx`. Generated payloads remain ignored.
The initial focused Linux NTFS safety run passed **61/61**, zero skips, including
six new TxF cases. Final `scripts/verify.sh` passes **1184 tests, one Windows-only
BCL skip (1185 total)**: Storage **898/one skip**, Services **177**, UI **68**,
dependency **27**, foundation **14**. Locked restore, zero-warning/error Release
build, formatting, UI resources/AppRun and all five reference copies pass.
Log: `verify.log` in this increment's evidence directory. No product runtime
source changed; then-configured publishes were not repeated for this tooling/test
increment and retain the previous frozen-source evidence below.
The independent Python diagnostic suite passes **10 tests**, including grouped
malformed-input cases; these counts are separate from .NET solution tests.

Reproduce the synthetic observer checks with:

```sh
python3 scripts/test-inspect-clfs.py
python3 scripts/inspect-clfs.py \
  artifacts/phase2d-txf-clfs-20261021/generated-txf-base.blf \
  --sha256 1b1606171a86214092d641d428e48d99b9c9ff210126481a9b92dee8804438f9
```

**Remaining blockers:** persistent both-client recovery/active-range semantics,
prepared/indoubt evidence, TOPS/`$TXF_DATA` update obligations and subsequent
edited-output/native-reopen/stack qualification. LFS 2.0/cache safety remains
separate. All required 2D.3-2D.5 gates remain incomplete; unavailable ARM64,
GUI/platform/review requirements are unchanged.
No specification, signed dependency, product writer implementation, release,
tag or publication changed in this increment.

### Container decoding and native comparison

After the successful R3 controls below, the owner authorized all three proposed
investigation, rule-development and conditional writer-verification steps
continuously, pausing only for needed decisions/evidence/owner-only actions.
The observer now has a bounded `--container` mode. It enumerates the known
unrecycled prefix, restores sector tails, checks record lengths/alignment and
stream-local virtual LSN progression, and reports opaque payloads/hashes and
restart candidates. It rejects owner-page/split/recycled layouts and unsupported
continuations rather than guessing mappings. It still returns **unproven**
persistent quiescence and **false** writer qualification. Zero container
checksum fields supply no checksum assurance; a negative test makes that
limitation explicit. See [scope and API references](dependency-evaluation.md#container-records-and-native-read-oracle-preparation).

Six additional native `ntfscat` reads independently extract both containers
from each exact R3 raw partition, with exit zero/no warnings and hashes matching
the earlier independent raw inventories. All six bounded observations pass:

| R3 control | First container blocks / records | Occupied prefix bytes | Last physical TxF restart candidate | KTM candidate |
| --- | --- | --- | --- | --- |
| Ordinary | 4 / 5 | 2048 | `0x600`, 112-byte payload | `0`, 32-byte payload |
| Committed | 9 / 15 | 6656 | `0x1600`, 112-byte payload | `0x1401`, 32-byte payload |
| RolledBack | 8 / 13 | 5632 | `0x1400`, 112-byte payload | `0`, 32-byte payload |

All second containers are zero. The KTM candidate payload bytes/hash are
identical across the three controls. Both base copies' raw stream field 80
match the corresponding last physical restart candidate in each control.
These correlations do not establish active-copy/range semantics or decode
client checkpoint tables. Historical completed records remain physically
present. No writer rule follows from scanning nonzero blocks.

Evidence is under the existing ignored `native-controls-r3/` directory:
each control has `generated-container-34.bin`, `generated-container-35.bin`
and the corresponding `container-34-observations.json`/
`container-35-observations.json`; `container-comparison-summary.json` records
counts, hashes and raw-field correlations. The diagnostic suite now passes
**17/17 Python tests**. The earlier complete .NET result remains **1184 passes,
one Windows-only skip**, not a new execution or a combined test count.

Example, after reproducing the independent generated extraction:

```sh
python3 scripts/test-inspect-clfs.py
python3 scripts/inspect-clfs.py --container \
  artifacts/phase2d-txf-clfs-20261021/native-controls-r3/Committed/generated-container-34.bin \
  --sha256 00e02c73afa11a07714e3753a2714d7e67c5d8a40a2b0efe846553504ff25c0d
```

### Native CLFS read oracle R4: owner-local launch

**Historical R4 preparation; owner execution is recorded below. Do not rerun
these single-use commands.** At this snapshot the new
single-use batch creates three fresh **R4** virtual-disk copies of the same
hash-pinned Native-R2 seed. It preserves all existing R3 evidence. Each control
records the actual KTM transaction GUID and queries native CLFS information,
restart areas, forward records and file TxF metadata before/after; transacted
controls also query while the rename is active. An independent helper checks
the exact generated disk/partition identity and requests only existing read
handles. API errors are retained; no write-access retry or ACL change exists.

Each observer child has a **30-second** deadline and bounded record/payload
budgets. Context/handle cleanup is explicit. Native CLFS cleanup may flush
pending I/O, so this is live generated-control observation, not an unchanged
detached-volume check. Fresh output cleanup, namespace/payload checks, native
nonrepairing checks and detached seed/output hashes retain the original control
bounds. No reset/replay/repair, real disks, service/schedule or SSH elevation.

Actual Windows **5.1.26100.9711** standard-account validation passes all four
script parses, both C# declaration compilations, synthetic payload copy/hash/
LSN and budget checks, three expected privilege-guard refusals and exact local/
transferred hashes. No native log/disk API methods were invoked in validation.
Evidence: `windows-oracle-r4-validation.json` in this increment's ignored
artifact directory. The initial large-stdin validation attempt timed out;
a subsequent file invocation encountered the existing execution policy.
Final validation parses transferred files and evaluates guards in memory;
neither machine policy nor account privileges changed. These failed validation
attempts are not native-control failures or successful executions.

The four scripts are transferred to
`C:\Users\swtest\SectorWorks-TxF-Controls-R4`:

| Script | Exact SHA256 |
| --- | --- |
| [`windows-native-process.ps1`](../scripts/windows-native-process.ps1) | `2174B91111EB4954E9116887D8028D24F6CF2D0135D2806ED61978DEED7F7D69` |
| [`windows-native-txf-controls.ps1`](../scripts/windows-native-txf-controls.ps1) | `306E6C2E0945D74C1B36C746D10AE417B42DFA2CD7B498CE631721A02215F399` |
| [`windows-native-clfs-oracle.ps1`](../scripts/windows-native-clfs-oracle.ps1) | `2DE389368370393512674D3FBCDCB0A4193BA9C71FC96E36A0F24C4487F00396` |
| [`windows-native-txf-oracle-batch.ps1`](../scripts/windows-native-txf-oracle-batch.ps1) | `7915ADEFC12D115825A8F175306DA1E4FC4639CDFC04793C16FA956B1DCCCDE4` |

Review the linked scripts. From a **local Administrator PowerShell**, run once:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'C:\Users\swtest\SectorWorks-TxF-Controls-R4\windows-native-txf-oracle-batch.ps1'
```

Execution policy applies only to these processes, as in the earlier handoff;
do not change machine policy or elevate the standard SSH account. The
[owner-local attachment requirement](../AGENTS.md) is why this single launch
cannot be performed by the agent. Existing R4 roots cause refusal; never delete
or reuse them to retry. Stop at any error and retain evidence. This batch cannot
establish prepared/indoubt semantics, offline update rules, LFS 2.0/cache safety
or product edited-output interoperability by itself. Continue comparison and
conditional implementation after collecting its actual results, without
inventing a passed gate.

### Owner-executed R4 results and reader correction

All three R4 controls ran locally on Windows 11 build **10.0.26200**,
PowerShell **5.1.26100.9711**, during **21:48:49-21:49:15 UTC** on October 21.
The owner retained the standard SSH account. Control reports pass expected
namespace/content behavior, transaction completion, cleanup/detach and seed
hash checks. All **28** native child-process reports exit zero without timeout
(including eight oracle children and three attachment commands). This does
**not** mean their inner CLFS API calls succeeded.

| R4 control | Exact detached VHD SHA256 | Active/native-after file LSN |
| --- | --- | --- |
| Ordinary | `369973FEE2EE884004FBA96F10E3AB4265A88B56027E3A12450C40C26264AFAA` | No file TxF metadata |
| Committed | `F3A5C9707D9FCF80E30CBE7416EEAEC3C881668D0BC259372EBFF2594CC2CD11` | `0x603` / `0x603` |
| RolledBack | `7878B76CEF22F809071F4706DF35EC8A8F5D54388663A1C9A546191750933D87` | `0x603` / `0x1000` |

Subsequent standard-account exclusive reads match all three hashes and the
unchanged Native-R2 seed. Exact downloads pass independent bounded QEMU/MBR/raw
inventory checks, **20** `ntfscat` stream comparisons plus **six** namespace/
payload commands, and before/after VHD/partition hashes. Container counts stay
**4/9/8 blocks, 5/15/13 records**; detached restart pages are fixup-valid LFS
**1.1**, not a raw LFS 2.0/cache-safety result. These are native-authored
contrast images, not SectorWorks-edited outputs.

All **16** CLFS opens report **Win32 error 2**. The log path erroneously ended
in `TxfLog`; the independently inventoried file is **`$TxfLog.blf`**. This is
a harness error, not evidence of a missing store or a need to elevate SSH.
The successful file-state queries still provide useful independent evidence:
the active file's locking GUID matches `GetTransactionId` and the raw log
records, its native state is **1**, and its last LSN is **0x603**. After commit
or rollback the state becomes **0** and the locking GUID clears, while the
native file ID/last LSN match raw `$TXF_DATA` fields in this corpus. The root
reports state 0 during both active child transactions; root-only checks cannot
establish whole-volume inactivity.

Exact reports, VHD/raw hashes, independent comparisons, GUID matches and the
four scripts **as executed** are retained under the ignored
`artifacts/phase2d-txf-clfs-20261021/native-controls-r4/`. R4's original Windows
handoff and output roots remain unchanged. Its missing CLFS observations leave
2D.11h incomplete; no product writer capability follows.

### Positive native CLFS self-test and bounded suffix refusal

The corrected helper preserves the native filename's literal `$` characters.
An SDK ABI review corrected `CLFS_RECORD_TYPE` to **byte by reference** and
the client filter to **3**. The new
[`windows-clfs-oracle-selftest.ps1`](../scripts/windows-clfs-oracle-selftest.ps1)
passes as the **standard** Windows account under a **45-second** deadline.
It creates only a new user-local generic multiplexed log, writes known
application data/restart bytes with CLFS APIs, closes the writers and reads
both streams through the actual corrected helper. No disk is attached and
no existing log or owner media is used.

All **eight** records match writer-returned LSNs, types and payloads; four
latest/previous restart observations and native cleanup pass. Forward EOF is
**38**; previous-restart termination is **6618**. Independently downloaded
logical prefixes reproduce exact native LSNs, payload bytes/hashes, low type
bits and previous/undo links for all eight records. Both base copies' raw fields
**56/72/80** match native base/last/restart LSNs. Raw field **64** does **not**
match the native `LastFlushedLsn` in this control; it remains unlabeled.

Actual files contain additional bytes beyond the native logical sizes:

| Generic file | Actual bytes | Native logical bytes | Uninterpreted suffix bytes |
| --- | --- | --- | --- |
| BLF | 73728 | 65536 | 8192 |
| Each of two containers | 2232320 | 2097152 | 135168 |

This is consistent with documented newer CLFS authentication overhead; the
suffixes are **not decoded/authenticated**. Full file hashes are retained and
complete extended files remain refused by the raw diagnostic. The comparison
uses explicitly labeled copies of logical prefixes only, never modified native
inputs. Unknown base-log suffixes, including zero suffixes, now fail instead
of being ignored. The decoder also checks the bounded nonzero initial virtual
LSN observed for the second generic client. Current Python tests pass **19/19**;
this is separate from the unchanged earlier .NET **1184-pass/one-skip** run.

Evidence: `windows-clfs-synthetic-selftest-r1.json` and `synthetic-clfs-r1/`
under the ignored increment artifacts, including `offline-native-comparison.json`.
See [primary references and qualification limits](dependency-evaluation.md#native-r4-results-and-corrected-oracle).
Generic transport/field correlations do not establish TxF/KTM recovery or
offline update safety. No authentication policy/reset/replay/repair is changed.

### Corrected native CLFS oracle R5: owner-local launch

**Historical preparation; owner execution is recorded below. Do not rerun.** This
single-use batch creates fresh R5 controls, retaining all R3/R4 evidence. The
reader correction has a real known-byte CLFS positive test. The batch also
checks each required before/active/after stage: the original R4 missing-query
reports now fail its coverage regression; known complete **16** observations
pass and partial coverage fails. A control-authoring pass alone no longer
produces a successful batch summary.

All five scripts parse on actual Windows PS5; both API declarations compile,
the three owner-only guards refuse the standard account, and all exact
transferred hashes match. This validation invokes no native disk/log methods;
the separate user-local positive self-test above does exercise native CLFS.
The finite child budgets and generated-disk identity guards are retained.
An initial oversized validation command hit Windows's command-line limit;
transferring the validator as a file resolved it without account/policy changes.
Evidence: `windows-oracle-r5-validation.json` in the ignored artifact directory.

The scripts are in `C:\Users\swtest\SectorWorks-TxF-Controls-R5`:

| Script | Exact SHA256 |
| --- | --- |
| [`windows-native-process.ps1`](../scripts/windows-native-process.ps1) | `2174B91111EB4954E9116887D8028D24F6CF2D0135D2806ED61978DEED7F7D69` |
| [`windows-native-txf-controls.ps1`](../scripts/windows-native-txf-controls.ps1) | `BA19844F5C6577424C7F5A3229D15C10ADA0B83E8A94E8A9F4CB6A3B967CA1A6` |
| [`windows-native-clfs-oracle.ps1`](../scripts/windows-native-clfs-oracle.ps1) | `46357D04A287230656858920ECF2E422D85BE15E82D7DF95FD61AF22AED5DDA5` |
| [`windows-native-txf-oracle-batch.ps1`](../scripts/windows-native-txf-oracle-batch.ps1) | `37CB285B77A25807561A1FEB3FDD0ABC38F7A8C2BF1E0D7294EF86F30B780D2C` |
| [`windows-clfs-oracle-selftest.ps1`](../scripts/windows-clfs-oracle-selftest.ps1) | `AF60BEAFBBBE9886ED7E92595A30B720576064D2E9EE299C3525961103DB670C` |

Historical owner-local command, already executed; **do not repeat**:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'C:\Users\swtest\SectorWorks-TxF-Controls-R5\windows-native-txf-oracle-batch.ps1'
```

Only process-local execution policy applies; the SSH account stays standard.
Do not run the standalone synthetic self-test as administrator or reuse its
single-use root. Existing R5 control roots also cause refusal; stop at any
error and retain evidence. Subsequent comparison must check the actual native
API terminal codes/payloads, not just the batch exit. Neither a successful
oracle nor the owner launch itself closes the persistent recovery/update,
edited-output, LFS 2.0/cache-safety, ARM64 or remaining Phase 2D gates.

### Owner-executed R5 results: protected log access denied

All three controls completed during **22:09:16-22:09:41 UTC**, October 21,
on Windows build **10.0.26200**, PowerShell **5.1.26100.9711**. Ordinary rename,
committed transactional rename and rolled-back transactional rename all pass
their native control assertions, payload checks, cleanup/detach and seed hashes.
All **28 child-process reports** exit zero without timeout. The final aggregate
coverage assertion at **line 26** of
[`windows-native-txf-oracle-batch.ps1`](../scripts/windows-native-txf-oracle-batch.ps1)
then fails because every required stage reports zero successful CLFS reads.
It is not a failure of the third control's transaction or detach operation.

| R5 control | Exact detached VHD SHA256 |
| --- | --- |
| Ordinary | `8101F608F3E9A21561F81B29856D0D0B11BF187B3B54F3BAE14AB99117BA75CF` |
| Committed | `3614B5FCFD09FB02F7B22AC5FFD1C557C2A14266374DE9D410297B0C7C74D3A7` |
| RolledBack | `48D72612FD18037911CE387795336805F401A9A5E27C9749062DB66CA1692D1F` |

The corrected literal-dollar path now reaches an access failure: **16/16**
protected TxF/KTM CLFS opens return **Win32 error 5**, `Access is denied`, even
with the owner-local administrator token. **Zero** information/restart/record
observations succeeded. The reports do not distinguish file ACLs from other
native protected-log restrictions; they do not justify elevating SSH, changing
ACLs, retrying with write access or bypassing authentication. Generic self-test
success cannot replace these missing observations. The coverage guard remains
intact, and the original scripts as executed are retained with matching hashes.

File-state queries still succeed. Both active files have native state **1** and
the exact `GetTransactionId` locking GUID; after completion state becomes **0**,
the locking GUID clears, and file ID **1** / last LSN **0x603** (commit) or
**0x1000** (rollback) match the bounded raw `$TXF_DATA` observations. Independent
Linux verification of the **exact** downloads passes **20 stream comparisons**
and **six namespace/payload commands**, without native-tool warnings; full VHD
and partition hashes remain unchanged. Both detached `$LogFile` restart pages
are fixup-valid **LFS 1.1**. None is a SectorWorks-edited output or modern-log
cache-safety result. Standard-account exclusive reads also verify all three
detached VHD hashes and the unchanged Native-R2 seed.

Evidence is retained under ignored
`artifacts/phase2d-txf-clfs-20261021/native-controls-r5/`: original reports,
five `harness-as-executed` scripts, exclusive source hashes, exact generated
VHDs, derived raw inventories, bounded CLFS observations,
`independent-comparison-summary.json` and `coverage-failure-summary.json`.
No account, ACL, authentication policy or owner media was changed.

The read-only alternative review found a narrower documented API surface,
not a replacement recovery oracle:

- [`TxfLogCreateRangeReadContext`](https://learn.microsoft.com/en-us/windows/win32/api/txfw32/nf-txfw32-txflogcreaterangereadcontext)
  creates replication-record contexts, and
  [`TxfLogReadRecords`](https://learn.microsoft.com/en-us/windows/win32/api/txfw32/nf-txfw32-txflogreadrecords)
  returns redo records. These documents do not establish complete access to
  both clients' checkpoints, undo data or prepared/indoubt recovery state.
- [`TXFS_QUERY_RM_INFORMATION`](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-txfs_query_rm_information)
  reports transaction count at query time. That live value is not a persistent
  detached acceptance rule. Microsoft's
  [KTM log-stream documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/using-log-streams-with-ktm)
  describes distinct KTM and resource-manager recovery streams/restart areas.

Those are documentation findings, not executed alternative probes or proof
that no solution exists. The next prerequisite is an independently validated
both-client checkpoint/active-range schema and a defensible offline update rule;
the current protected-log oracle route is blocked. No repeat owner batch is
prepared on speculation. **2D.11h/2D.11d and writer gates remain incomplete**;
conditional writer implementation cannot start from this evidence.

A subsequent **offline access-descriptor check** used only the retained R5
Ordinary partition. Independent `ntfscat` extraction of `$Secure:$SDS` matches
the raw inventory hash; full VHD/partition hashes still match the verified
baseline. Stored descriptors for `$RmMetadata`/`$TxfLog` (Security ID **258**),
the BLF (**259**) and both containers (**260**) contain allow ACEs for the
Administrators SID `S-1-5-32-544`: masks **0x120089**, **0x1200a9** and
**0x1200a9**, respectively. Each includes file read-data/read-attributes/read-EA,
READ_CONTROL and SYNCHRONIZE. SYSTEM has **0x1f01ff**. These simple stored
DACLs do not establish effective live-token access, validate the security
indexes or explain CLFS's specific denial. They do make a blanket claim that
the stored DACL lacks administrator read permission unsupported by this corpus.
Evidence: `Ordinary/protected-log-security-observations.json` in the ignored
R5 evidence directory. No descriptor is modified.

The owner subsequently authorized the proposed next steps. A new focused R6
diagnostic is now prepared and validated below. It distinguishes an ordinary
read-only file open of the physical generated BLF/containers from the
corresponding read-only CLFS client open, recording the exact failed operation
and error code. Even a successful access diagnosis must be followed by
independent both-client recovery/update qualification.

### Focused R6 access diagnostic: owner-local launch required

**Historical preparation; executed results follow below. Do not rerun R6.**
This is one focused access diagnostic, not a
repeat of the three R5 rename controls. It copies only the exact sealed
Native-R2 generated VHD to a fresh
`C:\Users\Public\SectorWorks-Phase2D-TxF-R6-Access` directory. The immutable
seed and all previous R3/R4/R5 evidence stay detached/retained. Native Windows
may update the disposable copy during attachment; it is not an immutable raw
capture or a qualified writer output.

[`windows-native-clfs-access-audit.ps1`](../scripts/windows-native-clfs-access-audit.ps1)
verifies the file-backed **128 MiB** virtual disk, MBR, exact partition offset/
size and disk identity. Its finite child performs **18** operations: ordinary
file opens at READ_ATTRIBUTES and GENERIC_READ for the root, known payload,
`$Extend`, `$RmMetadata`, `$TxfLog`, BLF and two containers, then two GENERIC_READ
CLFS client opens. Every open uses OPEN_EXISTING and share read/write/delete.
Successful ordinary handles also return basic identity/type attributes.
It records exact native failures and the existing backup/restore privilege
state, closes handles, detaches the generated copy, hashes it and rechecks the
seed. It does not enable privileges, alter protected ACLs, request write/delete
access, read/replay/reset log records, configure a resource manager, rename a
file or create a TxF transaction. Only the newly created evidence-directory ACL
is initialized, using the existing administrator-write/standard-user-read pattern.
There is no real-disk argument, automatic elevation, service or schedule.

Attachment/detachment child budgets are **30 seconds** each; the access child
has **45 seconds**, with retained process reports. The child requires the exact
parent-owned image/drive/disk identity. Incomplete/duplicate operations, failed
handle cleanup or failed ordinary-payload positive controls fail the parent.
Access-denied observations are retained as diagnostic findings, **not** passed
CLFS reads: `Completed` means the diagnostic finished, while
`ClfsOpensSucceeded` records successful native access separately. Persistent
quiescence remains Unproven and WriterQualified remains false.

Actual standard-account validation on Windows PowerShell **5.1.26100.9711**:

- All **four** transferred scripts parse; audit and synthetic-author declarations
  compile. Both owner-only entry modes reject the standard account.
- Five invalid drive prefixes are refused; every literal `$` is retained.
  Complete 18-operation evidence is accepted and **nine** incomplete/invalid
  reports are refused. Write-access masks are rejected before opening a file.
- Fresh user-local controls produce **10 successful read-only observations**
  (eight physical-file opens and two generic CLFS opens), plus two correctly
  reported missing-file/log failures with error **2**. Basic handle metadata
  and native cleanup succeed. All **seven** generated source-file hashes remain
  unchanged during read-only probing, including authentication-related files.
- Backup/restore privileges are absent/disabled in that standard token and
  unchanged before/after. No disk attachment or TxF operation is invoked.
- A separate pure-guard run accepts one valid mocked virtual disk and rejects
  **12** invalid/ambiguous disk/image responses (including physical, boot,
  system, wrong-size/layout/identity and read-only cases), without invoking
  storage cmdlets or native disk/log methods. Final read-only preflight verifies
  the four transferred hashes, unchanged seed and absent R6 output root.

The first wrapper invocation was rejected by PowerShell execution policy before
tests. Adding the existing process-local `-ExecutionPolicy Bypass` to that
invocation resolved it; no account/machine policy changed. Both the failed
pre-test launch and successful validation are retained under ignored
`artifacts/phase2d-txf-clfs-20261021/windows-access-r6-validation*.json`, with
`r6-transferred-manifest.json` and `windows-access-r6-guard-preflight.json`.
This is tooling-only verification, not a new
full .NET/publish run or a protected-volume access result.

The validated files are in `C:\Users\swtest\SectorWorks-TxF-Access-R6`:

| Script | Exact SHA256 |
| --- | --- |
| [`windows-native-clfs-access-audit.ps1`](../scripts/windows-native-clfs-access-audit.ps1) | `DE392EA96BC68FAB16E8ACAAB9D606EB8FF2091B2D29196E3F43B5D17B2E10AD` |
| [`test-windows-native-clfs-access-audit.ps1`](../scripts/test-windows-native-clfs-access-audit.ps1) | `968D0547B1E219A6D517C8EED5377A7B4C61F7BDF6953EAC46B03FE8CBFC1325` |
| [`windows-native-process.ps1`](../scripts/windows-native-process.ps1) | `2174B91111EB4954E9116887D8028D24F6CF2D0135D2806ED61978DEED7F7D69` |
| [`windows-clfs-oracle-selftest.ps1`](../scripts/windows-clfs-oracle-selftest.ps1) | `AF60BEAFBBBE9886ED7E92595A30B720576064D2E9EE299C3525961103DB670C` |

Historical owner-local command, already executed; **do not repeat**:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'C:\Users\swtest\SectorWorks-TxF-Access-R6\windows-native-clfs-access-audit.ps1'
```

Do not launch the validation/self-test scripts as administrator or repeat R5.
Keep all output on any error; existing R6 roots cause refusal and must not be
deleted/reused. The project owner-only virtual-disk rule is why the agent stops
at this handoff, with the SSH account unchanged. Next compare the exact physical
versus CLFS open results before proposing any new oracle collection method;
do not infer an ACL fix or writer acceptance from error codes alone. Recovery/
prepared/indoubt/update rules, Windows-edited-output/LFS/cache safety, ARM64 and
remaining Phase 2D gates are still incomplete and unwaived.

### Owner-executed R6 results: attribute access is not data access

The owner ran R6 during **22:37:09-22:37:16 UTC**, October 21, Windows build
**10.0.26200**, PS **5.1.26100.9711**. The diagnostic completed all **18**
observations, closed its handles, detached the generated image and verified the
unchanged seed. Attachment, probe and detach process reports exit zero without
timeout. This is successful diagnostic execution, not successful protected-log
access: **10** operations succeed and **eight** return error **5**.

| Targets | READ_ATTRIBUTES | GENERIC_READ |
| --- | --- | --- |
| Root, ordinary payload (two targets) | Both succeed | Both succeed |
| `$Extend`, `$RmMetadata`, `$TxfLog` directories (three) | All succeed | All denied, error 5 |
| `$TxfLog.blf`, two containers (three) | All succeed | All denied, error 5 |
| TxF and KTM CLFS client opens | Not requested | Both denied, error 5 |

The successful metadata handles identify MFT **11/27/30/33/34/35**, with matching
record sequences and volume serial in the independently decoded output.
This excludes the earlier missing-dollar path bug as the explanation for these
observations. Denial already occurs in ordinary `CreateFileW` GENERIC_READ
opens; the record reader/ABI is not reached. It still does not identify which
additional requested right or native policy causes the denial. Microsoft's
[file access documentation](https://learn.microsoft.com/en-us/windows/win32/fileio/file-security-and-access-rights)
lists the additional rights included in generic read; attribute queries alone
do not establish access to file data.

The owner administrator token reports **SeBackupPrivilege and SeRestorePrivilege
present, both disabled**. R6 changed neither. That state is a candidate for
investigation, not proof of the denial's cause or that activation will fix it.
The standard SSH account stays unelevated and has neither privilege assigned
in the preceding standard-user control.

Exact detached VHD SHA256:
`E88135FF216CC32606006F2E5DEDB865EE61C3A9EADE608BADF6E1BC40DD967B`.
Subsequent standard-account exclusive reads verify this hash and the immutable
Native-R2 seed. Exact downloads pass bounded QEMU/MBR/raw checks and **seven**
independent Linux commands: five `$SDS`/BLF/container/`$LogFile` stream reads,
one namespace listing and one payload read. Stream lengths/hashes match the raw
inventory; the original 52-byte payload/name remains intact. Both detached
restart pages report LFS **1.1**, not modern-log/cache safety. Full VHD/partition
hashes remain unchanged during analysis. Original process reports, executed
scripts and `independent-comparison.json` are retained under ignored
`artifacts/phase2d-txf-clfs-20261021/native-access-r6/`.
This closes only the bounded access diagnostic, **2D.11j**; 2D.11h/d and writer
qualification remain incomplete.

### R7 backup-read candidate: explicit owner opt-in required

**Historical R7 preparation; owner execution subsequently failed its result
checker as recorded below. Do not repeat R7.** Microsoft's
[backup access documentation](https://learn.microsoft.com/en-us/windows/win32/fileio/file-security-and-access-rights)
describes using an already granted, enabled backup privilege with backup
semantics. Its
[token privilege documentation](https://learn.microsoft.com/en-us/windows/win32/secauthz/enabling-and-disabling-privileges-in-c--)
distinguishes enabling an existing privilege from granting one. This supports
a bounded candidate test, not a conclusion that NTFS/CLFS will permit the reads.

The new script requires explicit **`-EnableOwnerBackupRead`**. The owner-local
parent creates only a fresh sealed-seed copy under
`C:\Users\Public\SectorWorks-Phase2D-TxF-R7-Access`, with the same image/partition/
disk-identity guards and finite 30/45/30-second native budgets as R6. Only the
isolated **45-second child** activates its own existing SeBackupPrivilege; it
requires that privilege to be present and initially disabled. No token privilege
is granted to an account, SeRestorePrivilege enabled, ACL changed on a volume,
authentication disabled, repair/reset/replay performed or arbitrary privileged
command accepted. No SSH token or interactive-shell token is adjusted.

The child records the same **18** opens before and with backup enabled.
Only if both CLFS clients open successfully does it invoke the existing
hash-pinned, byte-verified bounded read oracle for information/restart/records.
It does not author a TxF transaction. All compilation occurs before activation.
The scope restores and checks the original backup attributes in `finally`, then
the child compares both backup/restore states against its initial snapshot.
Restoration/cleanup/envelope failures fail the parent and retain reports;
terminating a timed-out child ends that child's token. Native read-context
cleanup can flush CLFS state on the disposable generated image, as previously
documented; it is not an immutable live capture. Original seed/owner media and
prior R3-R6 outputs are never attached by this script.

Actual **standard-account** validation passes four script parses, **three**
C# declarations and **five** refusal cases (three driver entry modes, the
helper script and the scope constructor). The constructor refuses before native
privilege adjustment. Token query state stays unchanged; no disk or CLFS call
is invoked. Exact transferred hashes match and the new output root is absent.
R7 retains R6's unchanged base API and observation/disk-guard functions; prior
R6 positive/malformed controls remain their own snapshot. These results do
**not** validate the positive activation or restoration path. No product
runtime/writer code changes, full .NET rerun or new publish is claimed.
Evidence: ignored `windows-backup-r7-validation.json` and
`r7-transferred-manifest.json` in the current investigation artifacts.

Files in `C:\Users\swtest\SectorWorks-TxF-Backup-R7`:

| Script | Exact SHA256 |
| --- | --- |
| [`windows-native-clfs-backup-probe.ps1`](../scripts/windows-native-clfs-backup-probe.ps1) | `2350DEF36599E3C4B6B3D9044843EEE01CD242F7FD47136F60AD4198C74D05DB` |
| [`windows-owner-backup-read-scope.ps1`](../scripts/windows-owner-backup-read-scope.ps1) | `A55E17BCA64146B22B1C5F6A2F34678C9989117885BCEE9F00C6B130CE843F16` |
| [`windows-native-clfs-oracle.ps1`](../scripts/windows-native-clfs-oracle.ps1) | `46357D04A287230656858920ECF2E422D85BE15E82D7DF95FD61AF22AED5DDA5` |
| [`windows-native-process.ps1`](../scripts/windows-native-process.ps1) | `2174B91111EB4954E9116887D8028D24F6CF2D0135D2806ED61978DEED7F7D69` |

Historical owner-local command, already attempted; **do not repeat**:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'C:\Users\swtest\SectorWorks-TxF-Backup-R7\windows-native-clfs-backup-probe.ps1' -EnableOwnerBackupRead
```

Do not run this through SSH or repeat older controls. Existing roots cause
refusal; retain all reports on error. A diagnostic completion is not a passed
CLFS/recovery oracle: inspect individual open/record/terminal/cleanup results
and the verified token restoration separately. Even successful native reads
still precede both-client recovery/update proof, conditional writer expansion,
Windows-edited-output/native reopen and LFS 2.0/cache safety. All those gates,
ARM64 and the rest of Phase 2D remain incomplete and unwaived.

### R7 owner execution: failed dictionary result check

The owner launched R7 during **22:53:14-22:53:21 UTC**, October 21. Attachment
and detach reports exit zero; the probe child exits **1**, without timeout.
Parent line **333** reports that child failure. Its retained stderr identifies
the actual exception at checker line **84**: `Failed operation lacks its native
error.` The error is in the diagnostic, not evidence of a new native access
restriction. No successful backup/oracle or writer result is claimed.

The R7 checker was inherited from R6's **JSON-deserialized** parent check. R7
also calls it inside the child with raw C# `Dictionary<string,object>` rows.
Their `Win32Error` keys exist, but `PSObject.Properties['Win32Error']` does not
expose those entries. A valid recorded denial therefore triggers the missing-
error assertion. The same lookup also misses raw dictionary cleanup-error
keys. Earlier parse/compilation/privilege-refusal tests and JSON fixtures did
not exercise this native result shape; that was a coverage gap.

A **standard-account, in-memory** reproduction against the exact as-executed
R7 checker confirms the key is present, its PSObject property is absent, raw
rows fail with the reported message and their JSON round-trip passes. It
invokes no disk, CLFS or privilege-adjustment API. The subsequent R8 regression
also reproduces this failure with real native user-local results.

R7 did not persist baseline/with-backup stage observations before validation.
The failure is consistent with its first baseline assertion, but the saved
wrapper error alone cannot establish the activation/restoration stage. Those
outcomes remain **unverified**; the finite child exited, ending its process-local
token. Neither the interactive-shell nor SSH token is adjusted by this design.
R8 adds explicit partial-stage records rather than inferring outcomes from a
wrapper exit or source-order expectation.

The generated image detached successfully. Subsequent standard-account
exclusive reads confirm the immutable seed and detached VHD SHA256
`74BBF441A174B5488E2075E7CAED0C884DA44201823CFFF0AA994EC60CF2DB98`.
The exact download matches; original reports, diskpart commands, VHD and four
as-executed scripts are preserved under ignored
`artifacts/phase2d-txf-clfs-20261021/native-access-r7/`, including
`dictionary-reproduction.json` and `windows-exclusive-source-hashes.json`.
The R7 Windows handoff/output directories remain untouched. Historical R7
source hashes above refer to those retained versions; the current linked
backup-probe source is now R8.

### Corrected R8 backup probe: owner-local launch required

**Historical preparation; the owner subsequently completed R8 as recorded
below. Do not repeat this single-use command.** The correction uses a dictionary-aware field check for raw native
rows and PSObject properties for JSON rows. Required error/cleanup/coverage
checks stay intact. The same fixed checker is used inside the child and after
parent JSON decoding. R6's base native API, disk guards, backup-scope helper
and bounded read oracle are unchanged; no writer gate or privilege scope is
expanded.

R8 creates a fresh `C:\Users\Public\SectorWorks-Phase2D-TxF-R8-Access` generated
copy only. It retains the explicit `-EnableOwnerBackupRead` opt-in, existing
disabled backup privilege in an isolated finite child, restoration/state checks,
conditional two-client read oracle, identity/partition guards and finite native
budgets described for R7. Before validation it now saves
`before-observation.json`; `with-backup-observation.json` is saved in `finally`,
with partial results and explicit restoration state even on checker/cleanup
failure. Previous R7 evidence is not reused or deleted.

Actual standard Windows PS5 verification of
[`test-windows-native-clfs-backup-probe.ps1`](../scripts/test-windows-native-clfs-backup-probe.ps1)
passes:

- Six script parses (including retained original R7), three API compilations
  and all **five** owner-only refusals; no privilege adjustment, disk attachment
  or protected-log call occurs.
- **18 actual native user-local observations**: four ordinary root/file opens
  succeed and fourteen missing-file/log calls correctly record error **2**.
  The original R7 checker fails on these raw dictionaries with the reported
  error; the corrected checker accepts both native and JSON forms.
- **18 malformed evidence cases** are rejected: the same nine missing,
  duplicate, unattempted, missing-error, cleanup-error, wrong-target,
  false-writer-claim, incomplete and failed-positive cases in both forms.
  In particular, a raw dictionary cleanup error is detected.
- The generated ordinary file's hash/file set and token query state remain
  unchanged. All six transferred hashes match; the R8 output root is absent.

These are actual result-shape/safety regressions, not a positive backup-token
activation/restoration test, protected TxF/KTM read or new .NET/publish run.
Evidence: ignored `windows-backup-r8-validation.json`,
`r8-transferred-manifest.json` and the R7 reproduction above. No valid assertion
was removed or bypassed to hide the failure.

Files in `C:\Users\swtest\SectorWorks-TxF-Backup-R8`:

| Script | Exact SHA256 |
| --- | --- |
| [`windows-native-clfs-backup-probe.ps1`](../scripts/windows-native-clfs-backup-probe.ps1) | `06996BC282C806A072548C9F70EE663BEE9BA8DE3401BC67236FF05FC8B12F2B` |
| [`windows-owner-backup-read-scope.ps1`](../scripts/windows-owner-backup-read-scope.ps1) | `A55E17BCA64146B22B1C5F6A2F34678C9989117885BCEE9F00C6B130CE843F16` |
| [`windows-native-clfs-oracle.ps1`](../scripts/windows-native-clfs-oracle.ps1) | `46357D04A287230656858920ECF2E422D85BE15E82D7DF95FD61AF22AED5DDA5` |
| [`windows-native-process.ps1`](../scripts/windows-native-process.ps1) | `2174B91111EB4954E9116887D8028D24F6CF2D0135D2806ED61978DEED7F7D69` |
| [`test-windows-native-clfs-backup-probe.ps1`](../scripts/test-windows-native-clfs-backup-probe.ps1) | `6F0F01706E9319E4FC7BD20EDDCFEAEEBDC48B8B8F382FAC9919E0F3DDEAA361` |
| Retained `previous-r7.ps1` | `2350DEF36599E3C4B6B3D9044843EEE01CD242F7FD47136F60AD4198C74D05DB` |

The historical handoff required a **local Administrator PowerShell**, keeping
SSH standard. This is the command already executed, not a new launch request:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File 'C:\Users\swtest\SectorWorks-TxF-Backup-R8\windows-native-clfs-backup-probe.ps1' -EnableOwnerBackupRead
```

At that preparation snapshot, positive activation/restoration and protected
backup access were untested; the subsequent results distinguish them below.
The diagnostic is not a native repair or permission to expand writing without
proof.

### Owner-executed R8 results: backup restored, access still denied

The owner completed R8 during **23:04:17-23:04:24 UTC**, October 21, on Windows
build **26200**, PowerShell **5.1.26100.9711**. Attachment, the finite diagnostic
child and detachment all exit **0**, without timeout. The child's CLIXML stderr
contains progress records, with no error/warning stream. Both standalone stage
files agree with the complete child report; all four executed source/helper
hashes match the handoff above.

The child positively exercises the existing backup privilege: it is present
and disabled before, enabled during the second observation, and restored to
disabled afterward. Restore privilege stays disabled. Original queried token
state and explicit scope restoration both verify. This is now actual positive
activation/restoration evidence, unlike the earlier standard-user guard tests
or failed R7 run; it applies only to the isolated finite child.

| Observation | Before backup | With backup enabled |
| --- | --- | --- |
| Attribute opens, eight targets | 8 succeed | 8 succeed |
| Ordinary root/payload GENERIC_READ | 2 succeed | 2 succeed |
| Six protected metadata GENERIC_READ opens | 6 denied, error 5 | 6 denied, error 5 |
| TxF and KTM CLFS client opens | 2 denied, error 5 | 2 denied, error 5 |

All **18 before/18 with-backup** rows are identical. Because neither protected
client opens, the conditional restart/record reader is correctly **not called**;
`Oracles` is empty with an explicit reason. The successful parent diagnostic
does not turn those denials into successful native log queries. Backup
activation alone is insufficient for this exact read path; the result does
not identify the underlying restriction or establish that every possible
backup/read mechanism fails.

Subsequent standard-account **exclusive reads** match the detached output and
unchanged seed; the downloaded exact output has SHA256:

```text
R8 VHD: 82dfe31fb5bb1ef170b527c949e54eb0b10df54cd09f7f1df0f5e0c0d1873e4c
Seed:   ce71bdbe0f656ad32744d1be3639326963422e9d57ec74acc7952b56b9148313
```

Independent Linux conversion confirms the bounded virtual disk/partition
layout. **Seven** native stream/namespace/payload reads agree with the raw
inventory, including `$SDS`, BLF, both containers, `$LogFile` and the unchanged
52-byte ordinary file. Every successful native handle's file ID/volume serial
matches the raw MFT/boot record in both stages. Both detached RSTR pages retain
version **1.1** and matching sector fixups; this supplies no new attached-LFS-2.0
or cache-safety qualification. Full VHD/derived-partition hashes match before
and after inspection.

Ignored evidence is retained in
`artifacts/phase2d-txf-clfs-20261021/native-access-r8/`: original parent/child/
stage/attach/detach reports, exact VHD, four `harness-as-executed` scripts,
exclusive Windows hashes, raw inventory and `independent-comparison.json`.
This completes **2D.11k's bounded candidate evaluation**, not **2D.11h** native
both-client query coverage or **2D.11d** persistent recovery/update proof.
No full .NET suite or publishes were repeated for this evidence/docs-only
follow-up; their earlier snapshots remain explicitly dated.

The next missing prerequisite is a justified independent protected-log oracle
or validated persistent TxF/KTM format sufficient to prove both-client
quiescence and offline update/preservation semantics, including prepared/
indoubt cases and authentication. The present public API review and generated
corpus do not supply that contract; see
[remaining format/access gaps](dependency-evaluation.md#r8-negative-access-result-and-remaining-proof-gaps).
No additional owner-local run is prepared: broader privileges, write-access
retries or authentication changes are not justified by these denials. Keep
all single-use roots, writer gates, LFS/cache, GUI, ARM64 and Phase 2D review
requirements intact. Phase 2D remains incomplete.

### Prepared native controls: local owner launch required

This records the original handoff. **The owner has now run all three controls
successfully; do not repeat these single-use commands or delete their evidence
roots.** Executed results follow the preparation record.

`scripts/windows-native-txf-controls.ps1` is prepared for three separate
single-use controls: **Ordinary**, **Committed**, **RolledBack**. Each copies
only the exact hash-pinned, detached Native-R2 generated VHD into its own new
Public test root. It verifies the 128 MiB file-backed virtual disk/MBR partition,
performs one ordinary or transacted synthetic-file rename, records live RM
information before/during/after, checks namespace/payload outcomes and native
nonrepairing checks, then cleanly detaches and hashes the output and immutable
seed. Failures retain evidence; unresolved transaction cleanup prevents an
automatic detach. No resource reset/stop, repair, arbitrary image/disk argument,
real disk, elevated SSH, service or schedule exists.

Actual Windows PowerShell **5.1.26100.9711**, as the unchanged **standard**
testing account, now passes parser validation, compilation of the test-only
native API declarations and expected administrator refusal **before any disk
operation**. At that preparation snapshot, the three controls themselves were
**not executed or qualified**; subsequent owner execution is recorded below.
Prior command-length failure, two corrected PS5 continuation errors and the
stdin-EOF timeout are retained as validation chronology, not native failures.
Line-delimited stdin resolved the validation transport timeout. Final evidence:
`windows-control-validation-r4.log`; checksum-verified script-only transfer:
`windows-harness-transfer-r2.log`, in the current evidence directory. No API
transaction method was called during validation.

The reviewed harness/helper are already in the Windows standard profile at
`C:\Users\swtest\SectorWorks-TxF-Controls-R3`. The root is a script handoff,
not a privileged command broker. In a **local Administrator PowerShell**,
after reviewing the script, the owner can verify both hashes and launch each
control in a fresh process, stopping at the first failure:

```powershell
$harness = 'C:\Users\swtest\SectorWorks-TxF-Controls-R3'
$expected = @{
    'windows-native-txf-controls.ps1' = '8590DEB4EDAEAA2576D2FF52AC84B7CA44315003C7A2FE328ADFF6021D11384D'
    'windows-native-process.ps1' = '2174B91111EB4954E9116887D8028D24F6CF2D0135D2806ED61978DEED7F7D69'
}
foreach ($name in $expected.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $harness $name) -Algorithm SHA256).Hash -ne $expected[$name]) {
        throw "Harness hash changed: $name"
    }
}
foreach ($control in @('Ordinary', 'Committed', 'RolledBack')) {
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $harness 'windows-native-txf-controls.ps1') -Control $control
    if ($LASTEXITCODE -ne 0) { throw "Stop and retain evidence: $control failed" }
}
```

ExecutionPolicy Bypass applies to those child processes only; do not change
machine policy or the standard account's group membership. The owner-launch
boundary is explicit in [AGENTS.md](../AGENTS.md). The three fresh output roots
are `PUBLIC\SectorWorks-Phase2D-TxF-R3-Ordinary`, `-Committed`, and `-RolledBack`;
never delete/reuse existing evidence to rerun a failed control.
These native-authored contrast images would be the next read-only investigation
inputs. Their successful preparation alone would not qualify SectorWorks editing,
detached recovery state, prepared/indoubt handling or LFS 2.0/cache safety.

### Owner-executed native TxF controls, 2026-10-21

The owner locally executed all three modes on Windows 11, retaining the
standard SSH account. Reports cover **21:17:04-21:17:18 UTC**. Each reports
success, expected namespace/payload behavior, completed cleanup/detach and the
unchanged immutable generated seed SHA256
`CE71BDBE0F656AD32744D1BE3639326963422E9D57EC74ACC7952B56B9148313`.
Subsequent standard-account **exclusive read-handle hashes** independently
match that seed and each detached generated output; no elevation or native
attachment was performed through SSH. All **20** recorded native-process
invocations completed with exit zero and no timeout; this count includes the
three owner-local attachment commands, not just read-only checking tools.

| Native control | Observed rename outcome | Exact detached VHD SHA256 | CLFS dump counters, physical copy order |
| --- | --- | --- | --- |
| Ordinary | New name present, original absent | `FA4E15D7836579B00D8E56DEC6428ECB7DE04B7622A78EB93924A019A6EE7A94` | 23 / 22 |
| Committed | New name present, original absent | `C40A0051DCB928ECCB12D964D72426D805E5D489AB6AF1486184B0B6BA5A9B0B` | 25 / 24 |
| RolledBack | Original name present, new name absent | `2F5190A9E8B73913E4714BB4E078912906C8405EE80625DCB92C025A6D348898` | 23 / 22 |

Each VHD is **12615680 bytes** with a **128 MiB** virtual disk and the same
bounded MBR partition as its seed. Exact downloaded container hashes match
Windows reports/exclusive reads. Linux QEMU conversion writes only fresh
generated raw derivatives; partition offset/size are independently checked
against **65536/133103616 bytes**. Original VHD and partition hashes match
before/after inventory/base decoding. **17** independent Linux `ntfsls`/
`ntfscat` commands verify exact rename outcomes, payload bytes, `$LogFile`,
and every observed `$TXF_DATA` stream against independent raw inventory.
Every command exits zero without warnings. These are finite read-only
comparisons, not filesystem certification or product-written output checks.

The new evidence resolves several questions without providing a writer rule:

- Both actual active transactional controls report **one running transaction
  with TOPS still 100% free**, then zero running transactions after completion.
  The committed control records one two-phase commit. Empty/free TOPS cannot
  establish transaction inactivity, now demonstrated on actual native controls.
- `$Tops:$T` remains the same all-zero **1 MiB** stream in all three cleanly
  detached outputs, including committed and rolled-back transaction history.
- The ordinary control retains the seed's two `$TXF_DATA` values. Both TxF
  controls change root `$TXF_DATA` and leave a new **56-byte** `$TXF_DATA` on
  the affected ordinary file, MFT **42**, even after rollback restores its
  original name. These hashes independently match `ntfscat`; rolled-back
  namespace equality does not mean all metadata returned to the seed.
- Every attached native report identifies **LFS 2.0**; both detached raw RSTR
  pages identify **LFS 1.1** with valid sector fixups. No pre-detach raw pages
  were captured, so this still does not prove an exact transition instant or
  establish arbitrary LFS 2.0 cache safety.
- Both CLFS copies decode with the observed CRC/fixup checks. Their counters
  now put the larger value in the first physical slot, unlike the seed's
  19/20 order. No fixed slot is assumed authoritative by the observer.

Evidence: `artifacts/phase2d-txf-clfs-20261021/native-controls-r3/`, including
the original per-mode reports/native logs, exact generated VHDs, bounded raw
inventories/base observations and `windows-evidence-verification.json`,
`linux-control-observations.json`, `independent-comparison-summary.json`.
Existing application/observer sources are unchanged by this follow-up; the
preceding **1184/one skip** full verification remains its dated result,
not a newly executed test run or an added test count.

The known-state corpus is now available for further both-client persistent
checkpoint/container/owner-page and TOPS/`$TXF_DATA` interpretation. No
recoverable prepared/indoubt fixture or safe offline acceptance/update predicate
is established. Required product staged edits/Save As/native reopen and
2D.3-2D.5 remain incomplete; LFS 2.0/cache safety remains separately blocked.
No product writer gate, protected owner media, account permission or release
changed. The owner-local commands above should not be rerun.

## Documentation reconciliation, 2026-10-20

Owner approved a documentation-only reconciliation after the handoff audit.
Current summaries distinguish completed Windows codec/reader/generated-writer
and Tower subwork from incomplete Windows-authored edited-output/TxF/CLFS,
LFS 2.0/cache safety, current ARM64, GUI/platform and review gates. The corrective
batch below remains stopped for owner review; no phase-completion status changed.
Both specification copies are synchronized; historical test counts, failures and
authorizations retain their original snapshots. The audit checked the retained
290-file source manifest, TRX counters/hashes and source/media archive hashes;
it did not run new application tests, builds, remote commands or owner-media
probes. No implementation, gate waiver, commit/push or release is authorized.

## Final Global-session corrective snapshot

The owner-resumed 2D.10 runtime/verification pass is qualified on its frozen
source, **not a completed Phase 2D**. It repairs source reader/lock/atomic-finalize/
session lifecycle, system-global cross-session logging and preference error/
cleanup assertions without changing writer acceptance gates.

| Actual selection | Result |
| --- | --- |
| Linux writer-input preparation | 84/84 passed |
| Linux focused runtime, including independent-session before/after regression | 32/32 passed |
| Final Linux complete solution | 1178 passed, one Windows-only skip; 1179 total |
| Windows x64 focused runtime | 32/32 passed |
| Windows x64 full Services project | 177/177 passed |
| Windows x64 concurrent reader/version/attribute-list/security/safety selection | 392/392 passed |
| Windows x64 handoff guard tests | 12/12 passed |
| Windows x64 existing Save As/direct-commit matrix | 168/168 passed |
| Windows x64 real two-commit dynamic-container growth | 9/9 passed; 18 measured growth transitions |
| Independent Linux checks of exact Windows outputs | 186/186 outputs; 876/876 native commands exit zero |
| Final self-contained publishes/local Linux AppImage | Historical publish matrix/local AppImage passed |

`scripts/verify.sh` completed on this frozen source with zero build warnings/
errors: Storage **892 passed/one skip**, Services **177**, UI **68**, dependency
**27**, foundation **14**. The skip is the Windows BCL reference, executed on
actual Windows in the separately passing codec/security selection. Formatting,
headless UI resources, AppRun contracts and all five reference-copy comparisons
passed. Log: `artifacts/phase2d-corrective-verify.log`.

These are separate selections, not counts to add into a claimed Windows solution
run. Windows execution is standard-user CLI on actual Windows 11 Home x64, SDK
10.0.402/runtime 10.0.13, no administrator/native mount or new global install.
The full writer matrix retains 84 existing legal Linux-authored/generated rows;
all 84 inputs remain unchanged on both hosts. Nine FAT32 dynamic-VHD/VHDX cases
add 20 MiB + 17 bytes twice and prove physical growth after each real commit,
full content hashes, refreshed history/baseline and independent allocation checks.
They do not claim NTFS-specific physical-growth or Windows-native authoring.

Reproducible evidence:
- `artifacts/writer-handoff-global-final-run/final-summary.json`:
  per-selection TRX/hash, source/input/output identity and exact native commands.
- `artifacts/writer-handoff-global-final-run/final-closeout.json`:
  all **290** frozen source/config/test/script hashes still match.
- Catalog SHA256:
  `873C84E162D2B361FA610D3A1B0FBB45B02FA09F413F156DD6873D93D90E1E31`.
- Qualified byte-exact media archive: **36,939,989 bytes**, SHA256
  `BB50936B998E742931202949FE3D51BF5FFCA42B889689474558B3B0D2849C78`.
  All **568 members**, including **270 media files**, were fully hash-verified
  before deleting the enumerated matching uncompressed generated files and
  Windows duplicates. Historical evidence/archive remains separate.
  [Restore instructions](windows-writer-handoff.md#final-global-session-corrected-archive)
  preserve exact bytes without retaining approximately 24.44 GiB uncompressed.
- `artifacts/phase2d-corrective-publish.log`: current then-configured/AppImage evidence;
  physical compatibility remains separate.

The original 400/57 and 153/3 Windows failures, intermediate atomic replacement
failures, prior successful snapshot and cancelled/superseded Linux runs below
remain historical evidence, never relabeled as this source. Native Linux-only
authoring tests remain required Linux coverage, not silently passed Windows
cases or absent checks. No owner Tower/ISO/PMFX/native-R2 media was accessed in
this corrective pass; no owner original was copied, modified or packaged.

**Retained blockers:** actual Windows-authored NTFS edit/native reopen with
TxF/CLFS metadata, a demonstrably safe LFS 2.0/cache subset, unavailable Linux
ARM64 evidence, and separately retained GUI/platform/review gates. Authentic
legacy authoring stays post-1.0 P1.NTFS; new legacy/attribute-list writing and
remaining Phase 3 features remain held.
At the verification stop, no commit/push/tag/release/schedule had been authorized
or performed. The owner subsequently authorized source commit/push only on
2026-10-17, explicitly without a release. No tag/release/schedule or phase
advancement is authorized by that instruction.
The runtime/verification corrective batch is complete and stopped for owner
review. The listed relevant blockers remain required and incomplete; this is
not approval to finish Phase 3 or declare Phase 2D complete.

## Phase 2D preflight and closeout, 2026-10-17

Owner authorized a bounded 2D closeout of retained 2B/2C tasks after a read-only
prerequisite gate. Mandatory instructions/specification/checklist/current docs
and relevant source/retained logs were inspected before changes/tests/probes.
Technical-spec copies matched. Retained logs confirm the historical 1001-pass/
one-skip full 2C run and later 49-pass focused run, not a new full run.
Current source still skips the BCL reference off Windows and refuses LFS 2.0.
October 16 Phase 3 backend authorization/delivery is preserved; stale earlier
"Phase 3 unauthorized/unimplemented" language refers only to its dated state.
2D precedes remaining Phase 3 work; no later-phase/publication authority.

Preflight resolved here:
- Strict-key/no-PTY/publickey-only/BatchMode/strict-host-checking Windows SSH
  now succeeds as the expected standard account. Initial port-22 timeouts were
  resolved when the owner changed the laptop's active LAN profile from Public
  to Private; no firewall bypass or agent-side machine configuration occurred.
- Read-only Windows inventory: 64-bit OS/process, no administrator token,
  dotnet/Git absent, approximately 131 GiB free on the profile volume.
  Built-in diskpart/Mount-DiskImage/Dismount-DiskImage/Format-Volume exist;
  Hyper-V New-VHD/Get-VHD do not. Availability is not executed media evidence.
- Owner authorized user-local pinned tooling/project transfer and disposable
  generated test media. Native privileged preparation/reopen will use narrowly
  bounded finite scripts launched locally by the owner, never an elevated agent
  account, privileged command broker, real disks, service or schedule.
- Tower location was identified from directory metadata only (one matching
  8422654464-byte image), not by opening image bytes during preflight. Owner
  authorized in-place Linux full before/after SHA-256, bounded workflows and
  representative selected temporary extraction <=64 MiB outside synchronized/
  repository storage, with cleanup. Do not copy/mount/modify/transfer/distribute
  the image. Connection inventory and owner paths remain local-only.
- ARM64 machine is unavailable/off-network. Owner reports latest build works,
  without exact build/per-workflow results. Owner approved available-platform
  work while keeping 2D.8c blocked/unwaived. This is not new physical evidence.

No new application build/test, media qualification or GUI evidence is claimed
by preflight. Subsequent executed subitems are recorded below. The
[closure ledger](support-matrix.md#phase-2d-retained-closure-ledger-2026-10-17)
maps every retained incomplete 2B/2C gate without marking reassignment passed.
Authentic historical authoring remains P1.NTFS;
bootstrap lists unsupported; new legacy/attribute-list volumes noneditable.

Initial 2D safety execution: `NtfsWriteValidationTests` **55 passed, zero
skipped/failed**, including six new modern-log clean/closed/mixed-page cases.
Each retains readable media while refusing raw writer qualification and service
staging/Save As/commit, checks complete source SHA-256 and releases source handles.
TRX: `artifacts/phase2d-tests/phase2d-log-safety.trx`. This is targeted evidence,
not new full-suite or LFS 2.0 write qualification. The diagnostic now explains
that a clean bit cannot establish release of Windows cached metadata.
`record-repairs.py --verify` independently passes with imported source unchanged.

### Executed Windows reference and bounded reader evidence

Actual Windows x64 command-line execution uses a user-local official SDK
10.0.402 / runtime 10.0.13, with the SDK archive SHA-512 checked against official
release metadata. Windows reports OS build 10.0.26200. The standard SSH account
remains nonadministrator; private connection/key inventory is not repository data.

- `PortableNtfsSecurityTests`: **271 passed, zero failed/skipped**, including
  the actual Windows BCL reference. This closes the focused codec comparison,
  not complete inheritance, native filesystem editing or GUI qualification.
- The same bounded reader selection passes **66/66 on Windows**, zero
  failed/skipped. Linux-only native `ntfscat` comparisons are not claimed on
  Windows. Logs/TRX are retained under `artifacts/windows-phase2d/`.
- Linux x64 reader selection: **66 passed, zero failed/skipped** (the retained
  49 plus 17 new qualification cases). Nine workflow rows comprise three
  ordinary synthetic versions and six NTFS 3.1 attribute-list variants, not a
  version/feature cross-product. Every row checks browse/preview/paged hex/
  search/persisted extraction, independently expected bytes, complete unchanged
  source hashes and editing restrictions. Native `ntfscat` 2022.10.3 hashes
  agree on this host. Instrumented I/O/cancellation/error bounds also pass.
  Exact serialized SI/security/layout observations, fixture hashes and
  reproducible commands are in [fixtures](fixtures.md#phase-2d-reproducible-bounded-reader-rows-2026-10-17).
  Evidence: `artifacts/phase2d-reader.log`,
  `artifacts/phase2d-reader-tests/phase2d-reader.trx` and
  `artifacts/phase2d-reader-evidence.json`.

These results are bounded synthetic qualification, not authentic NT4/2000/XP
authoring or full physical-platform certification. Bootstrap lists remain
unsupported; new legacy/attribute-list volumes remain noneditable.

### Completed local-only Tower source/content checks

The approved in-place Linux probe completed full before/after SHA-256 with
unchanged file identity/size/mtime:
`D470478B0CC75D4F3A07D01A0548CDD58BCDB5E7B03A2C065D7994F45CF75DD7`.
It separately reproduces the original strict attribute-list rejection and
successful current bounded reader. Current enumeration is **904 child
directories / 905 including root, 16199 files**. Filtered/native root counts
are **10/24**, system32 **1844/1851**; these are different enumeration contracts,
not silent filter changes or missing-file certification.

Selected resident text and two nonresident DLL streams total **1235021 bytes**.
Production-reader, native `ntfscat` and persisted-extraction SHA-256 agree.
Shared read-only browse/preview/hex/search/extraction succeed; writing remains
disabled. Private extracted content and private command output were cleaned
up outside repository/synchronized storage. The original image was never
copied, mounted, modified, transferred or distributed. Safe metadata/hash
result: `artifacts/phase2d-tower-final-result.json`.

This closes the bounded source-integrity/selected-content requirement, not
exhaustive volume health, historical-OS interoperability or write safety.
The diagnosed 65536-byte native readall limit versus the 344064-byte index
remains a tool limit, not corruption evidence.

### Windows-native preparation and retained modern-log blocker

The owner locally authored a new disposable 128 MiB dynamic VHD using the
finite allowlisted native script. It contains NTFS 3.1, 512/512
sectors, 4096-byte clusters, 1024-byte MFT records and one MBR partition
(offset 65536, length 133103616), with seven synthetic entries and bounded
protected basic ACLs. Native dirty query and read-only `chkdsk` exit zero;
the detached full image hash matches recorded provenance.

An initial harness process-exit collection bug failed after creating/attaching
only its disposable VHD. Actual Windows exit-0/exit-7/timeout regressions now
pass. The owner's exact-image recovery detached that first VHD and retained
all evidence; the successful author retry uses a separate Native-R2 root.
The subsequent read-only source reopen reported a namespace mismatch: Windows
PowerShell 5.1 decoded UTF-8 JSON as ANSI, corrupting the Unicode comparison
key. Explicit UTF-8 decoding and its Windows regression correct this.
All seven captured entries agree including hashes/timestamps/ACLs/attributes;
the bounded `ReopenSourceRetry` passes native checks with unchanged image hash.
The failed evidence is retained. This is source-read evidence, not edited-output
interoperability.

Correction to the initial log-version interpretation: `fsutil` reported
**LFS 2.0 while writable-attached before dismount**, then **1.1 on read-only
reopen**. Bounded raw decoding of both detached 4096-byte restart pages observes
**LFS 1.1**. Pre-dismount raw pages were not captured, so the exact transition
instant remains inferred, not proven; do not classify the label as mere driver
support or claim Windows Home cannot use 2.0. Both
fixups validate, flags are `0x2`, and the NTFS client's current/restart LSN is
1590293, oldest LSN 1590280. Log SHA-256:
`ae6199c5120ed4b30f95ccdfbb55a5872a94c24a66098e3c1f39f7010cf19b77`.
Actual native `$Extend/$Deleted` and transaction-resource metadata
(`$RmMetadata`, `$Txf`, `$TxfLog`) are independent strict-writer blockers under
investigation. A complete 38-record inventory includes root/MFT31 `$TXF_DATA`,
`$Tops:$T`, `$Repair:$Config` and initialized nonzero CLFS logs (65536-byte base,
two 2 MiB containers). Empty `$Deleted`/`$Txf` directories do not prove resource
manager inactivity. Eight native stream hashes independently match bounded
decoding, including log/security/transaction streams. Evidence:
`artifacts/windows-phase2d/native-full-metadata-inventory.json` and
`artifacts/windows-phase2d/native-observation-summary.json`.
No metadata has been stripped to force acceptance.

Crucially, native cleanliness is **not** proof that Windows cached metadata
cannot be stale on arbitrary LFS 2.0 inputs. No demonstrably safe LFS 2.0
writing subset has yet been established; current writer refusal remains intact.
No Windows-authored
five-operation Save As/native-output reopen, modern-log writing or required
Windows-derived stack matrix is claimed. These remain concrete 2D.3-2D.5
blockers, separately from the unavailable ARM64 gate.

### Complete local verification and cross-target packaging

`scripts/verify.sh` passes on current application source: **1136 passed, one
Windows-only BCL skip on Linux, 1137 total**. Breakdown: 871 storage passes/
one skip, 156 Services, 68 UI, 27 dependency and 14 foundation. Locked restore,
Release build (**zero warnings/errors**), formatting, UI resources, AppRun
contracts and all five authoritative reference-copy comparisons pass.
Log: `artifacts/phase2d-verify.log`. The actual Windows codec/reference execution
above resolves that platform-specific test separately; do not rewrite the
Linux skip as a pass or add targeted counts to the full-suite total.

`scripts/publish.sh all` also passes: all then-configured self-contained RIDs, new local
Linux x64 AppImage and legal notices retained. Log:
`artifacts/phase2d-publish.log`. Subsequent harness/documentation changes are
outside application publish inputs. No versioned release assets, GitHub
publication or physical compatibility claims follow. ARM64 remains blocked,
Windows-native edited outputs remain unqualified.

### Broader Windows runtime execution: failures retained, not a platform pass

A subsequent actual Windows standard-user selection ran existing generated/
golden-derived tests, not the Windows-authored source: **storage 400 passed /
57 failed (457 total)** and **Services 153 passed / 3 failed (156 total)**,
zero skips. Exact finite commands, per-class counters, TRX names and tool
inventory: `artifacts/windows-phase2d/windows-qualification-summary.json`.
These are separate selections, not a complete Windows solution run or counts
to add to the Linux full-suite result.

The 60 failures classify as:
- **52 missing independent-tool failures:** `qemu-img` and NTFS authoring/
  checking tools are absent on Windows; the dependent matrix rows are not
  qualified or skipped-as-success.
- **Three application log-contention failures:** parallel service instances
  append to shared `explorer.jsonl` through an instance-local lock in
  `ExplorerLog`. This is not the SSH/native harness.
- **Four direct-commit sharing failures:** the managed-NTFS Services case and
  all three raw-layout direct-commit cases cannot obtain read/write source
  locking while `FileBlockDevice` retains `FileShare.Read`. The Services
  failure reproduces in isolation. Failure is before commit mutation, but
  tests abort before final hash assertions; unchanged source hashes on these
  failed tests were **not independently established**.
- **One preference exception mismatch:** isolated
  `ReplacementFailureIsReportedAndReplacementFileIsCleaned` expects
  `IOException`, receives `UnauthorizedAccessException`; its cleanup assertion
  is not reached.

Safety/refusal **55/55** and codec **271/271** pass in this broader selection.
Its reader failures are the shared logger contention; isolated current reader
classes pass **17/17** and **14/14**, and the earlier complete focused **66/66**
Windows reader run remains genuine. Do not replace the broader failure result
with those isolated passes or infer concurrent Windows reader robustness.

These newly exposed retained runtime defects were not caused by changes to
those paths in 2D and were not fixed during the owner-selected closeout.
Windows direct-commit, concurrent service logging and preference failure/
cleanup behavior remain concrete verification blockers, alongside native
TxF/CLFS and LFS 2.0 safety, missing native tools and unavailable ARM64.
Historical Linux/backend evidence is retained, not generalized to Windows.

Owner selected **finish the verified bounded closeout and STOP for review**
after the deeper transaction-safety investigation found no defensible offline
quiescence/update rule. Required Windows-writing/modern-log tasks remain in
1.0 and incomplete; they are not waived or silently moved to another phase.
No further remote/native probes, implementation, publication or remaining
Phase 3 work follows this stop.

### Owner-resumed Windows runtime corrective pass

After reviewing those retained failures, the owner explicitly accepted the
recommended runtime repairs and portable verification work. A fresh read-only
preflight found the existing standard-user Windows/tooling/generated-media
permissions sufficient; no new owner-media access or privilege was needed.
Checklist 2D.10 records this bounded resumption. The preceding failed Windows
runs and completed Linux/publish runs remain historical, not rerun results.

The corrective pass repairs commit source-handle ownership and locking, shared
log append/rotation, and verifies the real preference save-error/cleanup
contract. Managed Windows writer workflows must be distinguished from Linux
native-tool authoring/checking, with independently verified hashes linking the
same generated inputs and resulting outputs. No check is silently discarded.
TxF/CLFS, modern-log/cache-safety and unavailable ARM64 requirements remain
required and incomplete. Remaining Phase 3 features, privileged native steps,
owner-media operations and publication are not resumed by this authorization.

The first corrective Windows rerun passed **392/392** concurrent reader/
version/list/security/safety cases and **161/174** Services cases. Its 13
failures reached the next previously unexecuted commit boundary:
`File.Move(overwrite)` returns Win32 5 while the destination's write-excluding
handle remains open. These are retained in
`artifacts/writer-handoff-final-transfer/windows-current-attempt-summary.json`,
not replaced by isolated passing results. Logging and preference regressions
passed in that run.

Six disposable-text BCL characterizations confirmed that `File.Move` fails
with the held handle, while `File.Replace` succeeds. `File.Replace` was not
adopted: Microsoft's [documented 1176/1177 partial-failure states and stream/
attribute merging](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)
do not match this pipeline's single namespace-finalize contract.
The bounded Windows helper instead uses one
[`FileRenameInfoEx` replace/POSIX operation](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information),
retaining the source write lock and old-handle identity throughout. Ordinary
and Unicode/space native controls pass; deny-delete/read-only controls refuse
with old source/stage intact. The buffer includes a trailing UTF-16 NUL (not
counted in filename length); initial scratch-only missing-terminator evidence
is preserved, not a product pass. The production helper included that terminator
from its first implementation and preserves exact UTF-16 pathname code units.

Actual Windows focused corrective execution then passed **31/31**, including
the production atomic helper, old-handle identity, invalid staging/directory
refusals, complete commit lifecycle, concurrent logging and preference cleanup.
Final full Windows/matrix/native-return and Linux verification remain separate
until their exact manifests and results are recorded. The earlier Linux
corrective full run was cancelled as superseded after its 174 Services cases
passed; its storage suite was incomplete, so it is not a full-pass snapshot.
All then-configured final-source self-contained publishes and the local Linux AppImage
have been refreshed successfully (`artifacts/phase2d-corrective-publish.log`);
this is not physical compatibility or publication.

An additional Linux isolation check then found that an unprefixed .NET named
mutex is session-local. A writer launched in a different session could bypass
the log gate even though same-session workers passed. The corrected logger uses
`Global\` on every OS (the computed Windows name is unchanged). The actual log
regression fails against the old prefix and passes after correction, within
**32/32** focused Linux runtime cases. Isolation is established before CLR startup
with Linux `setsid --wait`: calling `setsid` from managed `Main` produced an
invalid negative control because the runtime retained its initial namespace.
Evidence: `artifacts/phase2d-corrective-tests/phase2d-cross-session-before-fix.trx`
and `phase2d-final-session-runtime.trx`. Full current-source qualification is
repeated rather than relabeling prior writer/native results as this snapshot.

## PMFX feasibility evidence and Phase 6 planning, 2026-10-17

Owner approved planning read-only DiskGenius PMFX browsing/extraction under
checklist **6.11**, not starting Phase 6. The
[durable research and qualification plan](pmfx-read-only-plan.md) records
observed metadata, bounded decoding, independent-reference gaps and licensing.
No production reader, fixture import, dependency change or new platform
compatibility follows.
Phase 6 must actually implement and qualify direct PMFX opening, browsing and
extraction inside SectorWorks before completion; this research entry and a
future implementation plan do not satisfy that gate.

Scratch-only Linux research reconstructed one expressly authorized small
FAT16 derivative: existing services and independent mtools compared all
400 ordinary files successfully. Temporary owner-data outputs were removed.
The follow-up examined ten originals in place, without copies/raw expansion/
extracted contents: 77,431 complete checksum-valid zlib payloads, 2,336
verbatim-sized payloads and twelve reassembled single-fragment chains; 36,632
absent entries were interpreted as zeros without vendor-restore qualification.
Bounded metadata showed five FAT16/five FAT32, no NTFS. Full SHA-256/size/
identity/mtime baselines matched after all ten probes. The in-place pass did
not recursively qualify filesystem contents/health or bootability.

The strict .NET decoder integration, independent DiskGenius-generated references,
production workflows, read-only enforcement and physical Windows/Linux/ARM64
qualification remain pending. No new build/test/
publish run or repository code change occurred in this documentation task.

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

- Build: `./scripts/build.sh` succeeds, 0 warnings/0 errors, after correcting
  a platform-specific lock analyzer error in the historical implementation.
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
  this is a backend-only delivery.
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

All then-configured self-contained development publishes and the local Linux x64 AppImage
were refreshed successfully; architecture/legal checks passed. Evidence:
`artifacts/iso-reader-publish.log`. Solution-wide formatting passes.
Final solution build passes with zero warnings/errors
(`artifacts/iso-reader-build.log`); the published Linux executable and
extract-and-run AppImage both pass `--verify-ui` resource checks without
starting a desktop. All five authoritative reference-copy pairs match.
Versioned releases and GitHub publication are untouched. Cross-publishing is
not physical Windows/ARM64 or new GUI qualification.

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
verification and Phase 3 writer/commit safety remain required for 1.0. Required **2B.6** current
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

The final repeat **`./scripts/publish.sh all` passed on latest source** for all then-configured self-contained RID targets and
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
open. No Phase 3 or release is authorized.
See [planned reader ledger](support-matrix.md#phase-2c-ntfs-reader-qualification-plan).

## Local artifact cleanup, 2026-10-15

Owner-authorized cleanup removed reproducible project `bin`/`obj` trees,
byte-identical release-download checks, empty test/inspection directories and
superseded pre-freeze Phase 2B synthetic image/conversion files. Earlier-run
screenshots, small inputs and logs remain; final FAT
`artifacts/phase2b-release-x11-<variant>/` and managed NTFS
`artifacts/ntfs-managed-x11-<ntfs-variant>/` evidence remains intact.
All then-configured current publish trees, AppImage, versioned release assets, immutable
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
modern-log gaps below remain incomplete; Windows ARM64 evidence is
unchanged. No Phase 3 or recurring automation is authorized.

## Managed NTFS repair: current qualification and remaining gaps

Historical Phase 2B snapshot: the heading is retained for existing links, but
the results and unexecuted-Windows statements below describe that earlier source.
The final corrective summary at the top supersedes those runtime/reference
gaps, not the unresolved native Windows-authored TxF/CLFS or LFS 2.0 requirements.

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
comparisons pass. All then-configured final self-contained publishes and the AppImage
prototype pass; target architecture headers, repaired/original assembly
presence, matching legal inventory and owner/test-media exclusion are checked.
Published Linux x64 and both AppImage FUSE/extraction-and-run resource checks pass.

Six actual Mint 22 x64/X11 workflows pass for NTFS filesystem-only raw,
primary-MBR raw, fixed/dynamic VHD and fixed/dynamic VHDX. They use newly
`mkntfs`/`ntfscp`-authored disposable media, rendered commands, all five edits,
history/review, staged preview/extraction, Save As/reopen, cancelled close and
discard. Each source hash remains unchanged. Independent qemu conversion,
bounded `ntfsfix -n`, `ntfsinfo`, `ntfscluster`, `ntfscat` and exact comparison
against the staged replacement bytes pass for all then-configured outputs.
Evidence and app-window-only screenshots are local under
`artifacts/ntfs-managed-x11-<ntfs-variant>/`; owner media is not used.

**Phase 2B remains partial.** Actual Windows-authored NTFS fixtures and Windows
security-reference execution have not yet been prepared/run, not passed.
A Windows x64 SSH host is now accessible as recorded above. Clean LFS 1.1 has
synthetic structural coverage only; LFS 2.0/cached-metadata safety remains
blocked. This is not a claim that normal Windows volumes are writable, nor an
owner-approved NTFS read-only scope reduction for 1.0.
Windows/ARM64 runtime remains unverified at this historical snapshot. Cross-publish evidence is build evidence only. Formatting, replay/reset,
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
All then-configured final self-contained cross-publishes and AppImage packaging pass;
target architecture headers, legal files and owner-sample exclusion
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

The historical cross-target build is not physical compatibility, signing,
Wayland, full-distro or production qualification. Owner samples remain ignored,
hash-identical and absent from packages.
The earlier date-failure regression expected a later fingerprint error; stricter
source/output qualification now catches invalid dates first. Its expectation
was updated to require that structural/timestamp failure, not weakened to
accept output. Four additional regressions prove valid-but-changed untouched
attributes/dates cannot publish despite matching trees/content hashes.

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

All then-configured existing publish folders were refreshed with the final code and legal
notices. The published Linux x64 executable also passes `--verify-ui`.
No new physical compatibility claim is made.

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
the hardening phase (now Phase 7). Existing SquashFS/desktop tools were present; appimagetool was absent.
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
format/resource checks and synchronized specifications. All then-configured self-contained
publish folders were refreshed successfully with the fix.

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
text preview; Hex View remains available. All then-configured refined cross-publishes pass.

Skia-backed UI tests exercise 100/125/150/200% scaling, twenty vector icons at
16/18/20/24 logical pixels, a 1000x640 minimum window and text contrast >=4.5:1.
Linux/X11 physical smoke additionally used process-local 125/150/200% scales.
A 50,000-entry grid realizes at most 100 rows. These results are not Windows, Wayland or ARM64 validation.

Native picker operation, pointer splitter dragging, clipboard integration and
exhaustive assistive-technology testing require actual desktop verification.
The verified Windows MCP now permits autonomous ordinary interaction tests;
particular assistive-tool/manual observations still need their own evidence. Smoke
injects extraction destinations rather than operating native dialogs. The owner
reports above add Windows x64 and Raspberry Pi 500 runtime evidence, but do not
complete that detailed review. Windows ARM64, explicit Wayland coverage, the full Linux distribution matrix and Raspberry Pi
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

At foundation completion there was no Avalonia window. Those results did not verify
desktop startup, image browsing/extraction, minimum OS versions, dialogs,
clipboard, DPI, audio, packaging or signing. Raspberry Pi 5,
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

| Dependency | Linux x64 / ARM64 | Windows x64 / ARM64 |
| --- | --- | --- |
| SkiaSharp 3.119.4 | ELF assets for both | PE assets for both |
| HarfBuzzSharp 8.3.1.3 | ELF assets for both | PE assets for both |
| Avalonia ANGLE 2.1.27548.20260419 | Not used | PE assets for both |

The .NET runtime/apphost is likewise target-specific ELF or PE with the
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
All then-configured RIDs solution-wide and restoring their committed lock graph without a
single-RID override. All then-configured subsequent publishes passed; scripts do not bypass
locked restore to hide failures.

## Resolved audio scope

On 2026-10-13 the owner confirmed that the detailed checklist governs audio scope.
BIN/CUE support and functional CD-DA playback belong to Phase 5. Phase 1 reserves
the optical/audio structure and pane tabs without implementing these workflows.
The UI specification and supplemental guide were corrected, with repository
documentation copies kept synchronized. The optical/audio mockup remains the
approved later-phase visual direction. Phase 1 was subsequently authorized on
2026-10-14; it reserves those panes without implementing playback.
