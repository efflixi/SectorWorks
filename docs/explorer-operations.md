# Explorer operations

`IExplorerService` is the UI-facing boundary. Core contracts contain no Avalonia
or DiscUtils types. The service composes the approved storage implementation
internally. All image input handles remain read-only; extraction writes only to
the explicitly selected host destination. Phase 2 adds a separate editing
boundary and validated new-image output; see [staged editing](staged-editing.md).

## Workspaces and threading

Each open image has an independent ID and serialized operation gate. Synchronous
filesystem parsing, search, preview and extraction execute on worker threads.
Close waits for active operations before disposing the underlying session.
Cancellation is checked between reads and traversal steps and passed to storage.
The UI must cancel its current operation before closing a busy tab.

Opening errors propagate without closing other workspaces. A workspace with no
supported filesystem can still expose metadata and raw image hex. Editing is
available only for the separately qualified Phase 2 stack. Commit, rebuild,
audio and preservation capabilities remain unavailable.
Structural checks on open are not advertised as full filesystem validation.

## Extraction policy

- Existing host files are **never overwritten**, including duplicate names.
- Names unsafe on either supported host family are rejected rather than silently
  renamed: traversal components, absolute paths, separators in names, control
  characters, Windows reserved names, trailing dots/spaces and alternate streams.
  Unicode names remain allowed.
- Selection metadata is re-read from the image rather than trusted from the UI.
- Directories are traversed iteratively with 128-level and 100000-entry limits.
  Links/reparse points inside images are reported and never followed.
- The selected destination must already exist. Existing ordinary directories may
  be reused, but each file is created exclusively. Empty directories are retained.
- Files are streamed in 64 KiB chunks; lengths must match metadata. Completed
  files are flushed before completion is reported. Per-file failures appear in
  the result and log. A failed/cancelled file is removed; completed earlier files
  remain. Empty directories created before cancellation may remain.
- Cancellation does not claim rollback of previously completed extraction files.

### Host link/race protection

String prefix checking and checking a path for symlinks before opening are
insufficient: a directory could be replaced between the check and the write.
`SafeExtractionTarget` isolates the necessary native OS primitives in Platform.
No native package is added, and these APIs are **not** used to mount images or
interpret their filesystems.

Linux/macOS traversal uses directory handles and `openat` with `O_NOFOLLOW`/
`O_DIRECTORY`, `mkdirat`, and exclusive no-follow file creation. Each component
is resolved relative to a held directory; an existing host symlink cannot redirect
extraction. Partial files are removed relative to that same directory handle.
Replacing the selected root pathname does not redirect the held original
directory. No OS can promise an immutable absolute pathname against an owner
who relocates that directory; the extraction target is the originally opened
directory, not a later symlink substituted at its old name.

Windows opens directories without following reparse points, checks their tags,
and keeps ancestor handles without write/delete sharing for the operation.
Creation uses `CREATE_NEW`; reparse targets and sharing conflicts fail explicitly.
Runtime validation on real Windows/macOS machines is tracked separately from
cross-compilation. Ordinary local directories are the intended destination;
network/filesystem providers with different handle semantics require validation.

## Preview, hex and search

Plain-text preview reads at most 64 KiB plus one byte to indicate truncation.
UTF-8 and BOM-marked UTF-16 are supported. Binary/control-containing or invalid
text is explicitly described as unsupported; Hex View remains available.
There is no shell, executable launch, HTML renderer or image-file execution.

Hex View reads a 4 KiB page from a selected ordinary file or raw virtual image.
Offsets are checked; pages retain absolute byte offsets and EOF/truncation status.
No complete image or large file is loaded to render a page.
Preview/file hex verify stream length against entry metadata and reject
premature EOF instead of presenting incomplete data as a successful page.
Raw-device hex completes partial reads and likewise rejects premature EOF.

The owner's missing-first-page issue was fixed on 2026-10-14 in presentation:
selecting the Hex View tab now loads the current bounded page, without requiring
Next/Previous. Explicit hex requests cancel pending text previews to prevent
late results from switching the tool back. Encoding checks remain text-only.
Regressions cover empty/short files, partial rows, page boundaries and explicit
hex activation after unsupported text preview; the supplied LEVEL01.OPL also
passed real Mint/X11 testing in a disposable ISO.

File timestamps retain their stored meaning. FAT timestamps have unspecified
timezone and must not be converted or labeled as UTC; UTC-backed timestamps can
be labeled UTC. The browser uses a neutral Modified column.

Search covers **filenames across all supported volumes in the current image**,
case-insensitively; it does not scan file contents. It is cancellable, returns
volume/path identity for navigation, and limits traversal to 100000 entries,
depth to 128 and results to 10000. Limit violations fail with actionable errors,
not incomplete results disguised as a complete search. Links are not traversed.

## Logs and settings

Structured JSON-line logs live under the user's local application-data
`SectorWorks/explorer.jsonl`, rotating to one `.previous` file after 4 MiB.
They record stages, source paths, capability decisions, warnings and failures;
file contents and preview text are never logged.

Pane dimensions are stored in `SectorWorks/layout.json`. Writes use a separate
temporary file and replacement. Invalid settings throw explicit errors so the UI
can explain a reset, rather than silently discarding the user's configuration.

Application sorting defaults are stored separately in `SectorWorks/preferences.json`.
File > Preferences exposes sort field, direction and folders-first grouping.
The initial order is folders first, then files, each by case-insensitive name
ascending with deterministic ties. Direction never reverses the group order.
Interactive column sorts are per-tab and do not overwrite application defaults.
Cancel changes nothing; Save persists before applying to open tabs, preserving
selection. Load/save failures remain visibly reported.

Successful directory navigation commits the browser path and canonical lazy-tree
selection together, expanding ancestors. Failed, cancelled and superseded
navigation cannot change the committed highlight or cause recursive navigation.

Legacy OEM text encodings are not supported by the current UTF-8/UTF-16 preview
handler. The Compaq README explicitly offers Hex View instead; successful file
browsing/extraction does not imply support for its text encoding.
