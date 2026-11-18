# SectorWorks

[v0.5.2-disc-layout development prerelease: packages, verification and limits](docs/prerelease-v0.5.2.md).

## Phase 5 — BIN/CUE and CD audio

Open a supported CUE sheet to browse session tracks, extract files, or stream
CD audio directly from sectors. Qualified data tracks can use staged editing
and rebuild to a new folder containing the CUE and its companions. The
[Phase 5 report](docs/phase5-bin-cue.md) describes the supported layouts, output
safety and playback requirements.

Open **Disc Layout** to see each CUE index and gap in disc order, with its
image-relative start, sector count, and backing file position where stored.

To save CD audio, open **Track Information** or **Audio Player** and choose
**Export Audio Tracks…**. Select tracks (or **Select All**) and choose a
destination folder. SectorWorks writes one lossless WAV per selected audio
track, even if the first track is data. It never modifies the image or
overwrites existing files.

## Phase 4 — optical rebuild

Supported ISO 9660, Joliet, Rock Ridge and bounded UDF 1.02/ISO-UDF bridge
images can use shared staged editing and **Rebuild to New Image**. The source
remains read-only; optical Commit is unavailable. A bounded El Torito subset
preserves complete boot files and the catalog at original addresses, without
a general bootability promise. See the [Phase 4 envelope and evidence](docs/phase4-optical-rebuild.md).
The [local review packages](artifacts/phase4-review/packages/manifest.json)
include the four Windows/Linux RID builds and the Linux x64 AppImage. For the
independent optical tests on Linux, install `genisoimage` (with `isoinfo`) and
`7z`; they are not application runtime dependencies.

## Remaining roadmap

| Phase | Work |
| --- | --- |
| 5 | BIN/CUE and optical expansion: data and mixed-mode tracks, CD-DA playback, and safe rebuilds |
| 6 | Additional formats and filesystems, including 360K/720K floppy compatibility (6.10) and DiskGenius PMFX read-only support (6.11) |
| 7 | Linux and ARM64 hardening: broad Linux x64 distro/backend testing and packaging after both feature phases |

Each feature phase still verifies what it adds; the broad platform sweep tests
the combined result in Phase 7. Physical ARM64 testing remains deferred
post-1.0. Phase 4 remains at owner review; the reorder does not start Phase 5.

## Prior 1.0 scope and Phase 2/3 closeout — November 17, 2026

**Phase 2, 2B, 2C, 2D and Phase 3: COMPLETE / OWNER ACCEPTED UNDER THE REVISED
1.0 SCOPE.** See the [owner decision](docs/sectorworks_1_0_scope_change_and_phase_closeout_sol_prompt.txt),
[exit audit and follow-up IDs](docs/1.0-scope-closeout.md),
[current review](docs/phase2d-phase3-review.md) and
[closeout evidence](artifacts/scope-closeout-20261117/evidence-index.json).
This is not a completed 1.0 product or published release.

All qualified FAT/NTFS readers and writers remain, including bounded structural
legacy/list rows and native passive NTFS3.1 with 512-byte sectors, 4096-byte
clusters/index blocks, 1024-byte MFT records, exactly `0x1400` flags, paired clean
LFS1.1 pages and exact empty repair/deleted-state/preservation checks. Default
TxF-enabled/transaction-bearing and other unqualified states stay **read-only**;
no safety gate or system metadata is removed.

**P1.NTFS.MODERN.1–.6** in [the checklist](checklist.txt) track the explicitly
**DEFERRED POST-1.0** TxF/CLFS/KTM checkpoint/reference, unsupported repair/deleted,
coherent detached LFS2/cache and dependent writer work. Existing authentic legacy
follow-up **P1.NTFS.1–.6** is preserved. Physical ARM64 is **NOT VERIFIED — DEFERRED
POST-1.0 BY OWNER**, with separate **P1.ARM64.LINUX** and **P1.ARM64.WINDOWS** tasks.
Owner owns hardware for later testing; retain four-target code/build/packages,
not expanded physical runtime claims. Neither deferral is a 1.0 phase/release gate.

