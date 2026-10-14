# Build and physical-platform status

## Phase 2 target policy

On 2026-10-14 the owner authorized Phase 2 and explicitly retained **macOS as
an intended supported target**, despite not having a Mac available for reliable
testing. Both macOS cross-publish targets remain required. Runtime/workflow,
bundle signing and notarization validation are deferred until suitable access
is available; this does not block Phase 2. Successful cross-publishes do not
establish physical compatibility or resolve the earlier launch report.

The physical results below are historical Phase 1 evidence, not Phase 2 write
workflow validation on Windows or ARM64.

## Phase 2 verified evidence

Verification date: 2026-10-14. Host: Linux Mint 22 x86-64, X11.
SDK 10.0.402 / runtime 10.0.13.

Final locked restore, Release build with zero warnings/errors, **287 tests**
with zero skips, formatting, UI resources and all authoritative reference-copy
comparisons passed. Counts: 128 services/platform, 67 storage, 62 UI (including
private staging-platform primitives), and 30 foundation/dependency tests.
No tests were disabled to obtain these results.

| Runtime identifier | Phase 2 self-contained cross-publish | Phase 2 physical runtime |
| --- | --- | --- |
| linux-x64 | Passed; ELF x86-64 header checked | Mint 22/X11 staged-edit workflow passed |
| linux-arm64 | Passed; ELF AArch64 header checked | Not retested for Phase 2 |
| win-x64 | Passed; PE x86-64 header checked | Not retested for Phase 2 |
| win-arm64 | Passed; PE AArch64 header checked | Unverified |
| osx-x64 | Passed; Mach-O x86-64 header checked | Explicitly deferred per owner |
| osx-arm64 | Passed; Mach-O arm64 header checked | Explicitly deferred per owner |

All six existing publish folders were refreshed with the final code and legal
notices. The published Linux x64 executable also passes `--verify-ui`.
No Mac runtime, signing, notarization or physical compatibility claim is made.

The real X11 smoke creates its own clean disposable FAT12 image; it exercises
all five edit types, staged markers, ordered review, undo/redo, preview,
extraction, Save As, independent output reopen, cancelled close and discard.
The original generated source hash is unchanged. The final output passes
`fsck.fat -n` (dosfstools 4.2) and `mdir` (mtools 4.0.43), including valid renamed
file and directory-special-entry dates. The app-only screenshot and output are
in `artifacts/phase2-verified-x11/`.

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
| osx-x64 | Passed | Runtime unverified; macOS launch blocker reported, tested architecture unspecified |
| osx-arm64 | Passed | Runtime unverified; macOS launch blocker reported, tested architecture unspecified |

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

**macOS testing is blocked at launch.** The owner reports that the unsigned
distribution was blocked even after attempting to disable security. Successful
runtime/workflow verification has therefore not occurred. macOS version, CPU
architecture and exact diagnostics were not supplied; signing/packaging is a
reported concern, not a confirmed sole cause. Investigate the launch diagnostics,
binary/library signatures and distribution packaging on a Mac before claiming
compatibility. Do not treat disabling system security as a verified workaround.

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
format/resource checks and synchronized specifications. All six self-contained
publish folders were refreshed successfully with the fix; this does not resolve
the existing macOS signing/launch blocker.

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
text preview; Hex View remains available. All six refined cross-publishes pass.

Skia-backed UI tests exercise 100/125/150/200% scaling, twenty vector icons at
16/18/20/24 logical pixels, a 1000x640 minimum window and text contrast >=4.5:1.
Linux/X11 physical smoke additionally used process-local 125/150/200% scales.
A 50,000-entry grid realizes at most 100 rows. These results are not Windows,
macOS, Wayland or ARM64 validation.

Native picker operation, pointer splitter dragging, clipboard integration and
exhaustive assistive-technology testing require human desktop review. Smoke
injects extraction destinations rather than operating native dialogs. The owner
reports above add Windows x64 and Raspberry Pi 500 runtime evidence, but do not
complete that detailed review. Windows ARM64, successful macOS runtime execution,
explicit Wayland coverage, the full Linux distribution matrix and Raspberry Pi
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
| osx-x64 | Passed on Linux host | Not run on Intel Mac |
| osx-arm64 | Passed on Linux host | Not run on Apple Silicon Mac |

At foundation completion there was no Avalonia window. Those results did not verify
desktop startup, image browsing/extraction, minimum OS versions, dialogs,
clipboard, DPI, audio, packaging, signing or notarization. Raspberry Pi 5,
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

| Dependency | Linux x64 / ARM64 | Windows x64 / ARM64 | macOS x64 / ARM64 |
| --- | --- | --- | --- |
| SkiaSharp 3.119.4 | ELF assets for both | PE assets for both | Universal Mach-O with both slices |
| HarfBuzzSharp 8.3.1.3 | ELF assets for both | PE assets for both | Universal Mach-O with both slices |
| Avalonia.Native 12.1.4 | Not used | Not used | Universal Mach-O with both slices |
| Avalonia ANGLE 2.1.27548.20260419 | Not used | PE assets for both | Not used |

The .NET runtime/apphost is likewise target-specific ELF, PE or Mach-O with the
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
all six RIDs solution-wide and restoring their committed lock graph without a
single-RID override. All six subsequent publishes passed; scripts do not bypass
locked restore to hide failures.

## Resolved audio scope

On 2026-10-13 the owner confirmed that the detailed checklist governs audio scope.
BIN/CUE support and functional CD-DA playback belong to Phase 6. Phase 1 reserves
the optical/audio structure and pane tabs without implementing these workflows.
The UI specification and supplemental guide were corrected, with repository
documentation copies kept synchronized. The optical/audio mockup remains the
approved later-phase visual direction. Phase 1 was subsequently authorized on
2026-10-14; it reserves those panes without implementing playback.
