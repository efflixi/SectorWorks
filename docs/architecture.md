# Architecture

The foundation's layered solution is retained. Phase 1 supplies the read-only
pipeline and Avalonia explorer. Phase 2 adds staged modifications and Save As
through separate editing contracts. Direct commit, rebuild and audio remain
later-phase work. See [staged editing](staged-editing.md).

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