[Four existing runnable packages and Linux x64 AppImage](artifacts/focused-ntfs-20261117/packages/manifest.json)
retain their exact tested identities. That scope-only closeout did not rebuild
binaries or claim a fresh full product test run. Phase 4 was authorized separately
and is documented above; no later phase, push, tag or release is authorized.

Commit is available through **Image → Commit Changes to Source**, the toolbar,
and **Ctrl+S**. **Ctrl+Shift+S** creates a validated new image with Save As and
retains staging. Commit requires explicit confirmation, verifies a retained
backup and temporary output, replaces the source atomically and refreshes history.
Startup recovery and **Tools → Interrupted Commit Recovery** inspect a bounded
private catalog; verified cleanup never overwrites a source. Keep copies for testing.

The October/November investigation narrative below is historical. The November17
scope/acceptance decision above supersedes its continued-investigation/blocked-
phase instructions, not the technical findings or exact evidence identities.

**Current platform policy (owner decision, November 16, 2026): Windows and
Linux only, each on x64 and ARM64.** Builds and distribution use exactly four
runtime identifiers. Historical test reports below retain their original
source/evidence scope; they are not fresh validation of this policy change.

The local Linux machine remains the primary development and default testing
environment. Testing the Windows version defaults to the verified
`windows_laptop` MCP connection, including desktop/UI interaction. Carry out authorized testing autonomously;
request owner help only for a concrete privilege, credential, hardware or
scope blocker. See [Windows desktop testing](docs/windows-desktop-testing.md).

SectorWorks is a planned cross-platform disk and optical-image workbench written
in C# with Avalonia and MVVM. It will inspect, browse, extract and, when safely
supported, stage modifications and commit or rebuild images without host-OS
mounting. Source data integrity and capability-based support are central.

