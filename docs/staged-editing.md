# Phase 2 staged editing and Save As

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

## Qualified editing envelope

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

The whole existing tree must use uppercase DOS 8.3 names consisting of A-Z,
0-9, underscore and hyphen (with one optional extension separator). Long-name
trees remain browseable but read-only for editing. New names obey the same
rules and reject reserved device names. Case-only renames are explicitly
unsupported. No silent truncation, case normalization or long-name loss is
permitted.

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
   the qualified DOS 8.3 rules above.
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
  rechecks the source path, compares copied bytes against that baseline, and
  rechecks the source again before publication. Detected external changes abort
  the save. This is not Phase 3 write locking, commit or recovery.
- Output is flushed to disk, closed and independently reopened through the
  reader pipeline. Its container/filesystem identity, complete expected tree,
  file lengths and SHA-256 content hashes must match the staged view.
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

These findings reinforce the distinction between readable content, consistent
allocation metadata and independently verified output. The approved dependency
version is retained behind the replaceable writer boundary; the release-quality
review remains required.

## Executed verification and reproduction

On Linux Mint 22 x64/X11, 2026-10-14:

- `./scripts/verify.sh`: locked restore, zero-warning Release build,
  **287 passing tests / zero skips**, formatting, UI resources and unchanged
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

The final smoke artifacts are in `artifacts/phase2-verified-x11/`. To repeat it,
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
