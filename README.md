# SectorWorks

SectorWorks is a planned cross-platform disk and optical-image workbench written
in C# with Avalonia and MVVM. It will inspect, browse, extract and, when safely
supported, stage modifications and commit or rebuild images without host-OS
mounting. Source data integrity and capability-based support are central.

**Status: Phase 2B partially delivered; managed NTFS writing implemented for a
qualified subset, with Windows-media interoperability still incomplete.** The supported
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
as recorded in the checklist. Phase 2B adds shared staged modifications and Save As a
new image for individually qualified FAT and ordinary NTFS volumes in raw, fixed/dynamic VHD and
fixed/dynamic VHDX; source images remain read-only.
See [staged editing and safety](docs/staged-editing.md). Direct commit
(Phase 3), ISO rebuild (Phase 4) and audio workflows (Phase 6) are not enabled.

The Phase 2B FAT envelope includes filesystem-only, primary MBR and consistent
GPT layouts, per-volume capabilities and independent multi-volume staging.
LFN/mixed-case/NFC BMP Unicode names are supported in a declared safe subset,
with alias, spelling, allocation and metadata preservation checks. FAT12 remains
limited to conventional 1,474,560-byte volumes; 360K/720K and early-DOS writing
remain deferred. Logical sectors must be 512 bytes; VHDX 512/512 and 512/4096
layouts are separately qualified, not 4Kn.

The owner subsequently authorized implementing and qualifying the managed NTFS
repair. Separately named managed security/backend assemblies remove the portable
ACL blocker without replacing signed DiscUtils packages. All five staged
operations and validated Save As pass 21 disk/container/layout/physical-sector
rows; fresh native fixtures also exercise all eight 512-byte-sector cluster sizes
from 512 through 65536 bytes, resident/nonresident transitions and exact metadata
preservation. There is no host mount or native runtime dependency.

**This is not complete ordinary-Windows-media qualification.** LFS 2.0 logs and
their cached-metadata safety remain blocked; actual Windows-authored NTFS
fixtures and the Windows-only security-reference test remain unverified.
Blank logs and rigorously checked clean LFS 1.1 restart structures are the current
log subset; the latter has synthetic structural evidence, not Windows runtime
evidence. NTFS writing remains required for 1.0, not demoted to read-only.
Formatting, direct source commit, recovery and unsupported NTFS metadata stay
disabled. See the
[qualification ledger](docs/support-matrix.md#phase-2b-qualification-ledger-implementation-authorized-2026-10-15),
[dependency blocker](docs/dependency-evaluation.md#phase-2b-existing-volume-ntfs-blocker-2026-10-15)
and [current verification/platform evidence](docs/platform-status.md).
Physical Mint/X11 workflows and independent FAT/container checks are distinct
from six-target cross-publishing; Windows/ARM64 Phase 2B runtime is unverified.
Current final verification: **956 tests passed, one Windows-only reference
test skipped (957 total)**, zero-warning Release build, locked restore,
formatting/resources/reference-copy checks, all six self-contained publishes
and the Linux x64 AppImage prototype. Six actual Mint/X11 NTFS workflows and
independent native output checks pass with unchanged sources. The preceding
FAT prerelease's 561 passing/zero-skipped tests and five X11 workflows remain
historical evidence, not Windows-media qualification.

### 1.0 scope boundary

The roadmap targets **1.0**. VMDK, VDI, QCOW2, DMG, ext2/ext3/ext4, exFAT,
HFS+ and APFS target **qualified read-only subsets where feasible** in Phase 7.
Their modification/staging, modified Save As, direct commit, rebuild, creation
and formatting are outside 1.0 requirements, not missing required writers.
No post-1.0 writer date is promised. Reader evaluation, legal fixtures and tests
remain required: detection is not browsing/extraction; exact unsupported variants
and infeasible-reader blockers must be documented for owner review, not silently
dropped. This is not a claim that these readers are implemented.

Read-only caps compose across the stack: a read-only container caps even FAT/NTFS;
ext/exFAT/HFS+/APFS stay read-only inside raw/VHD/VHDX. Internal parsers determine
access independently of native host-OS mounting capabilities. Platform limits
require evidence, not assumptions about Windows reading Linux/Mac filesystems.
Core disk, optical and retro write milestones below are unchanged.

### Planned editing coverage

The owner approved a documentation-only roadmap correction on 2026-10-15:
**Phase 2B now precedes Phase 3**. It explicitly schedules staged editing and
validated Save As for partitioned raw images, fixed/dynamic VHD and VHDX,
broader FAT naming (LFN/mixed case/Unicode), and ordinary existing-volume NTFS.
Phase 3 must then qualify direct commit per required disk stack, not just one
combination. Phase 4 covers shared ISO/UDF/bridge staging and rebuilding;
Phase 6 covers BIN/CUE staging and safe output-set rebuilding; Phase 7 item 7.10
covers conventional 360K/720K editing/Save As/direct-commit qualification.

The qualified FAT and managed NTFS subsets above are implemented; remaining
Windows-media qualification is blocked and later milestones remain unimplemented. The owner
separately authorized Phase 2B implementation on 2026-10-15; no Phase 3 work
is authorized. Existing Phase 2 completion remains historical and does not
complete the expanded Phase 2B requirements. See the
[capability delivery matrix](docs/support-matrix.md#required-editing-delivery-roadmap)
and [checklist](checklist.txt) for explicit tasks, preservation/failure gates
and the owner-review requirement for unresolved scope.

## Prerequisites

- .NET SDK **10.0.402**, pinned exactly in `global.json`.
- .NET **10 LTS**, target `net10.0`; validation host runtime **10.0.13**.
- Bash and network access to NuGet for the first restore/publish.
- `qemu-img` for Phase 2B disposable container/interoperability tests. Native
  `fsck.fat`/mtools and NTFS tools add independent checks when installed; none
  is a runtime application dependency.
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

Linux x64 publishes (including `all`) also produce
`artifacts/publish/SectorWorks-linux-x64.AppImage` and its `.sha256` checksum.
Packaging requires a Linux x86-64 host, `desktop-file-validate`, `file`, and the
checksum-pinned appimagetool 1.9.1/runtime installation. Install the tools once
with `./scripts/setup-appimage-tools.sh`; this downloads official binaries into
the user-local SectorWorks tool directory, verifies SHA-256, and changes no
system packages. Ordinary publishes never download/update tools and fail
explicitly if tools are missing or modified. Other RID publishes are unchanged.
Optional `SECTORWORKS_APPIMAGETOOL` / `SECTORWORKS_APPIMAGE_RUNTIME` paths must
contain the same pinned bytes; deliberate pin updates require review.

To package the existing Linux x64 publish without rebuilding:
`./scripts/appimage.sh` (or pass an output `.AppImage` path).
`SECTORWORKS_APPIMAGE_SOURCE` can select an extracted, already-verified Linux x64
release tree instead of the default publish directory.
Make the downloaded AppImage executable with `chmod +x`, then run it.
If FUSE mounting is unavailable, use
`APPIMAGE_EXTRACT_AND_RUN=1 ./SectorWorks-linux-x64.AppImage`.
The launcher preserves the caller's directory and file arguments, including
spaces. The image includes the .NET runtime, application/legal files and
AppImage-runtime notices, but still requires compatible native desktop/system
libraries. It is not a sandbox or a guarantee of portability across all
distributions. The launcher SVG temporarily reuses the existing disk glyph;
final product branding remains deferred.

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
