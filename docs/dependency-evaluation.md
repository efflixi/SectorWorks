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
At the foundation decision no fork was substituted: none was demonstrated to solve the relevant limitations
with sufficiently reviewed provenance/tests. The release-quality review will
consider then-current upstream maintenance, forks, patches or replacements.
Independently validate NTFS writes in their implementing phase before enabling
them; postponing the dependency-choice review does not establish write support.
The subsequently owner-authorized isolated NTFS source repair is documented
below; it does not substitute NuGet assembly identities or change package versions.

### Phase 2C implemented NTFS reader subset and open qualification, 2026-10-15

The owner explicitly authorized Phase 2C and its synchronized roadmap/
specification change before Phase 3. **Bounded reader implemented; remaining 1.0
verification/review gates open, not phase complete**. The owner subsequently
deferred authentic NT4/Windows 2000/XP-created-media qualification and dependent
checks to post-1.0 **P1.NTFS.1-P1.NTFS.6** in the [checklist](../checklist.txt).
These are incomplete follow-ups, not 1.0 blockers. VM-authored fixtures suffice;
vintage hardware is not required. Automated tests, bounds, source integrity and
general platform verification remain required, with the existing separate
macOS runtime deferral unchanged.
Implemented-subset results are now
tracked in [platform status](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15),
not full version/media qualification. Targets are
separate bounded **NTFS 1.2 (NT4), 3.0 (Windows 2000), 3.1 (XP+)** ordinary-read
subsets, including supported attribute lists/extensions and version-dependent
metadata. Existing ordinary reads and qualified managed Phase 2B writes below
remain the retained baseline, not evidence that legacy/attribute-list expansion
already works.

The implemented separate `NtfsReadValidation` preflight/resolver handles bounded
ordinary continuation/index/version metadata behind Storage contracts before the
unchanged signed reader. Shared record/run validation has explicit read-only
extension opt-in; strict writer `NtfsValidation.Validate` defaults remain
unchanged. Only its diagnostic now names the Phase 2B writer subset/Phase 3,
not the obsolete Phase 1 boundary. No package version, signed-reader assembly,
runtime dependency or source-repair assembly identity changed.
List/base/sequence/attribute and VCN checks reject malformed references and
unsupported content. MFT bootstrap lists remain explicitly blocked: safe
bootstrap mapping is not implemented. Resource caps and active-index/inactive-MFT
rules are documented in [storage bounds](storage-phase1.md#resource-cancellation-and-concurrency-policy).
Full verification passed 1001/one Windows-only skip, 1002 total. Four cases added
after that full run's build (named-list guard, mixed-volume isolation,
modern/legacy extension identity and empty nonresident index bitmap) pass in
the latest focused 49/49 run on current code (35 attribute-list,
14 version/layout/service). Current inventory 1006 including the skip is not a
claimed full 1005-pass run. Final repeat all-six
self-contained publish/local Linux AppImage passes on latest source, as does
the bounded owner read-only probe. Versioned
releases/GitHub publication are unchanged. Cross-builds are not physical
compatibility. Authentic historical media moves to **P1.NTFS**; remaining general
physical gates stay open. Stop for owner review.

No authentic NT4 fixture exists. Synthetic version-field tests are regression
inputs, not authentic legacy qualification. Legal synthetic fixtures, content
hashes and service workflows pass for the bounded subset; authentic provenance
remains incomplete under post-1.0 **P1.NTFS**. Full verification/platform and
owner-image source-hash/extraction gates stay open under **2C.1-2C.7**,
**2C.V1-2C.V8**, **2C.E1-2C.E6**.
Tower's approximately 8.42 GB raw NTFS 3.0 owner image stays outside the
repository: do not copy, modify or distribute it. Initial rejection concerns
the live directory attribute list at MFT 26 referencing indexing extension 8336;
the separate native warning is now explained by the pinned ntfs-3g whole-attribute
65536-byte limit versus the 344064-byte index. Initial reader enumeration succeeds,
not content/health certification; pinned references are in platform status.

