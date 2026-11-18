# Architecture

## Phase 5 — multi-file optical sets and playback

`ICdImageSession` exposes sessions, track geometry and bounded raw/audio views.
The Storage CUE adapter resolves regular, unlinked companions and presents
2048-byte user data to the existing optical reader. Services own the shared
staging/history and multi-member source hashes; `IImageSetRebuildSource` plans
only per-track ISO writes. The sealed output directory is independently
reopened before the Platform no-replacement rename.

`ICdTrackSource` gives the UI independent, read-only track streams. Services
stream aligned PCM using `ICdAudioOutput` (Platform: Windows waveform audio
or Linux ALSA). The workspace owns playback cancellation and never writes
source audio, companion files or mixer settings. See the
[Phase 5 report](phase5-bin-cue.md).

## Phase 4 — independent optical rebuild boundary

IImageRebuildSource supplies a bounded plan for a qualified standalone optical
volume. Services reuses its overlay, undo/redo, import spool and source checks.
IImageRebuildPlan estimates allocation, writes to an exclusively created
temporary output and validates independently reopened metadata and content.
The shared Save As entry calls this plan; the Phase 3 disk copy/commit and
recovery paths remain separate. No optical source replacement is provided.
See the [Phase 4 format envelope](phase4-optical-rebuild.md).

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



## Current commit/recovery integration (2026-11-16)

Resumed verification changes no architecture or product code: all337 prior
final-code manifest members match. Current R4b Windows GUI Save As/commit and
restarted cleanup/refusal have exact independent output evidence. Removing the
intentional stale test registration after archive/hash checks was task cleanup,
not a relaxation of production recovery refusal. See the current review.

The continuous assignment in [the owner prompt](sectorworks_phase_2d_and_phase_3_autonomous_sol_prompt.txt)
authorizes the remaining Phase 3 increment despite retained independent Phase 2D
blockers. Current implementation/evidence is tracked in the [review](phase2d-phase3-review.md)
and [live ledger](phase2d-phase3-progress.md); older dated backend-only statements
below describe their historical envelope.

`IImageCommitService` is a separate Core boundary. `IStagedImageWriter.QualifyCommitVolume`
defaults to denial; Save As eligibility alone grants no commit authority. The
Explorer service combines exact-volume decisions and source-host restrictions,
retaining its held-lock, hash-checked backup, copied staging, independent validation,
atomic replacement and baseline/history refresh pipeline.

`ICommitRecoveryService`/`RecoveryItem` cross the Core boundary; Services owns a
bounded private application catalog and anchored artifact access. The real App
startup checks that catalog before opening command-line images, while the Tools
menu can reopen recovery after workspaces close. The recovery view model controls
confirmation and error/pending state; views do not invoke storage primitives.
Cleanup never restores or overwrites an image. Journal intent is a bounded audit
summary of the active ordered plan, not a replay mechanism. Staged payload hashes
are computed during immutable capture. See the [review](phase2d-phase3-review.md)
for interruption boundaries, bounds and evidence limitations.

Windows/Linux x64 and ARM64 are the only build/distribution targets as of the
owner decision on 2026-11-16. The local Linux host remains the primary
development/default test environment. Windows tests use the verified MCP
[desktop/command workflow](windows-desktop-testing.md); infrastructure access
does not qualify untested SectorWorks behavior.

## Phase 2D scope, 2026-10-17