**Historical October 21 milestone: Phase 2D remained incomplete; TxF/CLFS compatibility work resumed.**
The first resumed increment adds bounded read-only CLFS metadata analysis and
TxF write-refusal regressions. A safe persistent-transaction acceptance/update
rule remains unproven; Windows-native transaction-bearing volumes stay
noneditable. See [current investigation](docs/platform-status.md#resumed-txfclfs-investigation-2026-10-21).
The previously verified runtime corrective batch remains closed for review.
The owner subsequently ran all three native TxF rename controls successfully;
exact generated-output hashes and independent Linux read comparisons pass.
These provide investigation inputs, while the writer-safety gates remain open.
Bounded diagnostics now pass **19** Python tests. R4 file-state queries match
the exact transaction GUIDs/raw records, while its CLFS reads exposed a harness
filename error. The corrected reader passes a real standard-user known-byte
CLFS self-test and independent offline comparison. Its fresh
[R5 owner-local batch](docs/platform-status.md#owner-executed-r5-results-protected-log-access-denied)
completed all three controls, but its final coverage check correctly failed:
all 16 protected CLFS opens returned access denied, even in the local
administrator run. Exact outputs pass 26 independent Linux reads; the seed
remains unchanged. Retain the single-use evidence; do not rerun or change
permissions. Persistent recovery/update rules still precede writer expansion;
unknown authentication-related suffixes remain unqualified and refused.
The [R6 access diagnostic](docs/platform-status.md#owner-executed-r6-results-attribute-access-is-not-data-access)
completed: metadata attributes are readable, but six metadata GENERIC_READ
opens and both CLFS opens are denied. The owner's backup privilege is present
but disabled. The owner-launched R7 backup-read candidate then failed a
PowerShell dictionary-result checker; cleanup detached the image and the seed
remains unchanged. The [corrected R8 probe](docs/platform-status.md#owner-executed-r8-results-backup-restored-access-still-denied)
completed successfully: the isolated child's existing backup privilege was
enabled and restored, with the original token state verified. All 36 access
observations were retained; before/with-backup results are identical, including
the same eight access denials. Neither protected CLFS client opened. Exact
output/seed hashes and seven independent Linux reads pass. This closes the
bounded backup-candidate evaluation, while both-client recovery/update rules
and writer safety remain unqualified. Retain the single-use evidence; no
further owner-local probe is currently justified by these results.
The [2D closure ledger](checklist.txt) records completed Windows codec-reference,
bounded-reader and Tower source/content checks separately from incomplete
Windows-authored edited-output/TxF/CLFS, LFS 2.0/cache-safety, current ARM64,
GUI/platform and review gates. Reassignment is not completion. Existing October 16 Phase 3
backend direct-commit delivery is preserved. The November 16 assignment resumes
its remaining new-target writer, startup recovery and UI work. Dated verification results below
describe their original snapshots, not new executions or phase-completion claims.
At that October snapshot, newly readable legacy/list NTFS stayed noneditable;
existing qualified FAT/NTFS writes remained intact. The current bounded expansion
and retained exclusions are described above. ARM64 verification is explicitly blocked by unavailable
hardware. The owner authorized source
commit/push only on October 17, 2026, not a release or phase advancement.
The October 20 documentation reconciliation itself authorized no implementation,
remote probes or publication; the owner's subsequent October 21 instruction
resumes only the retained TxF/CLFS compatibility work. See [current evidence](docs/platform-status.md).

Verified 2D subwork now includes the Windows security codec reference, nine
bounded synthetic reader workflow rows on Linux/Windows x64, and local-only
Tower full-source hashes/selected extraction checks. Initial full Linux verification:
**1136 passed, one Windows-only skip**; all then-configured publishes/Linux AppImage pass.
Final corrective-source Linux verification now passes **1178 tests, one
Windows-only skip** (1179 total), with zero build warnings/errors and formatting/
resource/reference-copy checks passing. All then-configured publishes/AppImage are refreshed.
Windows-authored edited-output interoperability, transaction/modern-log safety
and unavailable ARM64 evidence remain incomplete. Passing reader/build checks
does not enable additional writing or complete Phase 2D.
The initial broader Windows run exposed direct-commit sharing, logging and
preference-test failures; its failed results remain historical evidence.
The owner-resumed corrective pass now passes actual Windows **177/177 Services**,
**392/392 reader/security/safety**, **168/168 writer workflows** and **9/9 real
two-commit growth cases**. All **186 exact outputs** pass **876** independent
Linux native commands; no missing-tool checks were silently dropped.
This qualifies the retained Linux-authored/generated rows on Windows, **not**
Windows-authored TxF/CLFS or LFS 2.0 writing. See
[current results and retained blockers](docs/platform-status.md#final-global-session-corrective-snapshot).

The bounded corrective pass does not authorize
remaining Phase 3 features or waive TxF/CLFS, LFS 2.0 or ARM64 requirements.
Full Linux and cross-publish evidence is recorded separately against its
verified source snapshot; counts are not combined into a Windows solution pass.

**Historical Phase 2B/2C delivery: Phase 2B partially delivered; Phase 2C's bounded reader is implemented,
with remaining 1.0 verification/review gates open.** Authentic NT4/Windows
2000/XP-created-media qualification is owner-deferred to a tracked post-1.0
update, not a 1.0 blocker or a passed qualification. Managed NTFS writing is implemented
for a qualified Phase 2B subset; Windows-media interoperability remains incomplete. The supported
reader subset was implemented and verified on Linux Mint 22 x64/X11. Owner-reported
physical testing on 2026-10-14 also found Phase 1 working on Linux x64, Windows
x64 and Linux ARM64 (Raspberry Pi 500). Windows 7 x64 reportedly runs the current
build but remains unsupported.
Cross-publishing does not establish physical compatibility.
The reported missing first hex page is fixed: selecting the Hex View
tab now loads it without a paging workaround, including for small binary files;
see [platform status](docs/platform-status.md) for verification.
NTFS features outside the bounded reader subset and current platform/manual
desktop review remain incomplete, as recorded in the checklist. Phase 2B adds shared staged modifications and Save As a
new image for individually qualified FAT and ordinary NTFS volumes in raw, fixed/dynamic VHD and
fixed/dynamic VHDX; source images remain read-only.
See [staged editing and safety](docs/staged-editing.md). Direct commit
(Phase 3) now also has a confirmed user-facing command and bounded startup
recovery, described in the current section above.
Phase 4 enables qualified standalone optical rebuild; Phase 5 adds qualified CUE
track browsing, CD audio and new-set rebuilding.

Owner-authorized ISO reader refinements now handle zero-length files with
unused out-of-range sector addresses without seeking or rewriting the image,
and batch ISO9660/Joliet directory metadata without raising safety budgets.
The supplied Toshiba recovery ISO's complete tree and file streams read with
unchanged source SHA-256; **102 selected reader tests pass**, including 20 new
synthetic ISO cases. All then-configured local publishes/AppImage are refreshed.
See [exact evidence and boundaries](docs/platform-status.md#iso-reader-compatibility-refinement-2026-10-15);
this does not enable optical editing, Phase 3 or new physical-platform claims.

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

**This is not unrestricted Windows NTFS qualification.** The Windows codec
reference and exact native passive-subset edited outputs now pass. Existing
blank/clean-LFS1.1 support is retained; the native0x1400 subset has independent
Windows output evidence, not synthetic checks alone. Default TxF/CLFS and
coherent detached LFS2/cache qualification remain unresolved and explicitly
DEFERRED POST-1.0 under P1.NTFS.MODERN.1–.6. All bounded writing remains in1.0.
Formatting and unsupported NTFS metadata remain disabled. Current commit UI
and bounded startup recovery integrate the retained qualified backend. See the
[qualification ledger](docs/support-matrix.md#phase-2b-qualification-ledger-implementation-authorized-2026-10-15),
[dependency blocker](docs/dependency-evaluation.md#phase-2b-existing-volume-ntfs-blocker-2026-10-15)
and [current verification/platform evidence](docs/platform-status.md).
Physical Mint/X11 workflows and independent FAT/container checks are distinct
from cross-target cross-publishing. Current Windows x64 CLI workflows pass for the
retained generated rows and published GUI workflows pass; physical ARM64 is
NOT VERIFIED — DEFERRED POST-1.0 under P1.ARM64.LINUX/P1.ARM64.WINDOWS.
Historical Phase 2B final verification: **956 tests passed, one Windows-only reference
test skipped (957 total)**, zero-warning Release build, locked restore,
formatting/resources/reference-copy checks, all then-configured self-contained publishes
and the Linux x64 AppImage prototype. Six actual Mint/X11 NTFS workflows and
independent native output checks pass with unchanged sources. The preceding
FAT prerelease's 561 passing/zero-skipped tests and five X11 workflows remain
historical evidence, not Windows-media qualification.

### 1.0 scope boundary

The roadmap targets **1.0**. VMDK, VDI, QCOW2, DMG, ext2/ext3/ext4, exFAT,
HFS+ and APFS target **qualified read-only subsets where feasible** in Phase 6.
Their modification/staging, modified Save As, direct commit, rebuild, creation
and formatting are outside 1.0 requirements, not missing required writers.
No post-1.0 writer date is promised. Reader evaluation, legal fixtures and tests
remain required: detection is not browsing/extraction; exact unsupported variants
and infeasible-reader blockers must be documented for owner review, not silently
dropped. This is not a claim that these readers are implemented.

Read-only caps compose across the stack: a read-only container caps even FAT/NTFS;
ext/exFAT/HFS+/APFS stay read-only inside raw/VHD/VHDX. Internal parsers determine
access independently of native host-OS mounting capabilities. Platform limits
require evidence, not assumptions about Windows reading filesystems from other operating systems.
Core disk, optical and retro write milestones below are unchanged.

### Planned editing coverage

**PMFX roadmap addition (2026-10-17):** DiskGenius PMFX read-only browsing and
file/directory extraction are required in Phase 6, item **6.11**, for work/home
interoperability. This is planned, **not implemented**; local FAT16/FAT32
container research is not qualified product support and found no NTFS PMFX.
The container will cap otherwise writable filesystems to read-only. PMFX
writing, modified Save As, commit, rebuild and image conversion/export are not
part of this addition; no writer milestone is promised. See the
[research evidence and implementation/qualification plan](docs/pmfx-read-only-plan.md).
This roadmap approval does not authorize starting Phase 6.
**Phase 6 must deliver the reader inside SectorWorks before it is complete:**
PMFX in the declared qualified subset must open directly for browsing and
extraction. Research, detection-only, external raw conversion or a future
implementation plan does not satisfy that completion gate.

The owner approved the editing-roadmap correction on 2026-10-15:
**Phase 2B and the subsequently authorized Phase 2C precede Phase 3**.
Phase 2B explicitly schedules staged editing and
validated Save As for partitioned raw images, fixed/dynamic VHD and VHDX,
broader FAT naming (LFN/mixed case/Unicode), and ordinary existing-volume NTFS.
Phase 2C adds read-only NTFS compatibility qualification, not a writer expansion.
Phase 3 must first qualify staged editing/validated Save As/new-output NTFS
writing for newly readable attribute-list/legacy targets (3.0.1-3.0.5), then
direct commit per required disk stack (3.7), not just one combination.
Phase 4 covers shared ISO/UDF/bridge staging and rebuilding;
Phase 5 covers BIN/CUE staging and safe output-set rebuilding; Phase 6 item 6.10
covers conventional 360K/720K editing/Save As/direct-commit qualification.

Qualified FAT/NTFS, including native passive-source outputs, bounded reader/Tower
and legacy/list writer, commit UI and startup recovery are verified. The
November17 scope decision defers remaining modern NTFS states and physical ARM64,
and its conditional acceptance closes revised Phase2/2B/2C/2D/3 exits. Later
phases remain unimplemented apart from separately authorized packaging work. See the
[capability delivery matrix](docs/support-matrix.md#required-editing-delivery-roadmap)
and [checklist](checklist.txt) for explicit tasks, preservation/failure gates
and the owner-review requirement for unresolved scope.

### Phase 2C bounded NTFS reader evidence

On **2026-10-15** the owner explicitly authorized Phase 2C and synchronized
roadmap/specification changes. The separate qualification targets are **NTFS 1.2
(NT4), 3.0 (Windows 2000) and 3.1 (XP+)**, with bounded supported attribute lists,
extension records and version-specific ordinary metadata/content access.
New tasks/tests/exit gates began pending. The separate read preflight now supports
bounded ordinary attribute lists/extensions, resident/nonresident lists and split
data/index streams before the unchanged signed reader. The final Phase 2C focused
selection passed **49 tests (35 attribute-list + 14 version/layout/service)**, including named-list
guard, mixed-volume isolation, modern/legacy extension identity and empty
nonresident index-bitmap cases. Coverage includes malformed references,
read workflows/source hashes and staging/Save As denial.
See [canonical verification chronology and logs](docs/platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15)
for that full-run/focused chronology. Subsequent Phase 2D qualifies nine bounded
reader workflow rows with **66/66 focused passes each on Linux/Windows x64**;
current full-suite and then-configured publish evidence is summarized above. The Phase 2C
run changed no versioned release assets or GitHub publication; cross-builds are
not physical compatibility. MFT bootstrap lists are explicitly
unsupported; see [exact resource/feature bounds](docs/storage-phase1.md#implemented-phase-2c-ntfs-reader-subset-and-open-qualification).
No authentic NT4 fixture exists;
synthetic version-field tests alone cannot qualify authentic legacy media.

Attribute-list/legacy reader success never grants writing: only separately
qualified Phase3 structural rows enable staging/Save As/commit. Existing editing
is preserved; default TxF/LFS2/cache states stay read-only and qualification is
DEFERRED POST-1.0 under P1.NTFS.MODERN. The Windows codec reference is complete.
The owner's approximately 8.42 GB raw Tower NTFS 3.0 image stays outside the
repository and must not be copied, modified or distributed. Initial diagnosis
is rejection of a live directory attribute list at MFT 26 referencing indexing
extension 8336. The new reader enumerates 905 directories/16199 files through
retained hidden/system/metafile filters, without directory failures, with writing
false and unchanged source size/mtime. Three complete stream hashes match native
`ntfscat`; service read-only preview/hex/search passes with ordinary enumerated
entries. That initial Phase 2C probe changed no filters and performed no extraction
or full-image hash. Subsequent authorized Phase 2D checks completed full before/
after source SHA-256 and **1,235,021 bytes** of selected extraction/native content
comparisons; private extracted outputs were removed.
The native index warning is explained by ntfs-3g's 65536-byte whole-attribute
read limit versus the 344064-byte index; this is not volume-health certification
or exhaustive whole-volume content/health qualification. See
[completed bounded Tower evidence](docs/platform-status.md#completed-local-only-tower-sourcecontent-checks).
See the [planned version/feature ledger](docs/support-matrix.md#phase-2c-ntfs-reader-qualification-plan)
and checklist **2C.1-2C.7**, **2C.V1-2C.V8**, **2C.E1-2C.E6**.
On 2026-10-15 the owner moved authentic NT4/Windows 2000/XP-created-media
qualification and dependent checks to **post-1.0 P1.NTFS.1-P1.NTFS.6** in the
[checklist](checklist.txt). Disposable VM-authored disks count; vintage physical
hardware is not required. Synthetic/observed-version evidence is not authentic
historical-OS interoperability. Automated reader tests, safety bounds, Tower
source-hash/extraction tasks, revised x64 platform checks and bounded Phase3
writer/commit safety remain required for1.0. The November17 decision separately
defers modern states and physicalARM64; revised Phase2C exits are accepted
complete on retained evidence and this reconciliation. Reader proof alone grants no writer,
direct-commit or repair authority; the separately delivered backend is retained.
The signed reader and strict writer-validation defaults are unchanged; only the
writer diagnostic now names its Phase 2B subset/Phase 3, not obsolete Phase 1 scope.

## Prerequisites

- .NET SDK **10.0.402**, pinned exactly in `global.json`.
- .NET **10 LTS**, target `net10.0`; validation host runtime **10.0.13**.
- Bash and network access to NuGet for the first restore/publish.
- `qemu-img`, `fsck.fat`/mtools and NTFS tools for complete Linux writer/
  interoperability qualification, including the mandatory exact-output handoff
  checks. Windows managed workflows use the sealed generated-input catalog and
  return outputs for Linux validation; missing checks are not silent passes.
- Linux `setsid` (`util-linux`) for independent-session log-concurrency tests.
  Neither the native checking tools nor `setsid` is an application dependency.
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
`artifacts/publish/<rid>/`, with GPL and third-party notices. The four supported targets are
`linux-x64`, `linux-arm64`, `win-x64` and `win-arm64`.
Native desktop libraries are still required; self-contained does not mean a
statically linked GUI. Cross-publish is **not** physical platform validation.


The browser defaults to alphabetical folders first, then alphabetical files.
Use **File > Preferences** to choose the default sort field/direction and
folders-first grouping. Settings persist across restarts; column clicks remain
per-tab overrides. Browser navigation also selects/reveals the current directory
in the structure tree. See the UI documentation for error/cancellation behavior.

For an intentional package update, run an unlocked `dotnet restore SectorWorks.sln`,
review resulting locks and licenses, then rerun verification and all publishes.

## Windows testing access

A physical Windows 11 Home x64 laptop is now reachable over LAN SSH as a
dedicated standard testing account. On 2026-10-15 the agent verified a
strict-host-checking, publickey-only unattended `whoami` command with exit
code 0. That October 15 result established command access only. Subsequent
authorized work installed pinned user-local tooling, ran Windows CLI tests and
the codec reference, and authored/read-only-reopened disposable native media.
The corrective pass also qualified generated-image writer workflows with
independent Linux checks of the exact outputs. Published Windows GUI and
bounded native passive-source edited outputs subsequently passed. Default
TxF/CLFS and coherent detachedLFS2/cache safety remain unqualified but explicitly
DEFERRED POST-1.0, not barriers to accepted revised Phase2B/2D/3 completion
or evidence of unrestricted Windows support.

The [copyable Windows OpenSSH guide](docs/windows11-openssh-setup.txt) covers
installation, LAN-only firewall configuration, separate server/client keys,
standard-user public-key enrollment, permissions, verification and revocation.
The host trust entry already existed; independent local fingerprint comparison
remains unconfirmed. Keep machine-specific connection details and all private
keys out of Git and synchronized storage. SSH does not provide an interactive
Windows desktop; GUI testing requires separate setup. See
[current platform evidence](docs/platform-status.md#final-global-session-corrective-snapshot).

## Documentation and task authority

Read [AGENTS.md](AGENTS.md) and [rules.txt](rules.txt) before every task.
[checklist.txt](checklist.txt) is the authoritative progress tracker.
Current specs and design references are in [docs](docs/); supplied root originals
remain authoritative and documentation copies must remain synchronized.

- [Architecture/MVVM](docs/architecture.md)
- [Dependency evaluation](docs/dependency-evaluation.md)
- [Phase 6 PMFX read-only research and delivery plan](docs/pmfx-read-only-plan.md)
- [Fixture provenance and verification](docs/fixtures.md)
- [Build and physical-platform status](docs/platform-status.md)
- [Windows 11 Home OpenSSH setup and unattended login](docs/windows11-openssh-setup.txt)
- [Read-only explorer operations and extraction policy](docs/explorer-operations.md)
- [Phase 1 read-only support matrix and limitations](docs/support-matrix.md)
- [Phase 2 staged editing and Save As safety](docs/staged-editing.md)
- [UI behavior, physical-smoke reproduction and review areas](docs/ui-phase1.md)
- [Storage safety limits and tested reader subset](docs/storage-phase1.md)

Dependency decision, 2026-10-13: keep DiscUtils 0.16.13 for development, accepting
its maintenance risk and possible later replacement/rework. Re-evaluate in the
checklist's release-quality section once other production-readiness work has
matured, before production release. NTFS formatting fails on Linux because of
Windows principal/SID APIs; ordinary reads and the isolated managed Phase 2B
writer subset are qualified as documented above, not blanket NTFS writing or
formatting. Full Phase 2C legacy/attribute-list qualification remains pending.
Capability and safety testing remain
mandatory throughout development.

Audio scope was resolved by the owner on 2026-10-13:
follow the checklist, with BIN/CUE and functional audio playback in Phase 5.
The UI specification and companion guide now reflect that decision; Phase 1
only reserves the relevant structure/tabs. Phase 1 was authorized on 2026-10-14;
no audio implementation is started. See the evaluation and platform-status
documents for details.

Retro-computing priority, 2026-10-14: **360K and 720K PC floppy images** are
important product requirements. The tested Phase 1 read-only 720K Compaq/legacy
FAT12 subset is retained. Broader variants and dedicated 360K qualification are
planned in checklist Phase 6, item 6.10; neither capacity alone nor current FAT12
support implies blanket compatibility, writing or bootability preservation.

## License

SectorWorks original code is licensed under **GPL-3.0-only**, selected by the
project owner. See [LICENSE](LICENSE). This does not relicense third-party
dependencies. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
Supplied design/specification materials and mockups are retained as owner-provided
references; no third-party artwork rights are inferred from the code license.

### Focused native NTFS review candidates (November17)

The [decision report](docs/focused-ntfs-diagnosis.md) describes the newly qualified
clean native TxF/repair-log-disabled subset, remaining default-TxF/LFS2 limits,
and exact verification evidence. [Runnable package manifest](artifacts/focused-ntfs-20261117/packages/manifest.json)
lists the four supported self-contained targets and Linux x64 AppImage. These
are local owner-review candidates, not a release. Physical ARM64 remains
NOT VERIFIED — DEFERRED POST-1.0 BY OWNER; see P1.ARM64.LINUX/P1.ARM64.WINDOWS.

If MSBuild cannot create a temporary directory, the recorded successful local
verification used a fresh private `TMPDIR` outside synchronized storage with
`MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`. Keep that directory
through the finite build/test/format command and remove it only after its owned
processes exit. This is a verified isolation workaround; no global temp-directory
permission or security change is required or claimed.