Newly readable attribute-list/legacy variants remain noneditable until Phase 3
**3.0.1-3.0.5** qualifies staged editing, Save As and new-output NTFS writes
before **3.7** direct commit. Preserve existing Phase 2B qualified editing and
the required incomplete **2B.6** Windows-authored-media/security-reference and
LFS 2.0/cached-metadata write gates. Reader acceptance cannot close them or
demote NTFS writing to read-only 1.0 scope. No Phase 3 implementation follows.
See the [planned reader ledger](support-matrix.md#phase-2c-ntfs-reader-qualification-plan).

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

This is historical original-package **modification** evidence, distinct from
the formatter limitation. The subsequent managed repair is described below.
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
At that prerelease, NTFS staging/Save As remained disabled across raw/VHD/VHDX.
At the partial-release review, portable-security remediation or an explicit
scope decision required owner direction. The owner subsequently authorized the
bounded managed investigation below and reaffirmed that NTFS writing remains
required for 1.0; read-only NTFS is not an acceptable release-scope substitute.
DiscUtils 0.16.13 remains approved; no replacement, ACL substitution, host
mounting or formatting workaround was introduced.

### Managed NTFS repair implementation and qualification

The owner subsequently authorized the portable security component, managed
repair implementation and qualification. This removes the constructor and
inheritance blockers for the qualified subset, not for every NTFS feature.
The original dependency-boundary diagnostics remain intentionally tied to the
unchanged signed package; their expected failures are still valid baseline
evidence, not tests to rewrite as repaired-backend successes.

`SectorWorks.ManagedNtfs` is a separately named assembly importing only the
pinned NTFS sources and two required internal Core helpers (`ObjectCache`,
`Utilities`). Signed Core/Streams and all existing 0.16.13 packages remain
unchanged. No private signing key, identity impersonation, reflection-based
friend bypass or native runtime dependency is introduced. Portable byte APIs
are separate from annotated Windows-only descriptor bridges. Formatting, quota
management and the upstream optional `BuildClusterMap` diagnostic remain
explicitly unsupported; product allocation validation does not call that API.

`SectorWorks.NtfsSecurity` supplies bounded managed parsing/canonical serialization
and corrected file/container inheritance. Unsupported semantics reject before
allocation. Children inherit image-parent owner/group under the declared policy,
not host SIDs or a permissive default. Existing raw descriptor bytes remain
preserved; hashes of stored `$SDS` records use the stored bytes, not canonicalized
input. See the [editing guide](staged-editing.md#managed-ntfs-subset-and-remaining-mandatory-qualification)
for exact ACL/name/metadata/log restrictions.

Qualification also exposed and repaired tightly coupled upstream defects:

- Checked byte-complement casts overflowed while clearing allocation bits.
  Only intentional low-byte bit reinterpretation is unchecked.
- Backward run deltas require signed reinterpretation; lengths are unsigned
  positive bounded counts. Mapping-pair terminators must be accepted before
  nonterminal width checks. Compiled binary-vector tests cover these paths.
- Fallback allocator zones overwrote rather than accumulated allocations;
  scans also needed exclusive zone ends. Near-full replacement now leaves
  exactly 16 independently verified free clusters.
- MFT mirror writes were hardcoded to four records. The required extent is
  `max(4 * recordSize, clusterSize)`, including user records for large clusters.
- SI restoration regenerated independent filename/index dates and transaction
  change times. Explicit byte snapshots preserve those dates and DOS aliases.
- New-file creation automatically generated unsupported ObjectIDs on fresh
  media. The qualified writer opts out before creation; it never strips
  existing IDs or weakens the validator to accept them.

The targeted suite passes **382 tests with one explicit Windows-only skip**
(383 total): portable security vectors/flags/bounds, independent raw
allocation/security/log validation, compiled mapping pairs, 21 actual Services
container/layout/physical-sector rows, eight native cluster sizes, multi-volume
and FAT/NTFS composition, transitions, growth, full/near-full media, metadata and
source preservation, cancellation and actual I/O/flush/validation failures.
Independent `qemu-img`/`ntfsinfo`/`ntfscluster`/`ntfscat` checks pass. Six actual
Mint/X11 NTFS workflows pass separately, with app-window screenshots, unchanged
sources and independent native output-byte checks.

Final repository verification passes **956 tests, one Windows-only skip,
957 total**, locked restore, zero-warning/error Release build, formatting,
resources and authoritative-copy comparisons. Six final cross-publishes and
the Linux x64 AppImage pass architecture/legal/payload and Linux resource checks.
These do not supply missing Windows-runtime or modern-log evidence.

**Mandatory gaps remain:** actual Windows-authored media, Windows reference
execution, and LFS 2.0/cached-metadata safety. No normal-Windows-media or full
Phase 2B completion claim follows. Clean LFS 1.1 has synthetic bounded
restart-page coverage only; dirty/ambiguous/unsupported logs remain disabled,
without replay or reset. Structural references are the
[LFS definitions](https://github.com/tuxera/ntfs-3g/blob/edge/include/ntfs-3g/logfile.h)
and [clean-state checks](https://github.com/tuxera/ntfs-3g/blob/edge/libntfs-3g/logfile.c);
no source from those files is imported into the application.
NTFS writing remains mandatory for 1.0. No new dependency replacement decision,
scope reduction or Phase 3 authorization is implied.

MIT originals and notices are retained verbatim. Original repair additions
follow the repository's GPL-3.0-only terms. Imported sources, exact upstream
input hashes, transformation/repair patch and final hashes are retained under
`src/SectorWorks.ManagedNtfs/`; no generated binaries or owner samples belong
in Corresponding Source. Verify provenance against the approved source snapshot:

```sh
python3 src/SectorWorks.ManagedNtfs/record-repairs.py --verify
```

New SDK projects and downstream lock files record the changed project graph;
package versions are unchanged. The architecture boundary is Storage ->
ManagedNtfs -> NtfsSecurity, with no dependency types exposed to Services/Core/UI.
The existing Services test that expected all NTFS to be disabled was replaced
with an explicit positive qualified-NTFS test; genuinely unqualified historical
container/partition/optical fixtures retain their negative tests.

### Bounded managed NTFS repair investigation (2026-10-15)

**Historical investigation complete; agreed stop point reached, not a writer repair.**
The owner selected Option 1: investigate a contained managed repair and stop
before a broad security-subsystem rewrite or material dependency/architecture
change. NTFS editing remains mandatory for 1.0 and Phase 2B remains blocked.
During that investigation no product writer, dependency version, signing policy
or release asset changed. The owner subsequently authorized the implementation
and qualification below, superseding that stop point, not the Phase 3 boundary.

The investigation traced the approved package's exact source commit
`59d7cadab839c6d8dfcf52f8be5efe6d2ced190f`, checked the actual installed assembly's
creation-call IL, and executed constructor and existing-volume probes on
Linux x64 / .NET 10.0.13. The assembly version is `0.16.0.0`; the NuGet package
version remains `0.16.13`. These are not conflicting dependency versions.

#### Executed evidence

- All five independently exercised constructors/parsers fail with
  `PlatformNotSupportedException`: binary `SecurityIdentifier`, binary `RawAcl`,
  binary `GenericAce`, binary `RawSecurityDescriptor`, and SDDL
  `RawSecurityDescriptor`. Switching from SDDL to binary input does not remove
  the platform dependency; constructing an explicit `NewFileOptions` descriptor
  cannot bypass it either.
- The installed `CreateDirectory(string, NewFileOptions)` and `CreateNewFile`
  methods both pass literal `false` as `isContainer` to
  `CalcNewObjectDescriptor`. This confirms a directory-inheritance defect in the
  shipped package, not merely a discrepancy in downloaded source.
- All 16 existing-volume regressions pass again: expected creation/security
  failures, partial-mutation detection, narrower successful mutations, native
  checks, create-new destination safety and unchanged synthetic sources.
  Passing a regression that expects failure is not successful NTFS creation.
- Seven new dependency-boundary tests preserve the five platform observations
  and two installed-assembly call-site observations. These are baseline
  diagnostics, not desired future writer behavior; intentionally update their
  expectations when a reviewed patch changes the dependency.

#### Source findings and repair surface

The affected runtime path spans
[`NtfsFileSystem`](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Ntfs/NtfsFileSystem.cs),
[`SecurityDescriptor`](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Ntfs/SecurityDescriptor.cs)
and
[`SecurityDescriptors`](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Ntfs/SecurityDescriptors.cs).
The Windows types are used for parsing, inheritance, NTFS-specific descriptor
serialization, hashing/deduplication and `$Secure` lookup/insertion, not only for
the public security-property API. Legacy per-file descriptors and shared
security-ID descriptors both need portable handling.

Comparison with Microsoft's
[ACE inheritance rules](https://learn.microsoft.com/en-us/windows/win32/secauthz/ace-inheritance-rules)
identifies additional semantic gaps in the source helper:

| Case | Approved helper | Required behavior |
| --- | --- | --- |
| Container-only inheritance to a directory | Directory creation chooses the file branch and excludes the ACE | Inherit an effective directory ACE |
| Object-only inheritance through a directory | Even changing the argument to `true` excludes this ACE; the current `false` branch makes it effective | Propagate an inherit-only ACE unless no-propagate forbids it |
| Generic rights / CREATOR_OWNER or CREATOR_GROUP | Copies mask and SID without translation | Map effective rights/SIDs and retain a separate inheritable form when required |

The last two findings are source/rule comparisons, not executed Windows ACL
round trips. A constructor-only shim would retain them. The upstream
[`AclInheritance` test](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Tests/LibraryTests/Ntfs/NtfsFileSystemTest.cs)
is explicitly skipped, referring to
[DiscUtils/DiscUtils#14](https://github.com/DiscUtils/DiscUtils/issues/14).
Its presence is not evidence that inheritance was qualified.

Both creation paths also read the parent descriptor unconditionally, even when
an explicit descriptor is supplied, and do so after allocating/indexing the new
entry. Merely setting an option cannot bypass that call. A future repair should
validate and derive the supported descriptor before creating the entry;
disposable Save As failure/publication safeguards remain necessary regardless.

The public `IWindowsFileSystem` contract exposes `RawSecurityDescriptor`.
A contained implementation could retain Windows-facing adapters while adding
an internal portable byte representation; changing all public APIs or replacing
FAT/container handlers is not established as necessary. A reproducible source
patch/fork still requires a reviewed assembly-identity/build plan:
[`DiscUtils.Core` friend declarations](https://github.com/DiscUtils/DiscUtils/blob/59d7cadab839c6d8dfcf52f8be5efe6d2ced190f/Library/DiscUtils.Core/Properties/AssemblyInfo.cs)
bind NTFS access to a strong-name public key. Do not assume an independently
named/signed NTFS assembly is a drop-in replacement or disable signing to force
it through.

#### Stop decision and proposed next bounded deliverable

**No-go for an API-substitution-only patch.** The next meaningful implementation
is a portable NTFS security component plus corrected inheritance and dependency
integration, rather than a constructor fix. That exceeds this investigation's
contained-patch stop boundary. No partial codec, permissive/default ACL,
inheritance bypass, host mount or unqualified writer was introduced.
This does not establish that a managed repair is infeasible or that NTFS needs
to be demoted to read-only for 1.0.

Recommended next authorization: implement and qualify that NTFS-only component
in a reproducible, pinned source patch/fork, keeping the replaceable SectorWorks
interfaces and existing FAT/container behavior. Before enabling any writer:

1. Bound and validate self-relative descriptors, SIDs, ACEs and ACLs; preserve
   owner/group, DACL/SACL, control flags and null-versus-empty ACL semantics.
   Reject unsupported inheritance semantics explicitly, without discarding or
   weakening existing permissions. Define the exact ordinary-security subset.
2. Test file/directory inheritance, object/container/no-propagate/inherit-only
   combinations, deny/allow ordering, generic masks and creator SID handling.
   Establish independent expected bytes and Windows-reference interoperability;
   Linux construction success alone is insufficient.
3. Integrate both legacy/shared descriptors and verify `$Secure` security IDs,
   hashes, `$SDS` duplicate records and `$SII`/`$SDH` indexes alongside existing
   MFT/allocation/preservation checks. Do not replace this with `ntfsfix -n`.
4. Review MIT notices/source distribution, assembly identity/signing and locked
   dependency packaging, then run the required Phase 2B NTFS operation/layout/
   container matrix and failure gates. Cross-publishes remain distinct from
   physical Windows/ARM64/macOS evidence.

No Phase 3 work or change to the NTFS 1.0 requirement follows from this report.
Formatting and quota/SDDL presentation APIs remain separate evaluation surfaces;
this investigation did not qualify them.

Reproduce the boundary tests after sourcing `scripts/common.sh`:

```sh
"$DOTNET" test tests/SectorWorks.DependencyTests/SectorWorks.DependencyTests.csproj \
  --configuration Release --no-restore --filter FullyQualifiedName~NtfsSecurityBoundaryTests
"$DOTNET" test tests/SectorWorks.StorageTests/SectorWorks.StorageTests.csproj \
  --configuration Release --no-restore --filter FullyQualifiedName~NtfsExistingVolumeTests
```

Local-only evidence: `artifacts/ntfs-security-investigation/` contains the source
snapshot and detailed constructor/IL/baseline test logs. No owner sample or
modified NTFS output is distributed.
The complete dependency suite passes **27 tests**, and the separate
existing-volume suite passes **16 tests**, with zero failures/skips in both.
The added file passes formatting/analyzer verification; authoritative technical
specification copies and ignored owner-sample hashes remain unchanged.
No full-solution rerun, new physical-platform qualification, cross-publish or
release is claimed for this test/documentation-only investigation. The previous
561-test release evidence remains historical and is not relabeled as a new run.

### Historical foundation malformed and streaming evidence

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
  Phase 2C first adds independently qualified NTFS 1.2/3.0/3.1 reader subsets;
  newly readable legacy/attribute-list variants stay noneditable until Phase 3
  3.0.1-3.0.5 qualifies staged/Save As/new-output NTFS writing. Direct commit
  then needs 3.7 per-stack qualification for required Phase 2B and new writer rows.
- Phase 4 needs a preservation-aware ISO rebuild planner and independent
  validation/boot testing; CDBuilder is not proof of source-layout preservation.
  Declared UDF and ISO/UDF bridge read/staged/rebuild delivery also needs explicit
  implementation and independent qualification, not detection-only completion.
- Additional libraries/internal work are needed for missing filesystems,
  exact optical structures/audio and comprehensive validation. Prefer managed
  implementations; any native option requires all six OS/architecture reviews.
- Keep trimming/AOT off until reflection/provider registration is evaluated.
  Dependency choice is provisional; no platform support follows from compilation.