Owner-authorized 2D closes retained 2B ordinary-current-NTFS Windows reference/
media/log-safety and 2C bounded reader/integrity/workflow gates, not new legacy/
attribute-list writer capabilities. Keep existing Storage/Services/security
boundaries and all qualified writes. No host mounting is introduced for core
image access: owner-launched Windows-native disposable virtual-volume checks
are independent interoperability evidence only. BCL codec comparison alone
is neither complete inheritance proof nor filesystem interoperability.
Do not continue Phase 3 recovery/UI/new-target writer implementation; preserve
the October 16 backend delivery below. See [named closure items](../checklist.txt)
and [current evidence](platform-status.md#phase-2d-preflight-and-closeout-2026-10-17).

The opt-in `tools/SectorWorks.ReaderProbe` remains a Linux diagnostic, outside
the product dependency graph and release payload. It uses existing reader/
Services contracts for approved external-media source hashes, bounded workflows
and selected temporary extraction; it does not implement a new reader or writer.
Windows-native scripts are likewise independent generated-media verification,
not host-driver-backed product access. Their process/evidence helpers have
actual Windows exit/timeout and strict UTF-8 metadata regressions.
The initial 2D application change was a clearer existing log-refusal diagnostic,
not modern-log or TxF capability broadening. The owner-resumed corrective pass
also repairs commit resource ownership and log concurrency, without expanding
the writer envelope. The final corrective pass resolved the retained Windows
runtime defects on qualified generated rows. Windows-authored TxF/CLFS and
LFS 2.0/cache-safety proof obligations, current ARM64 and GUI/platform/review
gates remain [explicit blockers](platform-status.md#final-global-session-corrective-snapshot).

The October 21 owner-resumed TxF/CLFS investigation adds an opt-in
[`inspect-clfs.py`](../scripts/inspect-clfs.py) diagnostic, outside the product
graph/publish payload. It reads only sealed extracted generated base logs,
reports bounded structural observations and never supplies a writer-acceptance
predicate. Existing Storage/Services writer gates remain unchanged. See
[current scope/evidence](platform-status.md#resumed-txfclfs-investigation-2026-10-21).

Commit suspends the original read-only session before taking a source write
lock, streams a hash-checked recovery backup through the locked handle, and
validates preservation against an independently reopened backup session.
Windows denies other write handles but shares deletion for atomic finalize;
finalize uses one `FileRenameInfoEx` operation with replace/POSIX semantics,
not `File.Move` (which rejects the open target) or `File.Replace` (whose
documented partial-failure/metadata-merge behavior is unsuitable here).
The source lock is never released early to make replacement succeed.
Unsupported Windows/host-filesystem atomic replacement fails explicitly;
there is no non-atomic fallback.
Linux uses descriptor-owned `F_OFD_SETLK`, not process-owned `F_SETLK` (closing
an unrelated reader must not drop the lock). Unsupported Linux locking fails
explicitly. A per-path guard also refuses concurrent writers in this process.
Success refreshes the workspace baseline and clears committed staging/history;
failure reopens the unchanged baseline and retains pending content. If reopening
fails or source identity changed, further operations refuse explicitly while
close still cleans owned payloads. A pre-finalized recovery journal alone is
not proof that replacement never occurred: restoration refuses a source that
does not match the backup, including the atomic-replace/final-journal gap.

`JsonExplorerLog` serializes append and rotation with a bounded, path-keyed
`Global\` named mutex on every OS, not per-instance or session-local locks.
Linux tests create a new process session before CLR startup with `setsid --wait`;
switching sessions from managed `Main` does not establish a trustworthy isolated
mutex namespace. I/O, timeout and abandoned
writer failures remain explicit. The test-only `tools/SectorWorks.RuntimeProbe`
provides independent .NET writer/log workers without a product runtime
dependency or inclusion in release payloads.

Preference saving already reports filesystem errors and removes its uniquely
named replacement in `finally`. The corrected tests preserve the platform's
`IOException`/`UnauthorizedAccessException` distinction rather than hiding or
normalizing access failures. They also verify untouched destination-directory
contents, no temporary residue and actual Windows deny-delete handle behavior
(Unix replacement instead preserves the old reader's bytes).

The foundation's layered solution is retained. Phase 1 supplies the read-only
pipeline and Avalonia explorer. Phase 2 adds staged modifications and Save As
through separate editing contracts. Phase 3 (2026-10-16) adds a container/
filesystem-agnostic safe direct-commit pipeline for every row that was already
Phase 2B Save-As-qualified; rebuild and audio remain later-phase work. See
[staged editing](staged-editing.md#phase-3-direct-commit-implemented-2026-10-16).

## Phase 2C implemented reader boundary and open qualification

The owner authorized the Phase 2C roadmap/specification change and reader work
on **2026-10-15**, before Phase 3. The bounded reader is implemented;
**revised 1.0 reader/verification/review gates are accepted complete on November17**.
The owner subsequently deferred authentic NT4/Windows 2000/XP-created-media
qualification and dependent checks to post-1.0 **P1.NTFS.1-P1.NTFS.6** in the
[checklist](../checklist.txt); these are not 1.0 blockers or passed tests.
VM-authored fixtures suffice, without vintage hardware. Automated tests, bounds,
source integrity and general platform checks remain required. Stop for owner review.
Reader evidence is tracked in
[platform status](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15),
not full version/media or writer qualification.
Targets are bounded read-only NTFS **1.2 (NT4), 3.0 (Windows 2000), 3.1 (XP+)**
subsets with supported attribute lists/extensions and version-specific ordinary
metadata/content reads. Keep dependency-specific parsing behind Storage and
the existing Core read contracts; do not expose library types or use host
mounting. No UI redesign or new source-commit contract belongs to this work.

Implemented resolution checks bounded references, base/sequence/attribute
identity, VCN coverage and cycle/overlap checks before dependency consumption.
The reader design keeps strict `NtfsValidation.Validate` unchanged for writer
qualification and adds a separate `NtfsReadValidation` preflight/resolver before
the unchanged signed reader. Only the writer diagnostic now names the Phase 2B
writer subset/Phase 3 instead of Phase 1; strict defaults stay unchanged.
Shared `ValidateRecord`/`ValidateRuns` checks have explicit read-only extension-record
opt-in, never a relaxed writer default. Implemented coverage is bounded ordinary
base/extension records, resident/nonresident attribute lists and split data/index
streams. MFT bootstrap lists are explicitly blocked because safe bootstrap
mapping is not implemented; the resolver cannot assume its own unresolved MFT
mapping. This is an exact excluded subset, not blanket attribute-list
compatibility. Filesystem metadata reports
the observed version through `fs.Properties["NTFS version"]`; this does not
establish authentic legacy authoring provenance.
Browse/preview/hex/search/extraction continue through shared Services contracts.
The latest 49 focused tests (35 attribute-list, 14 version/layout/service)
include read workflows/source hashes, cancellation,
staging/Save As denial, malformed references and GPT FAT/NTFS capability isolation.
Resource caps and bitmap-driven
live-index traversal are documented in [storage bounds](storage-phase1.md#resource-cancellation-and-concurrency-policy).
The completed full verification and four later-case count chronology are
recorded in platform status. Final repeat then-configured publish/local AppImage and the
bounded owner read-only probe pass on latest source. These are not physical
compatibility or release publication. Authentic historical-media qualification
is deferred to P1.NTFS; revised1.0 full-phase gates are accepted complete. Physical
ARM64 and further modern NTFS qualify only in their deferred sibling backlogs.
Per-volume writer qualification remains independent. At the Phase 2C reader
checkpoint, newly readable variants stayed noneditable. The current bounded
structural writer expansion is separately qualified as described above; existing
FAT/ordinary NTFS workflows and mixed-volume isolation remain required regressions.
Phase 3 **3.0.1-3.0.5** must qualify their staged/Save As/new-output NTFS writer
rows before **3.7** direct commit. The 2026-10-16 Phase 3 session did not
attempt 3.0.1-3.0.5; the November16 assignment now qualifies its bounded
structural rows without broadening modern transaction/log acceptance.
Phase2B's bounded native passive outputs are verified separately from readers.
Further default-TxF/CLFS and LFS2/cache writer qualification remains incomplete
but explicitly deferred under **P1.NTFS.MODERN**, not passed by reader expansion. The
Windows codec reference is complete under **2D.2**; reader success alone did not
close that separate reference task or the remaining writer requirements.

Authentic NT4 provenance is absent; synthetic version-field tests are not
authentic legacy qualification. Tower's external owner image must not be
copied/modified/distributed; its initial directory-attribute-list rejection and
native index warning have separate bounded read-only evidence: initial
enumeration succeeds, and the warning is a native whole-attribute size limit,
not proof of corruption or health.
See [planned reader gates](support-matrix.md#phase-2c-ntfs-reader-qualification-plan)
and checklist **2C.1-2C.7**, **2C.V1-2C.V8**, **2C.E1-2C.E6**.

## Phase 3 direct-commit architecture (implemented 2026-10-16)

**Historical backend description.** The shared eligibility gate and absent
UI/startup integration described in this dated record are superseded by the
current independent qualification methods, UI and bounded recovery catalog at
the top of this document. Retained locking/finalization evidence remains valid
within its original scope.

`SectorWorks.Storage/DirectCommitTransaction.cs` adds container/filesystem-
agnostic primitives (`SourceWriteLock`, `CommitJournal`, `CommitBackup`,
`CommitRecovery`, the `CommitStage` enum and `TransactionRecord`). These do
not reimplement per-stack writers; `ExplorerService.CommitAsync`
(`SectorWorks.Services/ExplorerService.Commit.cs`) wraps the already-qualified
Phase 2B `IStagedImageWriter.Apply`/`ValidateOutput` pipeline, retargeting its
output at a disposable staged temporary file beside the source instead of a
new destination, then atomically swaps it over the source only after
independent reopen validation succeeds.

`WorkspaceCapabilities.CommitInPlace` is computed by the same qualification
`Capability` as `Modify`/`SaveAs` (`ExplorerService.Editing.cs`'s
`CommitCapability` helper), so direct-commit eligibility automatically tracks
Save-As eligibility per row without separate per-stack wiring; the still-
blocked Phase 2C legacy/attribute-list NTFS rows correctly remain
`CommitInPlace = false`.

Two integration points are explicitly NOT yet wired and are flagged as open
follow-ups rather than silently assumed: `CommitRecovery.Discover` (startup
crash-recovery detection) is a tested library call with no invocation site in
the UI application host yet, and there is no UI command bound to
`CommitAsync`. See [staged editing](staged-editing.md#phase-3-direct-commit-implemented-2026-10-16)
for the full pipeline description and test evidence.

## Retained Phase 2B editing architecture

Phase 2B adds per-volume qualification (`IStagedImageWriter.QualifyVolume`) and
production `DiskImageWriter` composition. Workspace editing capability means
at least one volume qualifies; it never grants editing to an unqualified
sibling. History snapshots contain separate case-insensitive trees keyed by
stable volume IDs, with one ordered cross-volume undo/redo list and one bounded
payload budget. Unqualified volumes continue using the original reader.

Save As copies physical container bytes and hashes that same byte stream,
not virtual sectors mislabeled as a container. Output reopen checks container,
virtual/sector and partition/volume identities before validating each affected
tree and its allocation/metadata. Selected-volume writing must also preserve
unchanged virtual regions and supported container metadata. The Platform
`IStorageSpace` boundary supplies host free-space checks for import spools and
destination/scratch estimates, independently testable for each filesystem.
The owner-authorized managed NTFS repair uses separately named implementation
assemblies, not replacements for signed DiscUtils packages. Its portable
security component and raw allocation/security preflight stay behind the same
writer boundary. No portable writer capability follows from successful
individual dependency operations; each NTFS volume must pass qualification.
`IStagedImageWriter.ValidateOutput` is mandatory: Services calls it on the
independently reopened output after its own tree/hash checks. Storage compares
supported source metadata against ordered operation semantics; a writer cannot
silently change valid timestamps/attributes/aliases and still pass publication.

| Project | Responsibility | Allowed current project references |
| --- | --- | --- |
| SectorWorks.Core | Storage/session contracts, immutable metadata, workspace/capability, explorer and editing contracts | None |
| SectorWorks.Storage | Read-only containers, bounded block devices, partitions, filesystem adapters and qualified new-image writer | Core, ManagedNtfs |
| SectorWorks.ManagedNtfs | Isolated, pinned DiscUtils NTFS source repair and minimal internal helpers; unchanged public signed Core/Streams packages | NtfsSecurity |
| SectorWorks.NtfsSecurity | Bounded portable security-descriptor parsing, serialization and qualified inheritance; no host principal APIs | None |
| SectorWorks.Platform | Dialog/layout/preferences contracts, JSON settings stores, safe host extraction and private staging-file primitives | Core |
| SectorWorks.Services | Workspace lifetimes, staged overlay/history, Save As orchestration, search, bounded previews/hex, extraction, structured logs | Core, Storage, Platform |
| SectorWorks.UI | Avalonia views, view models, themes, composition root and native dialog adapter | Core, Services, Platform |
| SectorWorks.Tests | Foundation boundary and MVVM tests | UI (transitively shared layers) |

Core, Storage, Services and Platform have no Avalonia references. No core image
access will use host mounting or filesystem drivers. DiscUtils experiments belong
in a separate dependency probe/test project at foundation. The owner approved
DiscUtils 0.16.13 for development, with production-readiness re-evaluation in the
release-quality checklist. Future integration must stay behind replaceable
storage interfaces rather than exposing DiscUtils types to Core, Services or
UI. No operation becomes supported merely because the library exposes its API.

Phase 1 adds an explicit UI -> Platform reference because the Avalonia native
dialog adapter implements `IPlatformDialogs`, and pane persistence consumes
`ILayoutStore`. These are OS-neutral contracts, not storage implementations.
Application sorting preferences likewise use `IPreferencesStore` and
`JsonPreferencesStore` in Platform; production injects the persistent store.
The architecture test's exact allowed graph is updated accordingly; Core and
Storage remain UI-independent, and UI still has no direct Storage reference.

## MVVM conventions

- Avalonia XAML views and UI-specific view models live in UI, not Core/Storage.
- View models inherit `ViewModelBase` (`CommunityToolkit.Mvvm.ObservableObject`).
- Prefer source-generated observable properties and commands in partial view
  models, using `[ObservableProperty]`, `[RelayCommand]` and async commands.
- Use compiled XAML bindings with explicit `x:DataType`.
- Code-behind is limited to view initialization and presentation-only behavior.
- Expose domain/service abstractions to view models, not concrete format readers.
- Keep long operations off the UI thread; expose cancellation/progress/error
  state explicitly. Dispatch presentation updates through Avalonia as needed.
- No service locator, global mutable workspace, OS-specific UI shortcuts, or
  writable capability defaults.

## Explorer and storage boundaries

Planned Phase 6 **6.11** adds a distinct PMFX read-only container handler and
bounded random-access block device ahead of the existing partition/filesystem
layers and shared explorer operations. Owner approval on 2026-10-17 is for
roadmap/documentation only. No PMFX production types or dependencies exist yet.
Phase 6 must deliver direct PMFX opening/browsing/extraction inside SectorWorks;
an external converter or future implementation plan cannot satisfy completion.
Keep decoding, table/fragment validation and cache ownership in Storage, behind
existing contracts; do not leak format/library types to Services/UI, mount
through the host, or expand a raw temporary image for normal access.
Container read-only caps must deny staging/modified Save As/commit/rebuild even
for otherwise writable FAT/NTFS. Recognized unsupported PMFX must not fall
through to Raw. See the [research and qualification plan](pmfx-read-only-plan.md)
for decoder-completion, independent-reference and lifecycle/resource gates.

`IImageOpener` creates an `IImageSession` with a bounded virtual device, partition
metadata and independent read-only filesystem volumes. `IReadOnlyFileSystem`
returns standardized entries and ordinary .NET streams, never DiscUtils types.

`IExplorerService` provides asynchronous operations to UI view models. Its
implementation owns sessions, serializes each image's operations and executes
parsing on workers. Close cannot dispose a stream while another operation is
reading it. The service checks directory-child identities and traversal budgets
in addition to the storage layer's format checks.

`IImageEditingService` is separate from `IExplorerService`; the production
`ExplorerService` implements both. `ImageChangeRequest`, `PendingImageChange`
and `EditingSnapshot` keep ordered history/review contracts UI-independent.
`IStagedImageWriter` isolates qualification, capacity validation and applying
operations to disposable output. It does not expose a source-commit API.
DiscUtils writer types remain within Storage and its isolated implementation
assemblies. Core, Services and UI never consume those types.
The 63-row disk integration suite also references Services to verify actual
staging/history and Save As against the same independently authored container
fixtures; this is a test-only reference, not a reversed production dependency.
`WorkspaceDataFiles` in Platform owns managed permission/link checks for private
staging directories and payload creation. Services does not scatter OS-specific
permission handling through the overlay.

FAT editing uses case-insensitive identity with case-preserving paths. Services
canonicalizes existing parents/targets without normalizing desired names.
Storage retains short-entry display flags and qualifies a separate LFN subset,
including mixed-case and NFC BMP names. Case-only rename does not move file
data. Final output fingerprints compare exact spelling, not just folded
identity or content hashes. See the staged-editing guide for the exact name,
metadata, allocation and resource restrictions; Unicode is not blanket support.

Extraction destination access lives
behind `SafeExtractionTarget` in Platform, using OS directory handles to prevent
symlink/reparse redirection. These small system API integrations are necessary
for race-resistant output creation; they introduce no host-mount dependency and
no external native library. See [explorer operations](explorer-operations.md).

Write/flush members of the read-only block-device contract still reject writes.
Phase 2 editing is a separate overlay and new-image output workflow, not a
writable source device. UI capabilities never infer editing support from
DiscUtils's available APIs.

## Build conventions

Nullable reference types, deterministic builds, checked arithmetic and compiler/
analyzer warnings as errors apply solution-wide. Follow `.editorconfig`; verify
with `dotnet format --verify-no-changes`. Keep package versions explicit and
commit `packages.lock.json`. Deliberate updates regenerate locks through an
unlocked restore and require dependency review and verification.
