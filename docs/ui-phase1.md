# Phase 1 read-only explorer UI

Historical Phase 1 evidence is retained below. Phase 2 adds capability-gated
staging and Save As commands; see [staged editing](staged-editing.md) for current
behavior. The original disabled-write descriptions below describe Phase 1,
not a removal of Phase 2 functionality.

Current Phase 2 Add/New Folder/Rename accept uppercase or lowercase DOS 8.3
components and case-only renames. The browser, pending review and saved output
retain their spelling; mixed case requiring long-name records is reported as
unsupported rather than silently uppercased.

## Boundary and composition

`SectorWorks.UI` contains Avalonia views, presentation-only behavior, view models,
theme resources and the desktop composition root. It consumes `IExplorerService`
and Core records; it does not reference Storage or DiscUtils directly.

Public presentation constructors:

```csharp
new MainWindowViewModel(
    IExplorerService explorer,
    IPlatformDialogs dialogs,
    ILayoutStore layoutStore,
    IPreferencesStore? preferencesStore = null);

new WorkspaceViewModel(
    IExplorerService explorer,
    IPlatformDialogs dialogs,
    WorkspaceInfo info,
    LayoutSettings layout,
    ApplicationPreferences? preferences = null);
```

Normal startup composes `ExplorerService()`, `AvaloniaPlatformDialogs(window)`,
`JsonLayoutStore()` and `JsonPreferencesStore()` behind the Platform interfaces.
Omitting a preferences store in presentation tests uses in-memory defaults only;
production always injects the persistent store.
Image paths passed on the command line are opened asynchronously after the
window opens. A failed image does not close successful tabs, and batch failures
remain visible even if a subsequent image opens successfully.

`--verify-ui` initializes Skia/text shaping and compiled application resources
without starting a windowing backend. It checks all twenty shared icon geometries
and exits nonzero on a resource failure. This resource diagnostic is not a claim
of physical desktop testing.

## Implemented interaction

- Graphite shell with all seven menus, compact labeled vector toolbar,
  independently selectable image tabs, three resizable primary panes and
  persistent read-only status.
- Hierarchical image/table/partition/filesystem tree; directory children load
  lazily and failed loads can be retried. Unpartitioned byte ranges are explicitly
  labeled as potentially containing disk metadata, not as verified free space.
- Dense, virtualized DataGrid with sortable/resizable Name, Size, Type,
  Modified, Attributes and Offset columns, multi-selection, context menu,
  keyboard/double-click folder navigation and clickable path breadcrumbs.
  Narrow windows retain the hierarchy/inspector and horizontally scroll columns.
- Copyable inspector and selected-file properties, complete capability reasons,
  opening warnings, bounded non-executing text preview and 4 KiB hex pagination.
  Hex page navigation stops at the selected content bounds.
- Whole-image filename search across readable volumes with cancellation and
  navigation from a result to its containing directory.
- Extraction of selected files/directories through the service, native folder
  picker, progress, per-file failures and safe cancellation. The service owns
  path/name/link safety and create-new/no-overwrite policy; the UI reports it.
  Cancelling may leave already-completed extracted files, never image writes.
- Per-tab directory, selection, tree expansion, tools and pane dimensions remain
  in presentation state. The last active layout is saved through `ILayoutStore`
  on tab detachment/window close. Invalid/unreadable layout settings are reported
  and fall back to default dimensions.
- Superseded directory/preview results are discarded. A failed switch to another
  volume does not associate old rows with the new volume. Closing cancels the
  workspace before the service closes its underlying session.

### Phase 1 refinements: sorting, Preferences and navigation

New installations sort folders first and then files, each by case-insensitive
ordinal name ascending. Equal case-insensitive names have ordinal name/path
tie-breakers, independent of the host's culture. Folders-first grouping is
independent of ascending/descending direction.

