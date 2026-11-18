# Foundation storage dependency evaluation

## Phase 5 optical and audio dependencies

CUE parsing, sector mapping and staged-set validation are managed code over
the existing reader/ISO builder. Playback dynamically uses Windows system
waveform audio and Linux system ALSA (`libasound.so.2`); SectorWorks does not
redistribute either audio library. This avoids shipping a native engine or
new NuGet packages. Linux desktop audio requires a working default ALSA
endpoint; this is qualified on the tested Linux x64/PipeWire host, not on
every distribution or native ARM64. See the [backend checks](phase5-bin-cue.md#playback-backends).

## Phase 4 optical implementation

No new production package or native dependency. The pinned DiscUtils ISO
reader remains the bounded fallback for supported non-rebuild layouts. A
separate managed parser/rebuilder qualifies ISO path tables, directories,
Rock Ridge and bounded El Torito layouts; UDF 1.02 uses bounded ROM-profile
descriptors and recorded extents. Rock Ridge and UDF preservation are not
inferred from the pinned builder. Development-only genisoimage/isoinfo,
7-Zip and QEMU evidence is linked in the [Phase 4 report](phase4-optical-rebuild.md).

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


## November 16 bounded writer and native evidence

**Resumed closeout update:** corrected elevated R2 completed two full133103616-
byte captures and detached, with unchanged seed. Both mounted pages are2.0;
both after-lock pages1.1. Microsoft documents
[FSCTL_LOCK_VOLUME](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_lock_volume)
as flushing caches and NTFS treating the volume as dismounted. The live capture
is sequential; the exclusive flushed capture already changed version and cannot
qualify unchanged-log2.0 writing. There are183 changed sectors mounted-to-locked
and189 locked-to-detached, all mapped to observed allocated metadata streams;
ordinary payload/bitmap/mirror agreement does not establish metadata quiescence.

The fresh generated NoTxF R2 used documented format-time TXF:disable and
NoRepairLogs, not a host policy change. It has no observed TxF log/TOPS/TXF_DATA,
but retains RmMetadata/Repair:Config/Deleted. Native read-only CHKDSK succeeds;
current product still refuses writes. These switches do not specify portable
offline metadata preservation/update rules. R1 full-format refusal on a thin VHD
remains failed evidence. The read-only
[snapshot transaction status API](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_txfs_transaction_active)
does not apply to arbitrary detached raw files or define writer updates.
The both-client/prepared/in-doubt/TOPS/TXF_DATA, repair/deleted and independent
LFS2/cache contracts remain missing. No further identical native probe or
speculative writer expansion is justified. Details and primary references are
in the current review and resumed native interpretation report. Historical
pending-run/UAC-absence descriptions below retain their original scope.

The [continuous assignment](sectorworks_phase_2d_and_phase_3_autonomous_sol_prompt.txt)
authorizes the remaining bounded Phase 3 writer/UI/recovery work. The isolated
MIT managed backend retains its replaceable boundary and unchanged signed reader
packages. New repairs preserve SI48/SI72 and legacy/new FILE creation layouts,
correct SI offset and quota/USN serialization, retain allocation identity when
reinitializing new MFT records, and prevent a synthesized directory attribute
from entering original SI bytes during timestamp restoration. The generated
repair patch/manifest are reproducible; `record-repairs.py --verify` passes.

Independent validation resolves bounded logical streams separately from physical
extension ownership and allocation. Retained SI and original stored security
bytes are compared without canonicalizing the input. Out-of-range FILETIMEs
are refused before the adapter can normalize them. Exact subset, growth/failure
coverage and native output results are recorded in the [review](phase2d-phase3-review.md).
Formatting, dirty/ambiguous logs, unsupported attributes and all modern Windows
transaction/log writer gates remain disabled.

A new standard-user route queries complete detached generated CLFS copies using
the documented `%BLF%\` relative-container prefix. Both TxF and KTM clients are
observed; 16 restart/forward payload observations on retained Ordinary/Committed/
RolledBack controls match the original raw bytes. Those are fresh observations
of retained authoring, not fresh authoring or TxF recovery proof. Native Windows
automatically adds authentication suffixes/`.cnpf` files to disposable copies;
complete before/after files are retained, never stripped, patched or propagated
back. R3–R8 access-denial/privilege-restoration evidence remains unchanged.

The failed generated LFS capture contains complete mounted log-stream data and
both actual RSTR 2.0 pages; detached output contains RSTR 1.1 pages. Its volume
capture is short by 4096 bytes and lacks a successful lock/flush comparison.
The corrected harness checks the exact opened volume's disk extent and enables
bounded extended DASD tail reads. Standard-token compilation/early refusal pass;
its new elevated run is deferred while the owner cannot approve UAC. Cache safety,
prepared/in-doubt controls and persistent both-client/TOPS/`$TXF_DATA` update rules
remain unproven. Native transport, checksums, empty state and detached version
labels never substitute for those rules.

## Phase 2D bounded qualification authority, 2026-10-17

Owner authorized closeout of retained Phase 2B current-Windows reference/media
and LFS 2.0/cache-safety gaps under 2D.2-2D.5, not dependency replacement,
complete permissions-editor development or exhaustive Windows certification.
Execute focused Windows BCL codec comparison separately from actual Windows-
authored disposable NTFS edit/native-reopen checks. Preserve allocation,
security/metadata validation, signed packages and the isolated managed repair.
Only a demonstrably safe bounded clean modern-log subset may qualify; dirty,
ambiguous/recovery-needed logs remain refused without replay/reset/normalization
or simply deleting validator gates. Infeasibility requires an explicit blocker/
owner decision, not a read-only scope reduction.

Current status after the owner-resumed corrective pass: the Windows BCL codec
reference passed 271/271, bounded reader rows passed 66/66 on both Linux and
Windows x64, and Tower source/content checks are complete within their declared
scope. Retained generated-image Windows writer/runtime defects are fixed and
verified. Windows-authored edited-output/TxF/CLFS and LFS 2.0/cache-safety gates
remain blocked; current ARM64, GUI/platform and review gates remain incomplete.
The runtime corrective batch stopped for owner review, not full Phase 2D completion.
On October 21 the owner separately resumed the retained TxF/CLFS compatibility
work; its first bounded investigation increment is recorded below. See
[final corrective evidence](platform-status.md#final-global-session-corrective-snapshot).

The bounded investigation has a concrete cache-safety obstacle, not merely a
version-number check. Pinned ntfs-3g 2022.10.3
[`ntfs_volume_check_logfile`](https://github.com/tuxera/ntfs-3g/blob/2022.10.3/libntfs-3g/volume.c#L699-L717)
explicitly refuses LFS 2.0 even after its clean-log checks, because Windows may
retain metadata for fast restart/hibernation or the volume may have been
abruptly detached.
[`ntfsrecover`](https://github.com/tuxera/ntfs-3g/blob/2022.10.3/ntfsprogs/ntfsrecover.c#L2979-L2987)
likewise calls out fast-restart data-loss risk. Its bypass/reset options are
not an approved SectorWorks strategy. Linux v6.12
[`fslog.c`](https://github.com/torvalds/linux/blob/v6.12/fs/ntfs3/fslog.c#L1313-L1325)
also distinguishes the modern 34-page special area from the older four-page
layout; accepting a version-field mutation under LFS 1.1 checks cannot qualify
modern tail/checkpoint semantics. These are inspected public safety references,
not imported source or proof that every clean LFS 2.0 image is unsafe.
Actual native detached-media evidence and a demonstrable cache-absence/safe
checkpoint criterion remain necessary before broadening capabilities.

Further bounded source inspection distinguishes actual modern-log support
from a clean-volume validator. Linux v6.12
[`last_log_lsn`](https://github.com/torvalds/linux/blob/v6.12/fs/ntfs3/fslog.c#L1629-L1715)
examines paired modern tail regions starting at pages 2 and 18, not the
older two-tail-page layout. Its restart path reads checkpoint transaction and
dirty-page tables and scans subsequent records before recovery decisions.
At [the end of recovery](https://github.com/torvalds/linux/blob/v6.12/fs/ntfs3/fslog.c#L5139-L5181),
the writable path emits LFS 1.1 restart pages with no active client. Successful
native mounting through that recovery/write path would therefore not qualify
SectorWorks' required **unchanged-log, no-replay/reset** behavior. No kernel
mount or recovery experiment was used as a shortcut.

The actual Windows 11 Home disposable fixture gives a useful distinction:
`fsutil` reports LFS 2.0 while writable-attached, then 1.1 on the cleanly
detached/read-only-reopened image; raw detached pages confirm 1.1. The exact
transition was not captured in raw bytes, so do not claim a proven transition
instant or that Windows Home cannot use 2.0. All post-detach image hashes agree.
No additional Windows machine or unsafe shutdown is required by this evidence.

Independent current-Windows writer blockers are now concrete: native
`$Extend/$Deleted` and `$RmMetadata`, root/resource-directory `$TXF_DATA`,
named `$Tops:$T`/`$Repair:$Config` streams and initialized CLFS transaction
logs. Empty transaction directories and matching opaque-stream hashes alone
do not establish inactive transactions. No metadata was removed, reset,
normalized or exempted from validation. Establishing a bounded offline
TxF/CLFS inactivity/preservation rule remains an investigation, not an
enabled writing subset. See [native evidence](platform-status.md#phase-2d-preflight-and-closeout-2026-10-17).

### Bounded offline transaction-safety investigation outcome

The focused public-reference investigation found **no sufficient offline
inactivity/no-recoverable-transactions predicate** for the observed TxF/CLFS
metadata. This is a concrete qualification blocker, not proof that a smaller
safe verifier can never exist or that every such volume is corrupt.

Microsoft documents that metadata-only undo information can reside entirely
in the transaction log, including for moves/renames/appends/attribute changes
([pinned performance documentation](https://github.com/MicrosoftDocs/win32/blob/ca8274decead49403d70ceeed4af8d4ca74eaf11/desktop-src/FileIO/performance-considerations-for-transactional-ntfs.md#L15-L24)).
Thus empty `$Txf`/`$Deleted` and all-zero `$Tops:$T` cannot establish quiescence.
Live [RM information](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-txfs_query_rm_information)
and [transaction-list queries](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_txfs_list_transactions)
describe current runtime state, not a detached-byte recovery predicate.
[KTM recovery](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/handling-recovery-operations)
reconstructs transactions/enlistments/resource managers and handles commit,
rollback and indoubt outcomes. An inactive runtime manager is not documented
as absence of persistent recovery obligations.

Pinned [libfsntfs format research](https://github.com/libyal/libfsntfs/blob/faaa031d0f779578559d5c025c9583d103702bcc/documentation/New%20Technologies%20File%20System%20(NTFS).asciidoc#L2457-L2613)
recognizes TOPS, multiplexed KTM/TxF CLFS stores and `$TXF_DATA` references/LSNs,
but retains unknown/tentative field semantics; recognition is not write
qualification. Microsoft's distinction between nontransacted and transacted
APIs also supplies no offline operation-by-operation guarantee that every
TxF field may remain unchanged during ordinary create/delete/rename/write.

Remaining proof obligations are persistent quiescence for both KTM/TxF clients
(including prepared/indoubt state), versioned TOPS/`$TXF_DATA` semantics, CLFS
copy/active-range selection, directory/file update obligations and secondary
resource-manager/`$Deleted`/`$Repair` interpretation. Opaque byte/hash preservation
alone proves none of these. No parser was imported or capability enabled.

Pinned [ntfs-3g writable mounting](https://github.com/tuxera/ntfs-3g/blob/78414d93613532fd82f3a82aba5d4a1c32898781/libntfs-3g/volume.c#L852-L904)
can make nonresident root `$TXF_DATA` resident by truncate/rewrite, called
[on its writable path](https://github.com/tuxera/ntfs-3g/blob/78414d93613532fd82f3a82aba5d4a1c32898781/libntfs-3g/volume.c#L1308-L1312).
That conditional compatibility repair is not an inactivity test, nor evidence
that it occurs on this fixture. It further prevents treating successful mature
driver mounting as proof of unchanged metadata representation. No such writable
mount, repair or normalization was performed in this investigation.

Retained reader closure is 2D.6-2D.9; authentic historical authoring stays
post-1.0 P1.NTFS and MFT-bootstrap lists unsupported. New legacy/attribute-list
writers stay under remaining Phase 3 3.0/3.7. Earlier statements that Phase 3
has never been authorized are historical: retain its October 16 backend delivery,
without continuing it under 2D. See [closure ledger](support-matrix.md#phase-2d-retained-closure-ledger-2026-10-17).

### Resumed TxF/CLFS structural investigation, 2026-10-21

The owner explicitly resumed the remaining Windows-native transaction
compatibility work after context recovery. This increment investigates the
retained legal generated Windows fixture, adds refusal regressions and leaves
**2D.3-2D.5 incomplete**. It grants no new writer capability, recovery/reset,
owner-media access, remaining Phase 3 work or publication.

[`inspect-clfs.py`](../scripts/inspect-clfs.py) is an opt-in, independent
read-only observer of the known six-descriptor CLFS base-log envelope, outside
the product dependency/publish graph. Input is a separately extracted generated
base stream with an independently recorded SHA256, at most 128 KiB. It bounds
descriptors, blocks, information arrays and UTF-16 names; checks the observed
CRC32 calculation and sector-signature restoration; reports both metadata
copies, logical streams and container numbers/sizes. It refuses damaged copies
instead of silently selecting the other, never follows paths contained in the
log, and verifies source identity/full hash again afterward. It does **not**
validate complete CLFS recovery semantics or choose an authoritative copy.

The format reference is pinned
[libfsclfs research](https://github.com/libyal/libfsclfs/blob/5966065b505f70a2b4439ad8e7ebc546495ee87f/documentation/Common%20Log%20File%20System%20(CLFS).asciidoc),
not a Microsoft on-disk contract. The implementation is original code based on
bounded field observations; no library/source was imported or new dependency
added. The reference remains external. Two discrepancies matter:

- This fixture's stream/container array offsets resolve relative to the
  metadata record, which begins 112 bytes into its block, whereas the research
  table labels them block-relative. Using that label literally lands outside
  the intended information records.
- Raw LSN-like fields contain `0xffffffff00000000`; the reference's special-LSN
  table instead lists `0x00000000ffffffff`. The observer reports raw fields
  without assigning sentinel/clean-state meaning. Likewise the native
  `$TXF_DATA` reference/flag interpretation still needs version-specific proof.

On the retained generated stream, all four occupied base blocks match CRC32
with their checksum field zeroed **before** fixup restoration. The two metadata
copies at offsets **2048/33280** have raw dump counters **19/20**, the same store
GUID, streams **0 physical / 1 TxfLog / 2 KtmLog**, and two **2097152-byte**
containers with observed physical/logical IDs **0/0 and 1/1**. The counter is
reported, not used to select a recoverable state. Equal base/flushed fields do
not establish absence of client recovery obligations. These are observations
on one generated Windows build, not generalized CLFS compatibility.

`test-inspect-clfs.py` exercises synthetic normal/changed copies, both-copy
CRC failure, torn signatures, descriptor/array/record/name bounds, overlapping
records, source identity/hash changes and linked/nonregular files. Six added
`NtfsWriteValidationTests` cases inject exact generated 56-byte `$TXF_DATA`
values or zero-valued metadata into disposable ordinary goldens. They retain
readable sentinel content while refusing raw writer qualification, service
staging/Save As/commit and output publication, with full source hashes and
handle release. They qualify refusal, never TxF writing.

The next unresolved work is **both-client persistent recovery interpretation**:
container/owner-page mapping, the TxF and KTM restart/checkpoint/transaction
records, prepared/indoubt state and copy/active-range consistency. This needs
independently known native transaction controls (ordinary operations, committed
and rolled-back metadata-only transactions), followed by versioned
TOPS/`$TXF_DATA` update/preservation rules. Native controls requiring attachment
must use reviewed finite owner-launched scripts on new disposable virtual files;
the existing standard SSH account must remain unelevated. A mounted runtime
query alone cannot certify detached bytes. Clean detached LFS 1.1 evidence does
not close the separate LFS 2.0/cache-safety requirement. Actual edited-output
and required-stack/native-reopen qualification follow only after a defensible
acceptance/update rule exists. No such rule has passed yet.

The prepared [`windows-native-txf-controls.ps1`](../scripts/windows-native-txf-controls.ps1)
provides the three bounded rename controls against copies of the exact sealed
generated seed. Actual standard-user Windows validation passes PS5 parsing,
API-declaration compilation and refusal before privileged work. At the initial
handoff the controls remained unexecuted; [local owner-launch instructions](platform-status.md#prepared-native-controls-local-owner-launch-required)
include the transferred script/helper hashes. No elevated SSH permission is
needed or requested. Successful native control creation would still be input
evidence for the remaining offline investigation, not product interoperability.

The owner subsequently executed all three controls successfully. Exact output
hashes, exclusive standard-account reads and **17 independent Linux native
namespace/content/stream checks** now pass. Both active native transactions
reported TOPS **100% free** while reporting **one running transaction**; their
detached `$Tops:$T` stays all-zero. Committed and rolled-back renames leave
changed root and new ordinary-file `$TXF_DATA`, independently byte-hash checked.
This supplies the planned ordinary/committed/rolled-back contrast corpus and
actual evidence against a free-TOPS inactivity shortcut. It does not establish
prepared/indoubt handling, persistent recovery state or offline update rules.
The full [native-control evidence](platform-status.md#owner-executed-native-txf-controls-2026-10-21)
retains attached LFS 2.0 versus detached raw 1.1, both physical CLFS copies and
the unchanged seed. No writer acceptance or log reset/replay is introduced.

The owner then authorized continuing all three proposed investigation,
acceptance/update-rule and conditional writer-verification steps without
intermediate review stops. The conditional step still requires a defensible
rule; this is not permission to infer quiescence from the contrast corpus.

#### Container records and native read oracle preparation

The observer now also accepts `--container` for **only** the observed,
unrecycled logical-container-0 prefix of the generated **2 MiB** containers.
It bounds physical blocks/records, restores sector tails, checks record lengths,
alignment, stream-local virtual LSN progression and last-record markers, and
reports opaque payload bytes/hashes and physical restart candidates. It rejects
gaps with later nonzero data, owner-page/split/recycled layouts, unsupported
continuations, malformed signatures and exhausted budgets. It never selects
an active base copy or converts an observation into a product writer predicate.
The container checksum field is **zero** in this corpus: sector signatures
are not a checksum, and successful decoding is not complete CLFS integrity
validation. A regression explicitly changes opaque payload bytes without
claiming checksum detection.

Six additional independent `ntfscat` extractions of MFT **34/35** match the
earlier raw inventory hashes, with no native warning/error. The first containers
have **4/9/8 blocks and 5/15/13 records** for Ordinary/Committed/RolledBack;
all second containers are zero. The last physical TxF restart candidates have
LSNs **0x600/0x1600/0x1400**, each with a **112-byte** client payload. KTM
candidates have LSNs **0/0x1401/0** and the same **32-byte** payload hash.
Raw base-stream field **80** matches these candidates in both metadata copies
for all three controls. This is an empirical correlation, not authoritative
copy/range selection, client checkpoint interpretation or prepared/indoubt
qualification. Historical transaction records remain physically present after
completion; enumerating all nonzero records as live would be incorrect.
The synthetic observer suite now passes **17 tests**, separate from the earlier
unchanged **1184-pass/one-skip** .NET verification snapshot.

The next independent oracle uses Microsoft's
[read-only log open](https://learn.microsoft.com/en-us/windows/win32/api/clfsw32/nf-clfsw32-createlogfile),
[CLFS information](https://learn.microsoft.com/en-us/windows/win32/api/clfs/ns-clfs-cls_information),
[restart reading](https://learn.microsoft.com/en-us/windows/win32/api/clfsw32/nf-clfsw32-readlogrestartarea)
and [record scanning](https://learn.microsoft.com/en-us/windows/win32/api/clfsw32/nf-clfsw32-readlogrecord)
APIs, plus [file TxF metadata queries](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_txfs_get_metadata_info).
These define runtime queries, not an offline TxF payload/update contract.
[`windows-native-clfs-oracle.ps1`](../scripts/windows-native-clfs-oracle.ps1)
requests only existing read handles and captures returned LSNs, restart/record
payloads, native errors and file transaction identifiers/states. Native context
cleanup is required; Microsoft's
[marshalling-area cleanup](https://learn.microsoft.com/en-us/windows/win32/api/clfsw32/nf-clfsw32-deletelogmarshallingarea)
may flush pending I/O, so these live observations must not be described as
immutable detached-source inspection. They run only inside fresh generated
owner-local controls. No append, reset, repair or write-access retry exists.

The opt-in R4 mode of the existing native control harness records the actual
KTM transaction GUID and launches the observer in **30-second** child processes
before/during/after each applicable control. The helper independently checks
that the drive identifies the exact parent-owned virtual disk/partition.
Its budgets are **64 KiB** marshalling blocks, two read buffers, four restart
areas, **128** forward records, **16 KiB** per payload and **64 KiB** aggregate
forward payload per stream. API access/sharing errors and terminal Win32 codes
remain explicit evidence; they do not authorize altered ACLs or imply a
complete scan. Prepared/indoubt controls and offline update semantics remain
unqualified even if all native reads succeed.

[`windows-native-txf-oracle-batch.ps1`](../scripts/windows-native-txf-oracle-batch.ps1)
provides one owner-local launch for all three fresh R4 controls, preserving R3.
Actual standard-account Windows PS5 parsing, both C# declaration compilations,
synthetic native-payload copy/hash/LSN/budget checks, all three administrator
guards and four transferred script hashes pass. This validation calls no
native log/disk methods. An initial large-stdin transport timeout and file
execution-policy refusal are retained as validation failures; final validation
uses transferred-file parsing and in-memory guard execution without changing
account or machine policy. Owner-launched child execution policy remains
process-local, as in the earlier controls. At that preparation snapshot native
R4 execution was **pending**; subsequent results and corrections follow below.
[launch/evidence details](platform-status.md#native-clfs-read-oracle-r4-owner-local-launch)
are separate from successful preparation.

#### Native R4 results and corrected oracle

The owner executed all three R4 controls successfully on October 21. All three
exact detached VHDs and the unchanged Native-R2 seed match subsequent exclusive
standard-account read hashes; independent Linux checks of the exact downloads
pass. However, all **16 CLFS opens failed with Win32 error 2**: the harness
incorrectly used `TxfLog` where the independently inventoried native filename is
**`$TxfLog.blf`**. Control-authoring success never completed the missing oracle.
The exact executed four-script snapshot and all failed-query reports are
retained, not overwritten or retroactively described as successful reads.

The file-metadata side of the oracle did succeed. In both active controls the
native file query reports the exact KTM GUID recorded by `GetTransactionId`,
file ID low part **1**, state **1**, and last LSN **0x603**. The same GUID occurs
in the raw TxF records at payload offset **32**, including record **0x603**;
the committed control also contains it in KTM records. After completion the
file query reports state **0** and a zero locking GUID, while retaining file ID
1 and last LSN **0x603** for commit / **0x1000** for rollback. Those latter two
values match raw `$TXF_DATA` offsets **16/24** in this corpus. These are bounded
correlations, not a complete versioned attribute schema or an offline predicate.
The root query reports state 0 even while the child transaction is active;
checking the root alone would therefore miss the known active control.

The corrected helper retains every literal `$` and uses a checked drive-prefix
constructor. A separate ABI review against the
[Microsoft SDK declarations](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/shared/clfs.h)
also corrected record types to **byte by reference** and the client filter to
**3**. An older third-party header represented these differently. The native
read methods use the SDK ABI, not the raw container header's 32-bit type word.

[`windows-clfs-oracle-selftest.ps1`](../scripts/windows-clfs-oracle-selftest.ps1)
then passed as the actual **standard** Windows account, inside a finite
**45-second** child. It creates only a fresh user-local synthetic multiplexed
log with two containers and two clients, writes known data/restart payloads
through CLFS APIs, closes the writers and exercises the corrected read helper.
Both clients return all **eight** exact known records, four latest/previous
restart observations, their writer-returned LSNs and bytes, normal terminal
codes **38** (forward EOF) / **6618** (previous-restart start), and clean native
reader cleanup. The real TxF path constructor and byte-reference ABI also pass
regression checks. This creates no TxF transaction, attaches no disk and
modifies no pre-existing log. It is a positive API transport test, not TxF
checkpoint/prepared/indoubt qualification.

Independently downloaded logical prefixes reproduce all eight native records'
LSNs, payload bytes/hashes, low data/restart type bits and previous/undo LSNs.
Both base copies' raw fields **56/72/80** match the native base/last/restart LSNs
in these controls. Crucially, raw field **64** differs from the native
`LastFlushedLsn`: the older research label must not be used as that API field.
The observer supports a bounded nonzero first virtual LSN, as seen when the
second generic client first appends at physical offset **0x800**. It still
does not select an active copy, authenticate a file or infer persistent safety.

The generic native files exceed their reported logical sizes: BLF **73728**
versus **65536** bytes; each container **2232320** versus **2097152** bytes.
These extra regions are consistent with Microsoft's
[CLFS authentication](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/clfs-authentication)
and [stable-storage documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/clfs-stable-storage).
Authentication can use system-specific keys and additional recovery files;
portable acceptance must not assume that ordinary CRC/fixup checks cover it.
The suffixes themselves are **not decoded or authenticated** here. Full source
hashes and the exact unexplained lengths are retained. Only explicitly labeled
diagnostic copies of the native-reported logical prefixes are compared; the
complete extended files remain refused by the bounded raw observer. Base-log
descriptor coverage now rejects trailing bytes instead of silently ignoring
them, including an all-zero suffix. No authentication policy, code, signature
or recovery file is changed, disabled or regenerated to qualify an input.

The diagnostic suite now passes **19 Python tests**. The previously completed
**1184-pass/one-skip** .NET run remains its original snapshot; no product
runtime code changed or new full-solution execution is claimed here.

The new **R5** owner-local batch preserves R3/R4 roots and uses the corrected
reader. Its final coverage check requires two successful stream observations
at every required stage; actual standard-account regressions reject the real
R4 missing-query reports, accept known complete 16-query coverage and reject
partial coverage. All five transferred scripts parse, both API declarations
compile and all owner-only guards refuse the standard account. The owner then
executed all three R5 controls successfully, but the final coverage assertion
at line 26 correctly failed: **all 16 protected CLFS opens returned Win32
error 5**, even inside that local administrator run. No CLFS information,
restart or record query succeeded. The saved reports do not identify the
specific native access restriction; changing permissions is not a supported
conclusion. Exact VHD/seed exclusive hashes, **26** independent Linux stream/
namespace/payload commands and native file-state/raw metadata comparisons pass.
All three controls detached; retain the single-use evidence and do not rerun.
See [R5 results](platform-status.md#owner-executed-r5-results-protected-log-access-denied).

Read-only API review does not close the gap:
[`TxfLogCreateRangeReadContext`](https://learn.microsoft.com/en-us/windows/win32/api/txfw32/nf-txfw32-txflogcreaterangereadcontext)
and [`TxfLogReadRecords`](https://learn.microsoft.com/en-us/windows/win32/api/txfw32/nf-txfw32-txflogreadrecords)
document replication/redo records, not a complete both-client recovery oracle.
[`TXFS_QUERY_RM_INFORMATION`](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-txfs_query_rm_information)
documents a transaction count at query time, not detached persistent safety.
These are scope findings, not executed alternative probes or a claim that no
future solution exists. No write-access retry, privilege/ACL/authentication
change, kernel driver or recovery/reset/repair was introduced.
The subsequent bounded access investigation finds stored administrator-read
allow ACEs in the generated metadata descriptors, without establishing effective
live access or the specific denial cause. A separate R6 diagnostic now compares
ordinary file opens with CLFS client opens on one fresh generated VHD. Actual
standard-account parser/compilation/guard/coverage tests and **10** positive
file/generic-log observations pass. Subsequent owner R6 execution records all
18 operations: all eight attribute opens succeed, six metadata GENERIC_READ
and both CLFS opens fail with error 5. Native handle IDs match the independent
raw records; seven Linux stream/namespace/content commands and unchanged
source/seed hashes pass. Backup/restore privileges are present but disabled
in that owner token; no privilege or protected ACL changed. See
[R6 executed results](platform-status.md#owner-executed-r6-results-attribute-access-is-not-data-access).

Microsoft's [backup access model](https://learn.microsoft.com/en-us/windows/win32/fileio/file-security-and-access-rights)
supports a candidate test with an existing granted backup privilege enabled.
An explicit opt-in R7 script is prepared for an isolated owner-local child:
before/with-backup opens, conditional existing bounded CLFS reader, restoration
and exact-state verification, then detach/hash the fresh generated image.
Actual standard-account parsing/compilation/five refusal cases pass, without
native adjustment or disk/CLFS calls. Positive activation, restoration and
protected-log access remained untested at that preparation snapshot; neither
the documented mechanism nor a disabled privilege proves causation.
No account privilege grant, restore-privilege activation, ACL/authentication
change or safety-gate expansion is proposed. See
[R7 source, limits and opt-in handoff](platform-status.md#r7-backup-read-candidate-explicit-owner-opt-in-required).
The owner subsequently ran R7: the child failed its raw dictionary-result
checker, while image detach and exclusive output/seed hashes pass. A recorded
`Win32Error` key is not exposed through `PSObject.Properties` on native C#
dictionaries. The failure reproduces as the standard account; no usable
activation/restoration or native backup-read result was captured. The corrected
R8 checker supports both native and JSON rows and preserves partial stages.
Actual user-local native observations and **18** malformed result-shape cases
pass; at preparation time positive backup/recovery/writer outcomes remained
untested. See
[R7 failure](platform-status.md#r7-owner-execution-failed-dictionary-result-check)
and [R8 correction](platform-status.md#corrected-r8-backup-probe-owner-local-launch-required).
The outstanding both-client persistent recovery/update rule still precedes
conditional writer implementation and native edited-output verification.

#### R8 negative access result and remaining proof gaps

The owner then completed R8 successfully. All **36** native access observations
are retained, and backup privilege is positively verified disabled/enabled/
restored in the isolated child. Restore privilege remains disabled; the original
queried token state matches after disposal. Before/with-backup access rows are
identical: **10 successes and eight error-5 denials** per stage. Both protected
CLFS opens remain denied, so the conditional native restart/record reader is
not called. Exact output/seed exclusive hashes, successful handle/raw-record
identities and **seven independent Linux reads** pass. See
[executed R8 evidence](platform-status.md#owner-executed-r8-results-backup-restored-access-still-denied).
This closes the bounded backup-candidate evaluation, without a protected-log
read, causal diagnosis of the restriction or writer qualification.

The following reference checks constrain further work:

- Microsoft's [filesystem privilege documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/privileges)
  describes backup ACL bypass and volume-management operations separately.
  It does not establish that enabling another privilege grants these protected
  log reads. R8 is evidence that backup alone is insufficient for the tested
  open methods, not justification for enabling restore/debug/manage-volume
  privileges, changing ACLs or broadening account permissions.
- The public [TXF_LOG_RECORD_BASE](https://learn.microsoft.com/en-us/windows/win32/api/txfw32/ns-txfw32-txf_log_record_base)
  is an **eight-byte replication header**, with two 16-bit fields and one
  32-bit record length. A bounded comparison of all **81** physical TxF-client
  payloads in the retained R3/R4/R5 observations finds **zero** matching that
  documented record shape: treating their first bytes as this header gives
  record type 0 and a mismatched length. This rejects that direct schema
  substitution; it does not identify the persistent client format or exclude
  a translation performed by the runtime API. Ignored reproduction evidence:
  `public-replication-shape-comparison.json` in the investigation artifacts.
- [TxfLogReadRecords](https://learn.microsoft.com/en-us/windows/win32/api/txfw32/nf-txfw32-txflogreadrecords)
  returns redo/replication records. Microsoft's
  [KTM log-stream model](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/using-log-streams-with-ktm)
  stores transaction-manager state separately from resource-manager data and
  recovers from their restart information. Together these documents do not
  establish a detached both-client quiescence predicate, prepared/indoubt
  interpretation or safe preservation/update of logs after ordinary edits.

Remaining proof obligations are explicit: authoritative active-copy/live-range
selection; versioned TxF and KTM checkpoint/transaction decoding independently
validated against native states; prepared/indoubt refusal/recovery handling;
authentication coverage; and a demonstrably safe offline metadata/log update
or preservation rule. Known ordinary/commit/rollback payload correlations,
zero TOPS, root state 0, successful privilege restoration or a clean detach
do not discharge these obligations. Product edited-output/native reopen and
LFS 2.0/cache-safety qualification remain downstream and incomplete.

No justified replacement native access method or complete persistent format
contract was found in this review. This is the current investigation blocker,
not a claim that compatibility is impossible or a waiver of the 1.0 requirement.
No further owner-local batch is prepared on speculation, and no writer gate,
authentication, account privilege, kernel driver or recovery policy changes.

Reproduction, exact source/stream hashes and current verification are in
[platform evidence](platform-status.md#resumed-txfclfs-investigation-2026-10-21).

Evaluated 2026-10-13 on the available Linux x64 host, SDK 10.0.402 / .NET 10.
Scope: **historical foundation experiments only, before Phase 1**. At that
evaluation point there were no product storage handlers or UI capabilities.
Current reader support is documented in the [support matrix](support-matrix.md);
Phase 2 write qualification is documented in [staged editing](staged-editing.md).
Do not substitute these direct-library experiments for product verification.

## Decision and provenance

### Planned Phase 6 PMFX dependency evaluation, 2026-10-17

The owner added PMFX read-only browsing/extraction under checklist **6.11**,
not implementation now. DiscUtils 0.16.13 has no PMFX handler; retain it for
existing filesystem access behind a separate read-only container/device.
The reader must be implemented and qualified inside SectorWorks during Phase 6
before that phase can be complete; dependency evaluation alone is not delivery.
No dependency/source import is selected or authorized by this planning update.
The [PMFX research and delivery plan](pmfx-read-only-plan.md) records pinned
public references, GPL/AGPL provenance cautions, observed layout/codec differences,
remaining unknowns and independent-fixture requirements. Prefer managed code;
review every imported file and decoder license before integration.

Crucially, .NET 10 `ZLibStream` output-length-plus-EOF checks accepted missing
checksum bytes and trailing compressed input in local probes. Genuine member
completion, checksum and exact compressed-input consumption require a separately
qualified integration with negative tests before enabling PMFX. Valid payload
decoding is not filesystem or vendor-restore qualification. The research found
FAT16/FAT32, not NTFS PMFX; existing NTFS restrictions remain unchanged.

**Owner decision, 2026-10-13: keep DiscUtils 0.16.13 for continued development.**
The owner accepts the known maintenance risk and possible future replacement/
rework. Re-evaluate the dependency in the checklist's release-quality section
once the other production-readiness aspects are substantially complete, before
production release. A fork evaluation is not a prerequisite to development.

This is not blanket production-readiness or read/write capability approval.
Per-phase verification, input safety and data-integrity requirements remain in
force. Keep integration behind replaceable storage interfaces, and address any
actual phase-blocking defects when encountered rather than hiding them until
the release review. The foundation probe references only `DiscUtils.Fat`,
`DiscUtils.Ntfs`, `DiscUtils.Iso9660`, `DiscUtils.Vhd`, and `DiscUtils.Vhdx`;
`DiscUtils.Core` and `DiscUtils.Streams` are transitive, also 0.16.13. Package lock
files record the actual graph/content hashes. Avoid `DiscUtils.Complete` and
network/host-disc modules that are unnecessary here.
The complete upstream MIT notice is retained in
[`licenses/DiscUtils.txt`](licenses/DiscUtils.txt).

Actual restored NuGet metadata identifies MIT, upstream
`https://github.com/DiscUtils/DiscUtils`, and commit
`59d7cadab839c6d8dfcf52f8be5efe6d2ced190f`. NuGet reports 0.16.13 published
2021-09-13. Upstream's default branch latest commit at review is 2021-08-30
(same commit). This is approximately five years without default-branch activity:
**maintenance risk is high**; do not describe this package as actively maintained.
At the foundation decision no fork was substituted: none was demonstrated to solve the relevant limitations
with sufficiently reviewed provenance/tests. The release-quality review will
consider then-current upstream maintenance, forks, patches or replacements.
Independently validate NTFS writes in their implementing phase before enabling
them; postponing the dependency-choice review does not establish write support.
The subsequently owner-authorized isolated NTFS source repair is documented
below; it does not substitute NuGet assembly identities or change package versions.

### Phase 2C implemented NTFS reader subset and open qualification, 2026-10-15

The owner explicitly authorized Phase 2C and its synchronized roadmap/
specification change before Phase 3. **Bounded reader implemented; remaining 1.0
verification/review gates open, not phase complete**. The owner subsequently
deferred authentic NT4/Windows 2000/XP-created-media qualification and dependent
checks to post-1.0 **P1.NTFS.1-P1.NTFS.6** in the [checklist](../checklist.txt).
These are incomplete follow-ups, not 1.0 blockers. VM-authored fixtures suffice;
vintage hardware is not required. Automated tests, bounds, source integrity and
general platform verification remain required.
Implemented-subset results are now
tracked in [platform status](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15),
not full version/media qualification. Targets are
separate bounded **NTFS 1.2 (NT4), 3.0 (Windows 2000), 3.1 (XP+)** ordinary-read
subsets, including supported attribute lists/extensions and version-dependent
metadata. Existing ordinary reads and qualified managed Phase 2B writes below
remain the retained baseline, not evidence that legacy/attribute-list expansion
already works.

The implemented separate `NtfsReadValidation` preflight/resolver handles bounded
ordinary continuation/index/version metadata behind Storage contracts before the
unchanged signed reader. Shared record/run validation has explicit read-only
extension opt-in; strict writer `NtfsValidation.Validate` defaults remain
unchanged. Only its diagnostic now names the Phase 2B writer subset/Phase 3,
not the obsolete Phase 1 boundary. No package version, signed-reader assembly,
runtime dependency or source-repair assembly identity changed.
List/base/sequence/attribute and VCN checks reject malformed references and
unsupported content. MFT bootstrap lists remain explicitly blocked: safe
bootstrap mapping is not implemented. Resource caps and active-index/inactive-MFT
rules are documented in [storage bounds](storage-phase1.md#resource-cancellation-and-concurrency-policy).
Historical Phase 2C full verification passed 1001/one Windows-only skip, 1002 total. Four cases added
after that full run's build (named-list guard, mixed-volume isolation,
modern/legacy extension identity and empty nonresident index bitmap) pass in
that snapshot's final focused 49/49 run (35 attribute-list,
14 version/layout/service). Its inventory of 1006 including the skip is not a
claimed full 1005-pass run. Final repeat then-configured
self-contained publish/local Linux AppImage passed on that source, as did
the bounded owner read-only probe. Versioned
releases/GitHub publication are unchanged. Cross-builds are not physical
compatibility. Authentic historical media moves to **P1.NTFS**; remaining general
physical gates stay open. Stop for owner review.

