# SectorWorks

SectorWorks is a planned cross-platform disk and optical-image workbench written
in C# with Avalonia and MVVM. It will inspect, browse, extract and, when safely
supported, stage modifications and commit or rebuild images without host-OS
mounting. Source data integrity and capability-based support are central.

**Status: Phase 2 staged file modification verified and ready for owner review.** The supported
reader subset was implemented and verified on Linux Mint 22 x64/X11. Owner-reported
physical testing on 2026-10-14 also found Phase 1 working on Linux x64, Windows
x64 and Linux ARM64 (Raspberry Pi 500). Windows 7 x64 reportedly runs the current
build but remains unsupported. macOS remains an intended supported target;
runtime/signing validation is explicitly deferred per owner while Phase 2
proceeds. The earlier launch blocker is unresolved, not confirmed to be solely
signing-related. Cross-publishing does not establish physical compatibility.
The reported missing first hex page is fixed: selecting the Hex View
tab now loads it without a paging workaround, including for small binary files;
see [platform status](docs/platform-status.md) for verification.
Advanced NTFS reads and manual desktop integration review remain incomplete,
as recorded in the checklist. Phase 2 adds staged modifications and Save As a
new image for individually qualified raw/FAT combinations; source images remain
read-only. See [staged editing and safety](docs/staged-editing.md). Direct commit
(Phase 3), ISO rebuild (Phase 4) and audio workflows (Phase 6) are not enabled.

Latest Phase 2 verification, including the short-name case refinement:
**313 tests passed, zero skipped**, zero-warning
Release build, formatting/reference-copy/resource checks, all six refreshed
self-contained publishes, and a real Linux Mint/X11 staged-edit/Save As workflow.
FAT12/FAT16/FAT32 outputs pass independent `fsck.fat -n` and mtools checks.
Editing requires a qualified unpartitioned conventional raw FAT image with a
DOS 8.3 tree; FAT12 writes are limited to 1,474,560-byte floppy images.
Uppercase and lowercase short names are supported, with case-insensitive lookup
and preserved display case. Each basename and extension must independently be
entirely uppercase or lowercase, such as `ctmouse.exe` or `README.txt`.
Case-only file/directory renames are supported; mixed case within a component
(such as `Readme.txt`) requires long-filename records and remains unsupported.
Long-name trees, partitioned images, VHD/VHDX, NTFS and ISO remain nonwritable.

## Prerequisites

- .NET SDK **10.0.402**, pinned exactly in `global.json`.
- .NET **10 LTS**, target `net10.0`; validation host runtime **10.0.13**.
- Bash and network access to NuGet for the first restore/publish.
- Primary host: Linux Mint 22 x86-64 / VS Code.

Install that SDK using Microsoft's [.NET installation instructions](https://learn.microsoft.com/dotnet/core/install/).
Scripts find `dotnet` on PATH, or the optional user-local installation at
`~/.local/share/SectorWorks/dotnet/dotnet`. Set `SECTORWORKS_DOTNET` to an absolute
executable path to override discovery. No user-machine path is committed.

.NET 10 is the current LTS baseline. Legacy Windows 7/8 are not supported by this
runtime and will not drive architecture. The owner's Windows 7 x64 success report
is an observation about this build, not a change to runtime support policy or a
future compatibility guarantee. Minimum OS/library versions and actual runtime
compatibility remain subject to further physical validation.
The [.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
lists .NET 10 LTS support through November 14, 2028. Patch/SDK updates should be
deliberate, verified changes rather than unpinned automatic upgrades.

## Local commands

Run from the repository root (scripts also work from other directories):

```sh
./scripts/restore.sh
./scripts/build.sh
./scripts/test.sh
./scripts/verify.sh
./scripts/run.sh
./scripts/run.sh /absolute/path/to/image.img
./scripts/publish.sh linux-x64
./scripts/publish.sh all
```

Restore uses committed dependency locks. Build/test use Release configuration.
Verify compares authoritative document copies, checks shell syntax, and runs
tests, formatting and noninteractive UI verification. Each failing stage is
named and returns nonzero; no remote CI or Windows IDE is required.
The solution test stage includes architectural/MVVM, storage, explorer
operation and rendered UI tests plus isolated dependency experiments. Immutable fixture hashes,
bounded malformed-input cases and large virtual-address probes are retained.
Publish produces self-contained, untrimmed, non-AOT outputs in
`artifacts/publish/<rid>/`, with GPL and third-party notices. All six targets:
`linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`.
Native desktop libraries are still required; self-contained does not mean a
statically linked GUI. Cross-publish is **not** physical platform validation.

The browser defaults to alphabetical folders first, then alphabetical files.
Use **File > Preferences** to choose the default sort field/direction and
folders-first grouping. Settings persist across restarts; column clicks remain
per-tab overrides. Browser navigation also selects/reveals the current directory
in the structure tree. See the UI documentation for error/cancellation behavior.

For an intentional package update, run an unlocked `dotnet restore SectorWorks.sln`,
review resulting locks and licenses, then rerun verification and all publishes.

## Documentation and task authority

Read [AGENTS.md](AGENTS.md) and [rules.txt](rules.txt) before every task.
[checklist.txt](checklist.txt) is the authoritative progress tracker.
Current specs and design references are in [docs](docs/); supplied root originals
remain authoritative and documentation copies must remain synchronized.

- [Architecture/MVVM](docs/architecture.md)
- [Dependency evaluation](docs/dependency-evaluation.md)
- [Fixture provenance and verification](docs/fixtures.md)
- [Build and physical-platform status](docs/platform-status.md)
- [Read-only explorer operations and extraction policy](docs/explorer-operations.md)
- [Phase 1 read-only support matrix and limitations](docs/support-matrix.md)
- [Phase 2 staged editing and Save As safety](docs/staged-editing.md)
- [UI behavior, physical-smoke reproduction and review areas](docs/ui-phase1.md)
- [Storage safety limits and tested reader subset](docs/storage-phase1.md)

Dependency decision, 2026-10-13: keep DiscUtils 0.16.13 for development, accepting
its maintenance risk and possible later replacement/rework. Re-evaluate in the
checklist's release-quality section once other production-readiness work has
matured, before production release. NTFS formatting fails on Linux because of
Windows principal/SID APIs; ordinary NTFS reads passed, but portable NTFS
writes/formatting are not established. Capability and safety testing remain
mandatory throughout development.

Audio scope was resolved by the owner on 2026-10-13:
follow the checklist, with BIN/CUE and functional audio playback in Phase 6.
The UI specification and companion guide now reflect that decision; Phase 1
only reserves the relevant structure/tabs. Phase 1 was authorized on 2026-10-14;
no audio implementation is started. See the evaluation and platform-status
documents for details.

Retro-computing priority, 2026-10-14: **360K and 720K PC floppy images** are
important product requirements. The tested Phase 1 read-only 720K Compaq/legacy
FAT12 subset is retained. Broader variants and dedicated 360K qualification are
planned in checklist Phase 7, item 7.10; neither capacity alone nor current FAT12
support implies blanket compatibility, writing or bootability preservation.

## License

SectorWorks original code is licensed under **GPL-3.0-only**, selected by the
project owner. See [LICENSE](LICENSE). This does not relicense third-party
dependencies. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
Supplied design/specification materials and mockups are retained as owner-provided
references; no third-party artwork rights are inferred from the code license.