**File → Preferences…** opens a real dialog for the default sort field,
direction and folders-first toggle. Name, Size, Type and Modified are supported;
Attributes and Offset remain available so all existing sortable columns retain
interactive sorting. Type uses the displayed folder/file-extension type.
Clicking a column toggles its sort direction and shows an arrow. Interactive
sorting is per tab and does not silently overwrite application defaults.

`ApplicationPreferences`/`IPreferencesStore` are Platform contracts.
`JsonPreferencesStore` persists `SectorWorks/preferences.json` under
`Environment.SpecialFolder.LocalApplicationData`. It validates enum values and
JSON members, reports malformed/inaccessible settings, and replaces the file
through a unique same-directory replacement file. Missing files use defaults;
read errors do not silently masquerade as a new installation. The UI displays a
persistent warning for load/save failures. Cancel does not write or apply edits;
Save applies to open tabs only after persistence succeeds. Saving cannot be
cancelled mid-replacement. Open-tab selection, including a 50,000-row select-all,
survives re-sorting without realizing the entire grid.

Every successful directory navigation resolves its canonical lazy tree node,
expands its ancestors and commits the displayed directory and tree selection
together. This covers double-click/Enter, breadcrumbs, parent navigation and
search-result navigation. Tree-initiated requests do not move the committed
highlight while pending or on failure; cancelled/superseded results cannot
change it. Lazy nodes share in-flight loads. Programmatic tree synchronization
does not issue recursive navigation requests. Directory results populate the
tree's child cache, avoiding redundant loads during normal browser navigation.

Shortcuts: Ctrl+O open, Ctrl+W close, Ctrl+E extract, F5 refresh,
Alt+Up/Backspace parent, Enter open/preview or navigate a search result,
Ctrl+F (also Command+F) focus search, Escape cancel. Standard tree, tab, grid and
text-copy keyboard behavior is retained.

Add, Replace, Delete, New Folder, Commit and Rebuild remain unavailable.
Audio Player and Track Information are disabled Phase 6 reservations.
ISO trees also have a clearly unavailable Phase 6 session/track reservation:
an informational node with no volume/path, children or enumeration behavior.
Image Map is visibly reserved, not implemented. Validation displays opening
warnings and explicitly states that this is not a full filesystem validator
or a bootability guarantee. Nothing executes image content.

## Dependency and icon review

- Avalonia/DataGrid is the existing toolkit's maintained, managed grid, needed
  for virtualization, column sorting/resizing and desktop multi-selection.
  **DataGrid 12.1.2** is the latest available stable package observed during
  implementation; **12.1.4 does not exist** for this separately versioned package.
  Its NuGet manifest requires Avalonia >=12.1.0 and supplies net8/net10 assemblies,
  compatible with the pinned Avalonia **12.1.4** application.
- DataGrid 12.1.2's MIT license was checked against upstream commit
  `1e12211eb0c52a09e314ca57a344147c37d0a26c`. It matches the retained
  `docs/licenses/Avalonia-MIT.txt`. It adds no native dependency.
- Tests add **Avalonia.Headless 12.1.4**, MIT, only to the test project; it uses the
  already-present Skia desktop renderer for actual offscreen rendering.
  Existing xUnit/VSTest versions are reused.
- Functional artwork is the original **SectorWorks Compact** family:
  twenty shared `StreamGeometry` resources in `Themes/Icons.axaml`, GPL-3.0-only.
  No external artwork was copied/traced. Attribution/license is in
  `Themes/ICONS-LICENSE.txt`, copied into build/publish `licenses/`.
  Geometry has no embedded color; PathIcon colors use theme resources.
  External product branding remains separate and is not supplied here.

## Executed verification (Linux x64, 2026-10-14)

```sh
dotnet test tests/SectorWorks.UITests/SectorWorks.UITests.csproj \
  --no-restore -c Release
dotnet test tests/SectorWorks.ServicesTests/SectorWorks.ServicesTests.csproj \
  --no-restore -c Release --filter FullyQualifiedName~PreferencesTests
dotnet format src/SectorWorks.UI/SectorWorks.UI.csproj \
  --no-restore --verify-no-changes
dotnet format tests/SectorWorks.UITests/SectorWorks.UITests.csproj \
  --no-restore --verify-no-changes
env -u DISPLAY -u WAYLAND_DISPLAY dotnet run \
  --project src/SectorWorks.UI --configuration Release -- --verify-ui
```

