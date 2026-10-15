# Foundation storage dependency evaluation

Evaluated 2026-10-13 on the available Linux x64 host, SDK 10.0.402 / .NET 10.
Scope: **historical foundation experiments only, before Phase 1**. At that
evaluation point there were no product storage handlers or UI capabilities.
Current reader support is documented in the [support matrix](support-matrix.md);
Phase 2 write qualification is documented in [staged editing](staged-editing.md).
Do not substitute these direct-library experiments for product verification.

## Decision and provenance

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
No fork is substituted: none was demonstrated to solve the relevant limitations
with sufficiently reviewed provenance/tests. The release-quality review will
consider then-current upstream maintenance, forks, patches or replacements.
Independently validate NTFS writes in their implementing phase before enabling
them; postponing the dependency-choice review does not establish write support.

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

This is new **modification** evidence, not the historical formatter limitation.
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
NTFS staging/Save As remains disabled across raw/VHD/VHDX.
**Owner decision required:** authorize portable-security dependency remediation
(with licensing/architecture review as appropriate), or explicitly revise the
required NTFS delivery scope. DiscUtils 0.16.13 remains approved; no replacement,
ACL substitution, host mounting or formatting workaround was introduced.

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
| BIN/CUE, CD-DA | No complete required CUE/track/audio workflow established | Additional optical model/parser/audio backend needed in Phase 6 |

All extra-format statements are **research only**, not executed capability tests
or a Phase 7 implementation. Rock Ridge, El Torito, UDF, hybrid optical metadata,
and boot preservation are not exercised by the simple ISO golden.

## Significant-dependency risk/platform matrix

| Dependency / version | License / maintenance evidence | Native / platform and error risk | Threading / large / sparse |
|---|---|---|---|
| DiscUtils Core, Streams, FAT, ISO9660, VHD, VHDX 0.16.13 | MIT; 2021 upstream/package baseline, maintenance risk high | Managed, no required native library. Linux x64 executed. Other five target RIDs unverified physically. Error taxonomy inconsistent as above | Mutable stream position/caches: assume instances **not thread safe**; serialize per instance. Streams/Int64 APIs; dynamic VHDX large-address probe passed, not all formats/features |
| DiscUtils NTFS 0.16.13 | MIT; same maintenance baseline | Managed but Windows security APIs restrict format/ACL/write paths. Linux ordinary reads passed; Windows x64/ARM64, Linux ARM64, macOS x64/ARM64 unverified | Same isolation policy; only resident/nonresident ordinary-file read tested; compression/sparse semantics unverified |
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
writer milestone is required for these additions. Phase 7 reader evaluation,
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
  Direct commit needs per-stack qualification for the required Phase 2B rows.
- Phase 4 needs a preservation-aware ISO rebuild planner and independent
  validation/boot testing; CDBuilder is not proof of source-layout preservation.
  Declared UDF and ISO/UDF bridge read/staged/rebuild delivery also needs explicit
  implementation and independent qualification, not detection-only completion.
- Additional libraries/internal work are needed for missing filesystems,
  exact optical structures/audio and comprehensive validation. Prefer managed
  implementations; any native option requires all six OS/architecture reviews.
- Keep trimming/AOT off until reflection/provider registration is evaluated.
  Dependency choice is provisional; no platform support follows from compilation.