No authentic NT4 fixture exists. Synthetic version-field tests are regression
inputs, not authentic legacy qualification. Legal synthetic fixtures, content
hashes and service workflows pass for the bounded subset; authentic provenance
remains incomplete under post-1.0 **P1.NTFS**. Subsequent 2D.6/2D.7 closed the
declared bounded reader/Tower source-content subitems; parent platform/review
gates remain open, as recorded in the current summary above and checklist.
Tower's approximately 8.42 GB raw NTFS 3.0 owner image stays outside the
repository: do not copy, modify or distribute it. Initial rejection concerns
the live directory attribute list at MFT 26 referencing indexing extension 8336;
the separate native warning is now explained by the pinned ntfs-3g whole-attribute
65536-byte limit versus the 344064-byte index. Initial reader enumeration succeeds,
not content/health certification; pinned references are in platform status.

Newly readable attribute-list/legacy variants remain noneditable until Phase 3
**3.0.1-3.0.5** qualifies staged editing, Save As and new-output NTFS writes
before **3.7** direct commit. Preserve existing Phase 2B qualified editing and
the required incomplete **2B.6** Windows-authored edited-output/TxF/CLFS and
LFS 2.0/cached-metadata write gates. The Windows codec reference separately
passed under 2D.2. Reader acceptance cannot close writer gates or demote NTFS
writing to read-only 1.0 scope; remaining Phase 3 work is not authorized by it.
See the [planned reader ledger](support-matrix.md#phase-2c-ntfs-reader-qualification-plan).

Sources reviewed:

- [Upstream README](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/README.md):
  managed C#, no native code/P/Invoke; general format claims, not guarantees.
- [MIT license](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/LICENSE.txt).
- [Exact package publication](https://api.nuget.org/v3/registration5-semver1/discutils.core/0.16.13.json).
- [Upstream tests](https://github.com/DiscUtils/DiscUtils/tree/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Tests/LibraryTests):
  FAT, NTFS, ISO, partitions, streams, VHD/VHDX, VDI/VMDK/XVA, Ext/HFS+/XFS,
  Btrfs and other directories exist. `LibraryTests.csproj` targets net5.0 with
  xUnit 2.4.1. This is test-suite existence, **not a measured coverage percentage**,
  recent .NET 10 assurance, fuzzing certification, or proof of platform parity.
- [Container source](https://github.com/DiscUtils/DiscUtils/tree/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library)
  and installed package XML API documentation, including VHDX stream constructor
  rejection of differencing disks and NTFS formatter's security-descriptor path.

## Initial capability matrix

“Source” means upstream API/source claim only. “Executed” means the narrowly
described local direct-library experiment. No bootability, exact-layout,
transactional-write, recovery, full validation, or production support is implied.

| Layer | Source read | Source write/create | Executed foundation evidence |
|---|---|---|---|
| Raw image | Seekable stream / Raw.Disk | Stream / Raw.Disk | Generated raw FAT images; reopen and read sentinel |
| MBR | Partition enumeration | Initialize/create table | One FAT partition authored; reopen, count, extent bounds, sentinel |
| GPT | Header/table enumeration | Initialize/create table | One FAT partition authored; reopen, count, extent bounds, sentinel; not comprehensive CRC/backup validation |
| FAT12/16/32 | Browse/read | Format/create/modify | All three real variants checked; directory and sentinel reopen; disposable create/move/reopen; read-only stream write rejected |
| NTFS | Browse/read | Format and modification APIs | Independent NTFS golden: resident sentinel plus 8192-byte nonresident file reopened. **Format fails on Linux** (see below). No successful NTFS modification probe |
| ISO9660 + Joliet | CDReader, optional extensions | CDBuilder rebuild, not normal in-place filesystem writes | ISO built once; both non-Joliet and Joliet reader paths reopen sentinel; Joliet Greek filename verified |
| VHD | Fixed/dynamic/differencing APIs | Fixed/dynamic creation and content writes | Dynamic 32 MiB virtual image authored with FAT sentinel; read-only reopen. Fixed/differencing unexecuted |
| VHDX | Fixed/dynamic/differencing APIs | Fixed/dynamic creation and content writes | Dynamic 32 MiB fixture authored/reopened; separate 1 TiB virtual write/read probe. Fixed/differencing unexecuted |

VHDX's stream constructor cannot resolve parents and explicitly throws
`NotSupportedException` for differencing layers. Path-based constructors resolve
chains: future production code must constrain parent paths, depth, cycles, and
missing-parent errors, not simply trust automatic resolution. Neither parent
handling nor interrupted VHDX log replay was validated here.

### Important NTFS cross-platform finding

The package annotates `NtfsFileSystem` as Windows-supported. The probe narrowly
suppresses CA1416 to **test this limitation**, not to suppress a product warning.
Calling `NtfsFileSystem.Format` on Linux .NET 10 produced:

```text
System.PlatformNotSupportedException: Windows Principal functionality is not supported on this platform.
  System.Security.Principal.SecurityIdentifier.op_Equality
  DiscUtils.Ntfs.NtfsFormatter.Format
```

Ordinary read-only access to the independently authored golden **does work** on
the available Linux host. This does not establish portable security descriptor,
ACL, encrypted, compressed, sparse, reparse-point, journal, or write behavior.
`mkntfs`/`ntfscp` were used only to author the committed synthetic golden; no
native package or CLI is required to run the probe, materializer, or tests.

## Executed error and streaming behavior

### Phase 2B existing-volume NTFS blocker (2026-10-15)

This is historical original-package **modification** evidence, distinct from
the formatter limitation. The subsequent managed repair is described below.
`ExistingNtfsVolumeProbe` operates only on create-new disposable copies of an
existing independently authored NTFS volume. Seven creation/security cases
(resident/nonresident Add, Add without short names, explicit uncompressed Add,
ordinary/without-short-names mkdir, and GetSecurity) fail on Linux .NET 10 with
`PlatformNotSupportedException`: Access Control List (ACL) APIs are unsupported.
The stack is `NtfsFileSystem.DoGetSecurity -> RawSecurityDescriptor..ctor`,
with `CreateNewFile`/`CreateDirectory` for creation, not `NtfsFormatter`.
A freshly authored `mkntfs` volume reproduces the failure, ruling out only a
peculiarity of the immutable golden or formatter.

Creation can allocate and index an empty file before throwing; independent
`ntfsinfo` reports Security ID 0 and the MFT bitmap gains a record.
`ntfsfix -n` still passes, so that command alone is not a safe success gate.
No partially modified copy is published by the product.

Existing resident/nonresident replacement (including grow/shrink transitions),
deletion and Unicode rename experiments succeed. Tests verify content, retained
security bytes, creation time/attributes, cluster and MFT bitmaps, and independent
`ntfsinfo`/`ntfscluster`/`ntfscat`/`ntfsls` checks where installed. These narrower
successes do **not** qualify the required five-operation writer or establish
general NTFS consistency/security preservation.

Sixteen new regressions and 33 targeted NTFS/reader tests passed locally.
Detailed executed output is retained locally in
`artifacts/ntfs-existing-volume/regression.log`; repeat with the
`NtfsExistingVolumeTests` selector in the storage-test project.
At that prerelease, NTFS staging/Save As remained disabled across raw/VHD/VHDX.
At the partial-release review, portable-security remediation or an explicit
scope decision required owner direction. The owner subsequently authorized the
bounded managed investigation below and reaffirmed that NTFS writing remains
required for 1.0; read-only NTFS is not an acceptable release-scope substitute.
DiscUtils 0.16.13 remains approved; no replacement, ACL substitution, host
mounting or formatting workaround was introduced.

### Managed NTFS repair implementation and qualification

The owner subsequently authorized the portable security component, managed
repair implementation and qualification. This removes the constructor and
inheritance blockers for the qualified subset, not for every NTFS feature.
The original dependency-boundary diagnostics remain intentionally tied to the
unchanged signed package; their expected failures are still valid baseline
evidence, not tests to rewrite as repaired-backend successes.

`SectorWorks.ManagedNtfs` is a separately named assembly importing only the
pinned NTFS sources and two required internal Core helpers (`ObjectCache`,
`Utilities`). Signed Core/Streams and all existing 0.16.13 packages remain
unchanged. No private signing key, identity impersonation, reflection-based
friend bypass or native runtime dependency is introduced. Portable byte APIs
are separate from annotated Windows-only descriptor bridges. Formatting, quota
management and the upstream optional `BuildClusterMap` diagnostic remain
explicitly unsupported; product allocation validation does not call that API.

`SectorWorks.NtfsSecurity` supplies bounded managed parsing/canonical serialization
and corrected file/container inheritance. Unsupported semantics reject before
allocation. Children inherit image-parent owner/group under the declared policy,
not host SIDs or a permissive default. Existing raw descriptor bytes remain
preserved; hashes of stored `$SDS` records use the stored bytes, not canonicalized
input. See the [editing guide](staged-editing.md#managed-ntfs-subset-and-remaining-mandatory-qualification)
for exact ACL/name/metadata/log restrictions.

Qualification also exposed and repaired tightly coupled upstream defects:

- Checked byte-complement casts overflowed while clearing allocation bits.
  Only intentional low-byte bit reinterpretation is unchecked.
- Backward run deltas require signed reinterpretation; lengths are unsigned
  positive bounded counts. Mapping-pair terminators must be accepted before
  nonterminal width checks. Compiled binary-vector tests cover these paths.
- Fallback allocator zones overwrote rather than accumulated allocations;
  scans also needed exclusive zone ends. Near-full replacement now leaves
  exactly 16 independently verified free clusters.
- MFT mirror writes were hardcoded to four records. The required extent is
  `max(4 * recordSize, clusterSize)`, including user records for large clusters.
- SI restoration regenerated independent filename/index dates and transaction
  change times. Explicit byte snapshots preserve those dates and DOS aliases.
- New-file creation automatically generated unsupported ObjectIDs on fresh
  media. The qualified writer opts out before creation; it never strips
  existing IDs or weakens the validator to accept them.

The targeted suite passes **382 tests with one explicit Windows-only skip**
(383 total): portable security vectors/flags/bounds, independent raw
allocation/security/log validation, compiled mapping pairs, 21 actual Services
container/layout/physical-sector rows, eight native cluster sizes, multi-volume
and FAT/NTFS composition, transitions, growth, full/near-full media, metadata and
source preservation, cancellation and actual I/O/flush/validation failures.
Independent `qemu-img`/`ntfsinfo`/`ntfscluster`/`ntfscat` checks pass. Six actual
Mint/X11 NTFS workflows pass separately, with app-window screenshots, unchanged
sources and independent native output-byte checks.

Historical Phase 2B repository verification passed **956 tests, one Windows-only skip,
957 total**, locked restore, zero-warning/error Release build, formatting,
resources and authoritative-copy comparisons. The historical final cross-publishes and
the Linux x64 AppImage pass architecture/legal/payload and Linux resource checks.
These do not supply missing Windows-runtime or modern-log evidence.

**At that Phase 2B snapshot, mandatory gaps were:** actual Windows-authored
media, Windows reference execution, and LFS 2.0/cached-metadata safety. The
reference subsequently passed under 2D.2; native author/source-readonly reopen
also passed, but edited-output/TxF/CLFS and modern-log safety remain blocked.
No normal-Windows-media or full
Phase 2B completion claim follows. Clean LFS 1.1 has synthetic bounded
restart-page coverage only; dirty/ambiguous/unsupported logs remain disabled,
without replay or reset. Structural references are the
[LFS definitions](https://github.com/tuxera/ntfs-3g/blob/edge/include/ntfs-3g/logfile.h)
and [clean-state checks](https://github.com/tuxera/ntfs-3g/blob/edge/libntfs-3g/logfile.c);
no source from those files is imported into the application.
NTFS writing remains mandatory for 1.0. No new dependency replacement decision,
scope reduction or Phase 3 authorization is implied.

MIT originals and notices are retained verbatim. Original repair additions
follow the repository's GPL-3.0-only terms. Imported sources, exact upstream
input hashes, transformation/repair patch and final hashes are retained under
`src/SectorWorks.ManagedNtfs/`; no generated binaries or owner samples belong
in Corresponding Source. Verify provenance against the approved source snapshot:

```sh
python3 src/SectorWorks.ManagedNtfs/record-repairs.py --verify
```

New SDK projects and downstream lock files record the changed project graph;
package versions are unchanged. The architecture boundary is Storage ->
ManagedNtfs -> NtfsSecurity, with no dependency types exposed to Services/Core/UI.
The existing Services test that expected all NTFS to be disabled was replaced
with an explicit positive qualified-NTFS test; genuinely unqualified historical
container/partition/optical fixtures retain their negative tests.

### Bounded managed NTFS repair investigation (2026-10-15)

**Historical investigation complete; agreed stop point reached, not a writer repair.**
The owner selected Option 1: investigate a contained managed repair and stop
before a broad security-subsystem rewrite or material dependency/architecture
change. NTFS editing remains mandatory for 1.0 and Phase 2B remains blocked.
During that investigation no product writer, dependency version, signing policy
or release asset changed. The owner subsequently authorized the implementation
and qualification below, superseding that stop point, not the Phase 3 boundary.

The investigation traced the approved package's exact source commit
`59d7cadab839c6d8dfcf52f8be5efe6d2ced190f`, checked the actual installed assembly's
creation-call IL, and executed constructor and existing-volume probes on
Linux x64 / .NET 10.0.13. The assembly version is `0.16.0.0`; the NuGet package
version remains `0.16.13`. These are not conflicting dependency versions.

#### Executed evidence

- All five independently exercised constructors/parsers fail with
  `PlatformNotSupportedException`: binary `SecurityIdentifier`, binary `RawAcl`,
  binary `GenericAce`, binary `RawSecurityDescriptor`, and SDDL
  `RawSecurityDescriptor`. Switching from SDDL to binary input does not remove
  the platform dependency; constructing an explicit `NewFileOptions` descriptor
  cannot bypass it either.
- The installed `CreateDirectory(string, NewFileOptions)` and `CreateNewFile`
  methods both pass literal `false` as `isContainer` to
  `CalcNewObjectDescriptor`. This confirms a directory-inheritance defect in the
  shipped package, not merely a discrepancy in downloaded source.
- All 16 existing-volume regressions pass again: expected creation/security
  failures, partial-mutation detection, narrower successful mutations, native
  checks, create-new destination safety and unchanged synthetic sources.
  Passing a regression that expects failure is not successful NTFS creation.
- Seven new dependency-boundary tests preserve the five platform observations
  and two installed-assembly call-site observations. These are baseline
  diagnostics, not desired future writer behavior; intentionally update their
  expectations when a reviewed patch changes the dependency.

#### Source findings and repair surface

The affected runtime path spans
[`NtfsFileSystem`](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Ntfs/NtfsFileSystem.cs),
[`SecurityDescriptor`](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Ntfs/SecurityDescriptor.cs)
and
[`SecurityDescriptors`](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Ntfs/SecurityDescriptors.cs).
The Windows types are used for parsing, inheritance, NTFS-specific descriptor
serialization, hashing/deduplication and `$Secure` lookup/insertion, not only for
the public security-property API. Legacy per-file descriptors and shared
security-ID descriptors both need portable handling.

Comparison with Microsoft's
[ACE inheritance rules](https://learn.microsoft.com/en-us/windows/win32/secauthz/ace-inheritance-rules)
identifies additional semantic gaps in the source helper:

| Case | Approved helper | Required behavior |
| --- | --- | --- |
| Container-only inheritance to a directory | Directory creation chooses the file branch and excludes the ACE | Inherit an effective directory ACE |
| Object-only inheritance through a directory | Even changing the argument to `true` excludes this ACE; the current `false` branch makes it effective | Propagate an inherit-only ACE unless no-propagate forbids it |
| Generic rights / CREATOR_OWNER or CREATOR_GROUP | Copies mask and SID without translation | Map effective rights/SIDs and retain a separate inheritable form when required |

The last two findings are source/rule comparisons, not executed Windows ACL
round trips. A constructor-only shim would retain them. The upstream
[`AclInheritance` test](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Tests/LibraryTests/Ntfs/NtfsFileSystemTest.cs)
is explicitly skipped, referring to
[DiscUtils/DiscUtils#14](https://github.com/DiscUtils/DiscUtils/issues/14).
Its presence is not evidence that inheritance was qualified.

Both creation paths also read the parent descriptor unconditionally, even when
an explicit descriptor is supplied, and do so after allocating/indexing the new
entry. Merely setting an option cannot bypass that call. A future repair should
validate and derive the supported descriptor before creating the entry;
disposable Save As failure/publication safeguards remain necessary regardless.

The public `IWindowsFileSystem` contract exposes `RawSecurityDescriptor`.
A contained implementation could retain Windows-facing adapters while adding
an internal portable byte representation; changing all public APIs or replacing
FAT/container handlers is not established as necessary. A reproducible source
patch/fork still requires a reviewed assembly-identity/build plan:
[`DiscUtils.Core` friend declarations](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Core/Properties/AssemblyInfo.cs)
bind NTFS access to a strong-name public key. Do not assume an independently
named/signed NTFS assembly is a drop-in replacement or disable signing to force
it through.

#### Stop decision and proposed next bounded deliverable

**No-go for an API-substitution-only patch.** The next meaningful implementation
is a portable NTFS security component plus corrected inheritance and dependency
integration, rather than a constructor fix. That exceeds this investigation's
contained-patch stop boundary. No partial codec, permissive/default ACL,
inheritance bypass, host mount or unqualified writer was introduced.
This does not establish that a managed repair is infeasible or that NTFS needs
to be demoted to read-only for 1.0.

Recommended next authorization: implement and qualify that NTFS-only component
in a reproducible, pinned source patch/fork, keeping the replaceable SectorWorks
interfaces and existing FAT/container behavior. Before enabling any writer:

1. Bound and validate self-relative descriptors, SIDs, ACEs and ACLs; preserve
   owner/group, DACL/SACL, control flags and null-versus-empty ACL semantics.
   Reject unsupported inheritance semantics explicitly, without discarding or
   weakening existing permissions. Define the exact ordinary-security subset.
2. Test file/directory inheritance, object/container/no-propagate/inherit-only
   combinations, deny/allow ordering, generic masks and creator SID handling.
   Establish independent expected bytes and Windows-reference interoperability;
   Linux construction success alone is insufficient.
3. Integrate both legacy/shared descriptors and verify `$Secure` security IDs,
   hashes, `$SDS` duplicate records and `$SII`/`$SDH` indexes alongside existing
   MFT/allocation/preservation checks. Do not replace this with `ntfsfix -n`.
4. Review MIT notices/source distribution, assembly identity/signing and locked
   dependency packaging, then run the required Phase 2B NTFS operation/layout/
   container matrix and failure gates. Cross-publishes remain distinct from
   physical Windows/ARM64 evidence.

No Phase 3 work or change to the NTFS 1.0 requirement follows from this report.
Formatting and quota/SDDL presentation APIs remain separate evaluation surfaces;
this investigation did not qualify them.

Reproduce the boundary tests after sourcing `scripts/common.sh`:

```sh
"$DOTNET" test tests/SectorWorks.DependencyTests/SectorWorks.DependencyTests.csproj \
  --configuration Release --no-restore --filter FullyQualifiedName~NtfsSecurityBoundaryTests
"$DOTNET" test tests/SectorWorks.StorageTests/SectorWorks.StorageTests.csproj \
  --configuration Release --no-restore --filter FullyQualifiedName~NtfsExistingVolumeTests
```

Local-only evidence: `artifacts/ntfs-security-investigation/` contains the source
snapshot and detailed constructor/IL/baseline test logs. No owner sample or
modified NTFS output is distributed.
The complete dependency suite passes **27 tests**, and the separate
existing-volume suite passes **16 tests**, with zero failures/skips in both.
The added file passes formatting/analyzer verification; authoritative technical
specification copies and ignored owner-sample hashes remain unchanged.
No full-solution rerun, new physical-platform qualification, cross-publish or
release is claimed for this test/documentation-only investigation. The previous
561-test release evidence remains historical and is not relabeled as a new run.

### Historical foundation malformed and streaming evidence

The malformed tests run the direct library parser in a **separate process with
a 15-second deadline**; timeout kills that process tree and fails the test.
Exception types are asserted, not silently swallowed or accepted generically.
The CLI boundary prints exception type/message and returns nonzero. Observed:

| Fixture / operation | Expected observed error |
|---|---|
| 31-byte FAT prefix | `System.IO.EndOfStreamException`: unable to read 512 bytes |
| FAT BPB with zero bytes/sector | `DiscUtils.InvalidFileSystemException`: bytes per sector is 0 |
| 31-byte ISO prefix | `System.IO.IOException`: no permitted ISO9660 variant detected |
| 31-byte VHD prefix | `System.ArgumentOutOfRangeException`: non-negative number required |
| 31-byte VHDX prefix | `System.IO.EndOfStreamException`: unable to read 65536 bytes |
| Create file through read-only FAT stream | `System.NotSupportedException`: stream does not support writing |

The VHD exception is a leaked low-level argument error, not a polished format
diagnostic. The future adapter must normalize errors without hiding corruption.
These small cases do not prove general safety against malicious counts,
cyclic chains, decompression bombs, overflow, or parser hangs.

The separate dynamic VHDX experiment exposes **1 TiB** virtual capacity while its
container is **37,748,736 bytes**. It writes byte `0x5a` near the virtual end,
flushes/closes, reopens read-only, and checks the byte. Instrumented underlying
reads total **1,254,960 bytes**, largest request **1,048,576 bytes**. Tests enforce
less than 16 MiB read, requests at most 4 MiB, and container below 64 MiB.
This demonstrates a large sparse/dynamic *logical* address space without loading
the whole image; it is not a multi-terabyte physical-data or throughput benchmark.
Fixture decompression and SHA256 hashing use streaming buffers (64 KiB for copy).

## Extra formats researched, NOT installed or prototyped

| Source component | Source-level read claim | Source-level write/create claim / limitation |
|---|---|---|
| VDI | VirtualBox fixed/dynamic | `InitializeFixed` / `InitializeDynamic`; variants/parents untested |
| VMDK | VMware variants | `Initialize` + disk creation types; do not infer every extent/parent variant is writable |
| XVA | Xen virtual disks | README advertises read/write; TAR/archive rebuilding semantics require separate evaluation |
| DMG | UDIF disk-image reader | No demonstrated writer; treat as read-only candidate, compression/encryption variants unvalidated |
| WIM | Windows image reader | Source explicitly rejects writes (`No write support for WIM files`) |
| UDF | UDF reader | No demonstrated writer/rebuilder; version/feature coverage must be evaluated |
| Ext | ext-family reader | `VfsExtFileSystem` derives from read-only base; do not promise all ext3/ext4 incompat features |
| HFS+ | HFS+ reader | Internal implementation `CanWrite` false; metadata/normalization coverage untested |
| SquashFS | Reader + builder | Rebuild-oriented, not in-place mutable filesystem |
| XFS / Btrfs | Source reader components exist | Not evaluated for writes or feature subsets; no support claim |
| QCOW2, exFAT, APFS | No corresponding implementation identified in selected upstream library tree | Additional dependency or internal implementation required |
| BIN/CUE, CD-DA | No complete required CUE/track/audio workflow established | Additional optical model/parser/audio backend needed in Phase 5 |

All extra-format statements are **research only**, not executed capability tests
or a Phase 6 implementation. Rock Ridge, El Torito, UDF, hybrid optical metadata,
and boot preservation are not exercised by the simple ISO golden.

## Significant-dependency risk/platform matrix

The table retains the historical foundation evaluation envelope, not current
platform qualification. Later Windows x64 codec/reader/generated-writer runtime
results are recorded above and in [platform status](platform-status.md).
Current Linux ARM64/Windows ARM64 evidence remains limited.

| Dependency / version | License / maintenance evidence | Native / platform and error risk | Threading / large / sparse |
|---|---|---|---|
| DiscUtils Core, Streams, FAT, ISO9660, VHD, VHDX 0.16.13 | MIT; 2021 upstream/package baseline, maintenance risk high | Managed, no required native library. Linux x64 executed. Other then-configured target RIDs unverified physically. Error taxonomy inconsistent as above | Mutable stream position/caches: assume instances **not thread safe**; serialize per instance. Streams/Int64 APIs; dynamic VHDX large-address probe passed, not all formats/features |
| DiscUtils NTFS 0.16.13 | MIT; same maintenance baseline | Managed but Windows security APIs restrict format/ACL/write paths. Linux ordinary reads passed; Windows x64/ARM64, Linux ARM64 unverified | Same isolation policy; only resident/nonresident ordinary-file read tested; compression/sparse semantics unverified |
| .NET SDK 10.0.402 / runtime 10 | Microsoft .NET open-source MIT components plus applicable runtime notices; platform lifecycle owned by project foundation | Host/runtime has platform-specific native components; not a new storage-native dependency. Physical Linux x64 only here | Runtime streams/hashes used, not a guarantee that dependent libraries honor cancellation |
| xUnit 2.9.3 + VS runner 4.0.1 (test-only) | Apache-2.0 in restored package metadata; established maintained projects, selected versions pinned | Managed tests/adapter; actual Linux x64 runner passes. Other RIDs not physically validated | Isolation: per-test disposable directories, no shared parser instances; not product large/sparse handling |
| Microsoft.NET.Test.Sdk / TestHost / ObjectModel / CodeCoverage 18.11.0 (test-only) | MIT in package metadata; Microsoft test-platform project | Platform-specific test/coverage assets may exist; no coverage collector enabled here and not shipped in product. Linux x64 execution passed | Process/test orchestration only; not a storage parser |
| ntfs-3g command-line utilities (golden-authoring only) | GPL-2.0-or-later tool project, not linked/distributed by SectorWorks | Linux native `mkntfs`/`ntfscp` were already installed; unnecessary for routine tests or golden expansion | Disposable local image only, no mounting or host block device access |

Transitive test-only xUnit packages (`assert`, `core`, `extensibility`,
`abstractions`, `analyzers`) and Microsoft test-platform packages are recorded
in the test lock file. MIT and Apache-2.0 dependencies are compatible with the
chosen **GPL-3.0-only** application distribution when notices/licenses are
retained; this is an engineering review, not legal advice.

## Required future work, not implemented in foundation

The owner-approved 2026-10-15 roadmap targets 1.0 with qualified read-only
VMDK/VDI/QCOW2/DMG/ext/exFAT/HFS+/APFS subsets where feasible. No writer or future
writer milestone is required for these additions. Phase 6 reader evaluation,
fixtures/tests and concrete blocker reporting remain required; source-level
claims above and detection-only do not establish browsing/extraction.
Internal parsers, not native host mounting support, determine capability.

- Phase 1 must add bounded I/O/partition views, checked offsets/counts,
  safe cancellation/resource budgets, error mapping, parent-path restrictions,
  feature-level capabilities and substantially broader hostile-image tests.
- No assumption of thread safety; future session-level synchronization and
  background work must avoid sharing a mutable DiscUtils stream/parser.
- Phase 2 must independently validate each write operation/stack; the small
  FAT round trip does not authorize SectorWorks modification.
- The owner-approved 2026-10-15 roadmap adds Phase 2B before Phase 3 for explicit
  raw/VHD/VHDX, filesystem-only/MBR/GPT, FAT LFN and ordinary-NTFS staged/Save As
  qualification. Existing-volume NTFS modification and formatting are separate;
  the SID formatting defect does not settle writer feasibility. Required writer
  delivery cannot be marked complete after evaluation alone.
- Phase 3 owns locking, identity checks, journaling/recovery and safe direct
  commit. DiscUtils provides no complete SectorWorks transactional safety layer.
  Phase 2C first adds independently qualified NTFS 1.2/3.0/3.1 reader subsets;
  newly readable legacy/attribute-list variants stay noneditable until Phase 3
  3.0.1-3.0.5 qualifies staged/Save As/new-output NTFS writing. Direct commit
  then needs 3.7 per-stack qualification for required Phase 2B and new writer rows.
- Phase 4 needs a preservation-aware ISO rebuild planner and independent
  validation/boot testing; CDBuilder is not proof of source-layout preservation.
  Declared UDF and ISO/UDF bridge read/staged/rebuild delivery also needs explicit
  implementation and independent qualification, not detection-only completion.
- Additional libraries/internal work are needed for missing filesystems,
  exact optical structures/audio and comprehensive validation. Prefer managed
  implementations; any native option requires all four supported OS/architecture reviews.
- Keep trimming/AOT off until reflection/provider registration is evaluated.
  Dependency choice is provisional; no platform support follows from compilation.