Twenty-four UI tests and eleven focused Platform-preferences tests pass with no
skipped tests. UI tests cover workflow/service calls,
multi-tab failure isolation, multi-selection and visual selection restoration,
lazy tree expansion/retry, late navigation/preview rejection, failed-volume
navigation, close/open cancellation, extraction failures, search, hex boundaries,
layout save/collapse, a persistent invalid-layout warning even after command-line
image opening, selected-volume capability restrictions overriding aggregate
workspace availability, keyboard focus and compiled XAML instantiation. A
50,000-entry directory verifies that the grid realizes at most 100 rows.
Multi-selection property text is capped at 32 entries; extraction error text
shows the first 100 failures and the total, avoiding unbounded presentation text.
Modified timestamps are rendered according to `DateTime.Kind`: UTC is labeled
only for known UTC values; Local includes its offset; FAT/other unspecified
timestamps explicitly say `timezone unknown`. No timezone conversion is inferred.
Refinement tests additionally cover default/directional sorting, deterministic
ties, preference reload/error/cancel/save behavior, actual File-menu dialog
opening, actual TreeView highlights after navigation/failure/cancellation/late
results, duplicate-load prevention and selection-preserving virtualized sorting.
A real pointer click on the Name header verifies descending sorting still keeps
folders before files. The UI handles the grid sorting event itself, preventing
the DataGrid from reversing folder grouping a second time.

Skia-backed headless windows render at actual **100%, 125%, 150%, 200%**
render scaling. All twenty icons render in an 80-icon contact sheet at
**16/18/20/24 logical pixels** at those scales. Minimum logical size is
**1000 × 640**. Tests assert usable browser height, breadcrumb controls,
nonempty rendered frames, scaling, icon bounds and primary/semantic text
contrast of at least **4.5:1** against the graphite pane surface.

Physical **Linux/X11 (`DISPLAY=:0`)** smoke windows were opened without activation,
exercised automatically and closed. The final actual `ExplorerService`/storage
pass covered all **nine normal goldens**: FAT12, FAT16 and FAT32 raw, MBR/FAT16,
GPT/FAT16, NTFS, dynamic VHD, dynamic VHDX and Joliet ISO. Each workflow read
directory contents, previewed, read a hex page, searched, navigated results,
extracted a file and closed the workspace; available subdirectories were also
opened and their parent directory restored. Each image's SHA-256 before/after
the complete window/view-model workflow matched. All final nine runs used the
minimum window size. FAT12 physical runs separately used
process-local `AVALONIA_SCREEN_SCALE_FACTORS=1.25`, `1.5`, `2`; the reported
render scales were 1.25/1.5/2. These do not alter the desktop's global settings.
The normal production executable was separately launched with an image argument;
its own X11 window appeared and exited cleanly with code 0 after a targeted
`WM_DELETE_WINDOW` message. This did not close or manipulate other applications.

The refinement physical X11 smoke additionally opened the real Preferences
window, saved/reloaded sorting settings through `JsonPreferencesStore`, verified
selection preservation, navigated directories/search results and checked the
actual TreeView selection against the displayed directory. It closed cleanly;
`artifacts/ui-phase1/preferences-navigation.png` captures only the application
window. This smoke uses an isolated settings file in its new extraction folder,
not the user's real preferences.

### Actual legacy Compaq floppy smoke

The current physical X11 harness also passed against the owner's original
`Compaq Portable Diagnostic Disk.img`: **737,280 bytes, FAT12, label DIAGS,
10 root entries**. The app displayed the legacy missing-signature explanation,
browsed the real files, read hex, searched/navigated README.TXT, saved/reloaded
isolated Preferences and verified the actual tree highlight. No image content
was executed. The harness prefers a regular, non-link README.TXT when present;
otherwise it retains the first-safe-file fallback used for the goldens.

README.TXT was extracted at its reported **1,156 bytes**, to
`artifacts/ui-phase1/compaq-1791976207/README.TXT`, with SHA-256
`10a4c718fa34751fc29d871003f2ad8caaffcf0a4f33cad86c8be05d5f764583`.
The original image SHA-256 matched before and after the workflow:
`313ff1d0692ca4a54b3c1c99dd0ba33624be46caf1396c4873b0b5a9532507f1`.

The README does **not** render as plain UTF-8 preview: the existing safe preview
handler reports unsupported encoding/binary content and directs the user to Hex
View. This is an explicit preview limitation, not a claim of successful legacy
text decoding. App-only screenshot: `artifacts/ui-phase1/compaq-desktop.png`.

Reproduce a nonactivating physical smoke after the test build:

```sh
mkdir -p artifacts/ui-phase1
gzip -dc fixtures/golden/fat12.img.gz > artifacts/ui-phase1/fat12.img
DISPLAY=:0 dotnet tests/SectorWorks.UITests/bin/Release/net10.0/SectorWorks.UITests.dll \
  artifacts/ui-phase1/fat12.img artifacts/ui-phase1/new-extraction \
  artifacts/ui-phase1/desktop.png minimum
```

Choose a new extraction folder on each run: duplicate files are deliberately
refused. The smoke executable exits itself and has a 45-second UI deadline.
Screenshots are uncommitted local evidence in `artifacts/ui-phase1/`, with the
final nine named `final-<image name>.png`. Screenshots use
`RenderTargetBitmap.Render(window)`: **only the application's own window content
is captured**, never the shared desktop or another application's content.

## Remaining validation and limits

Native picker interaction, pointer-operated splitter dragging and exhaustive
assistive-technology behavior still warrant human review. The automated physical
smoke injects destination paths rather than operating a native picker.
The automated UI work above ran on Linux x64 only. Separately, on 2026-10-14 the
owner reported Phase 1 working on Linux x64, Windows x64 and Linux ARM64 on a
Raspberry Pi 500; Windows 7 x64 also reportedly runs this build but is unsupported.
macOS launch remained blocked for the unsigned distribution, including after an
attempt to disable security; exact cause and tested architecture remain unknown.
Windows ARM64, successful macOS execution and explicit Wayland coverage remain
unverified. See [platform status](platform-status.md) for evidence boundaries.

The owner's missing-first-page report was reproduced and fixed on 2026-10-14:
selecting the Hex View tab now executes the bounded hex read rather than merely
showing its uninitialized text. The command-running guard prevents a toolbar or
paging command's programmatic tab activation from issuing a duplicate read.
Explicit hex requests cancel pending text preview, preventing late text results
from switching the active tool back. Text-decoding restrictions are unchanged.

Nineteen added tests bring the UI suite to 43 passing tests. They exercise actual
tab selection, exact raw bytes through generated ISO files and the real service,
0/1/15/16/17/1494/4096/4097/9000-byte lengths, paging and late preview completion.
Headless session tests are serialized in one collection because Avalonia uses
process-global platform services. The physical smoke now activates the actual
tab initially and again after search navigation. It passed with the supplied
1494-byte LEVEL01.OPL in a disposable ISO; extraction and original hashes match.
See [platform status](platform-status.md) for the sample hash and evidence limits.

Full filesystem validation, graphical mapping, editing,
rebuilding, optical sessions/tracks and audio playback are not enabled.
No later-phase implementation is included.

Final bounded audit found no TODO/FIXME/implementation stubs or skipped tests in
owned UI/test sources. Disabled future commands/tabs are deliberate and labeled.
The physical smoke checks errors after each workflow stage and after close,
so a later successful command cannot conceal an earlier stage's failure.
