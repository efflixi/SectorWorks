# SectorWorks agent entry point

Before **every task**, read `rules.txt`, `checklist.txt`, the full current
technical specification, and the current UI specification and design references
when relevant. These are mandatory project instructions, not optional context.

- [Rules](rules.txt)
- [Authoritative checklist](checklist.txt)
- [Technical specification](docs/specifications/Cross_Platform_Disk_Image_Workspace_Tech_Spec_v0.3.txt)
- [UI specification](docs/specifications/SectorWorks_UI_Look_and_Feel_Spec_v0.2.txt)
- [Additional UI guide](docs/design/SectorWorks_UI_Design_and_Prototype_Guide_v0.2.docx)
- [Disk/optical mockup](docs/design/UI01.png)
- [Disk/filesystem mockup](docs/design/UI02.png)
- [Architecture and MVVM conventions](docs/architecture.md)
- [Local commands and SDK](README.md)
- [Windows 11 Home SSH setup and unattended access](docs/windows11-openssh-setup.txt)
- [Current build and physical-platform evidence](docs/platform-status.md)

The supplied root specifications remain authoritative; documentation copies must
stay byte-identical. Do not silently edit specifications or mockups to resolve
disagreements; report conflicts and obtain owner direction first.
Owner confirmed on 2026-10-13 that the checklist governs audio scope:
BIN/CUE support and functional CD-DA playback belong to Phase 6. Phase 1 reserves
the optical/audio structure and pane tabs without implementing those workflows.

Owner also approved keeping DiscUtils 0.16.13 for development on 2026-10-13.
Do not treat fork evaluation as a prerequisite to starting development.
Re-evaluate the choice in the release-quality checklist before production
release, retaining the documented NTFS formatting limitation and all per-phase
safety/capability verification requirements. Keep storage integration replaceable.

Authorization applies only to the requested phase. Run verification, update the
checklist truthfully, report limitations, and stop for user review. Do not
implement Phase 1 while carrying out foundation preparation.

Owner retained macOS as an intended supported target on 2026-10-14 and explicitly
deferred physical runtime/signing validation while authorizing Phase 2.
Keep both cross-publishes working without claiming physical compatibility.

Owner changed macOS support policy on 2026-10-16: the owner has no reliable
macOS test environment and macOS verification will not be pursued, superseding
the 2026-10-14 "intended supported target" framing above. macOS is now a
standing unsupported, build-only target, not dropped: keep producing and
distributing osx-x64/osx-arm64 cross-publishes, but stop tracking macOS
physical verification/checks as pending or deferred work anywhere in the
documentation or checklist. Clearly inform users in README.md and release
notes that macOS builds are untested and unsupported. See the technical
specification section 4.2 amendment for the full policy text.

Owner approved the editing-roadmap correction on 2026-10-15: Phase 2B is a
separate staged-editing/Save As expansion milestone before Phase 3. It covers
the required raw/VHD/VHDX, filesystem-only/MBR/GPT, FAT/ordinary-NTFS stacks and
FAT long names. Phase 3 must qualify direct commit per required stack.
ISO/UDF/bridge, BIN/CUE and conventional 360K/720K delivery is explicitly
assigned to Phases 4, 6 and 7.10 respectively. See the checklist and
[planned capability matrix](docs/support-matrix.md#required-editing-delivery-roadmap).
Owner separately authorized Phase 2B implementation on 2026-10-15, after the
1.0 scope correction. The subsequent Phase 2C authorization below does not
authorize Phase 3 or any later phase.
Keep historical Phase 2 completion limited to its verified envelope; required
unverified rows stay incomplete/blocked unless the owner explicitly approves
a scope decision with a named follow-up milestone.

Owner explicitly authorized the Phase 2C roadmap/specification change and
reader implementation on 2026-10-15, before Phase 3. Qualify NTFS 1.2 (NT4),
3.0 (Windows 2000) and 3.1 (XP+) read-only subsets, including bounded supported
attribute lists/extensions and legacy metadata. The bounded reader is implemented;
remaining 1.0 verification/review gates stay open, not phase complete.
Owner explicitly deferred authentic NT4/Windows 2000/XP-created-media
qualification and dependent reader/writer interoperability checks on 2026-10-15
to the checklist's post-1.0 P1.NTFS.1-P1.NTFS.6 milestone. These are not 1.0
blockers or passed tests; VM-authored media suffices, without vintage hardware.
Keep automated tests, safety bounds, Tower source-integrity tasks, general
platform testing and Phase 3 writer/commit safety in 1.0; macOS is excluded
from general platform testing as a standing unsupported, build-only target
(2026-10-16 policy change above). Do not waive 2B.6 current Windows-
authored-media/security-reference/LFS 2.0 gates or infer Phase 3 authorization.
Stop for owner review. Checklist 2C.1-2C.7, 2C.V1-2C.V8 and 2C.E1-2C.E6 began
pending. Implemented bounded attribute-list/version reading has 49/49 current
focused passes (35 attribute-list, 14 version/layout/service).
Full verification passed 1001/one Windows-only skip, 1002 total;
four focused cases were added after the full run's build. That snapshot's inventory
was 1006 including the skip, not a claimed full 1005-pass run. Final repeat all-six
self-contained publish/local Linux AppImage passes on latest source, as does the
bounded owner read-only probe. No versioned release or GitHub publication changed.
Cross-publishes are not physical compatibility; 2C.V6a is verified and parent
2C.V6 stays open.
Exact checked subitems and open
gates are tracked in the checklist/platform record. Synthetic version-field tests are not authentic legacy qualification;
no authentic NT4 fixture exists yet.
Newly readable attribute-list/legacy volumes must remain noneditable until
Phase 3 items 3.0.1-3.0.5 qualify staged editing, Save As and new-output NTFS
writing BEFORE direct commit under 3.7. Preserve ALL existing qualified Phase 2B
editing; actual Windows-authored-media/security-reference and LFS 2.0/cache-safety
write gates remain incomplete in 2B.6, not moved or scope-demoted.
The approximately 8.42 GB raw Tower NTFS 3.0 owner image stays outside the
repository: do not copy, modify or distribute it. Initial rejection concerns
the live directory attribute list at MFT 26 referencing indexing extension 8336;
the independent native index warning is now diagnosed as ntfs-3g's 65536-byte
readall limit versus the 344064-byte index, not corruption evidence.
Initial in-place enumeration succeeds, not full content/health qualification.
Reader acceptance or a tool-limit diagnosis never establishes write safety.
See [Phase 2C plan](docs/support-matrix.md#phase-2c-ntfs-reader-qualification-plan).
Keep the signed reader and strict writer validation defaults unchanged.
The separate read preflight resolves bounded ordinary lists/extensions; MFT
bootstrap lists remain explicitly unsupported. Follow the documented list/
extension/index budgets and bitmap-selected active-index traversal, while still
structurally validating inactive MFT records without resolving their references.

The current roadmap targets 1.0. VMDK/VDI/QCOW2/DMG/ext2/ext3/ext4/exFAT/HFS+/APFS
target qualified read-only subsets where feasible. Their writers, modified Save
As, commit/rebuild and creation/formatting are outside 1.0, not unassigned gaps;
no future writer milestone/date is required or promised. Phase 7 reader
evaluation/fixtures/tests remain required. Detection is not browsing/extraction;
document exact reader blockers for owner review, never silently drop them.
Apply read-only caps across containers and filesystems compositionally. Internal
parsers determine support, not the host OS's native mounting capabilities.

Owner separately authorized both bounded ISO reader refinements on 2026-10-15:
empty-file unused-extent compatibility and efficient ISO9660/Joliet directory
enumeration without raising budgets. The Toshiba recovery ISO remains local,
unmodified, untracked and undistributed, never a fixture or package input.
No optical writing/rebuild, Phase 3 or release-publication authority follows.
See [reader evidence](docs/platform-status.md#iso-reader-compatibility-refinement-2026-10-15).

Windows 11 Home x64 unattended SSH command access was verified on 2026-10-15.
This is infrastructure only, not Windows build/test, security-reference,
Windows-authored NTFS or GUI qualification. Keep those checklist gates open.
Keep private keys outside the repository/synchronized storage and connection
addresses/key inventory in local-only records, not committed documentation.
Use explicit-key, publickey-only BatchMode and strict host checking for finite
remote commands; the enrolled restricted key requires no PTY (`-T`).
Do not bypass a host-key mismatch or elevate the standard testing account.
GUI smoke needs a separately prepared interactive desktop. This setup does not
authorize Phase 3, real-disk operations or recurring automation.

## Phase 2D authority and retained closeout (2026-10-17)

Owner authorized Phase 2D documentation/implementation after a read-only preflight.
The prerequisite gate was resolved in this child chat before actual work.
Use checklist 2D.1-2D.9/2D.E1-2D.E4 to close retained 2B/2C requirements;
reassignment never marks the historical tasks passed. This supersedes stale
"Phase 3 unauthorized/unimplemented" current-state wording above: retain the
owner-authorized 2026-10-16 backend increment and its evidence, but do not
continue remaining Phase 3 work. No Phase 4/5/6/7, PMFX or publication authority.

Windows strict-key access is restored; keep the account standard and connection
inventory local-only outside synchronized storage. User-local pinned tools,
project transfer and disposable generated test media are authorized. Owner will
launch narrowly bounded finite privileged Windows-native virtual-disk steps;
never elevate the account/agent, accept arbitrary privileged commands, use real
disks or install a service/schedule. BCL codec comparison does not prove complete
inheritance or Windows filesystem interoperability. Qualify current Windows
ordinary NTFS media and clean LFS 2.0/cache safety without replay/reset/repair,
gate removal or weakened allocation/security/metadata checks.

Tower permission: in-place Linux full before/after SHA-256 and bounded workflows,
representative selected temporary extraction <=64 MiB in private local storage
outside repository/Dropbox, followed by cleanup. No image copy/mount/transfer/
modification/distribution. Other owner-media restrictions remain unchanged.
Authentic historical authoring remains P1.NTFS; MFT-bootstrap lists unsupported;
newly readable legacy/attribute-list volumes NONEDITABLE until Phase 3 3.0/3.7.
Preserve every existing qualified FAT/NTFS write. ARM64 is currently unavailable:
owner approved proceeding, not waiving 2D.8c; keep it explicitly blocked.
macOS remains unsupported/build-only, both publishes retained, no physical checks.
Stop at 2D verification/owner review with exact evidence and concrete blockers.

Current 2D evidence: Windows codec 271/271; reader 66/66 each on Linux/Windows x64;
bounded Tower full before/after hashes and 1235021-byte selected extraction/native
comparisons pass, private outputs removed. Full Linux verify 1136 passes/one
Windows-only skip; all-six publishes/local AppImage pass. These close named
reader/Tower subitems, not full 2D or its remaining platform/review gates.
Windows native author/source-readonly reopen pass, but edited output is untested.
Native writable-attached fsutil reports LFS 2.0; detached raw/read-only report 1.1,
with exact transition not raw-captured. TxF/CLFS metadata independently blocks
ordinary writer qualification; do not strip or blindly preserve it as proof.
Modern-log/cache-safety investigation continues by owner direction with all
writer gates intact. See checklist/current platform evidence for exact chronology.
Owner subsequently selected bounded verified closeout/STOP for review after
the focused investigation found no sufficient TxF/CLFS offline quiescence/
update predicate. These requirements stay in 1.0, incomplete and unwaived.
Broader Windows execution: storage 400 pass/57 fail; Services 153 pass/3 fail.
Missing native tools, application log contention, direct-commit sharing and
preference error/cleanup-test mismatch remain explicit blockers, not platform
passes. No source-integrity assertions completed in failed Windows commit cases.
Do not continue implementation/remote probes or remaining Phase 3 automatically.

Owner then explicitly resumed the recommended corrective pass: repair retained
Windows commit-handle lifecycle and shared logging, verify preference failure/
cleanup semantics, and separate managed Windows writer execution from independent
Linux checks of the exact generated outputs. The fresh read-only preflight found
existing standard-user/tooling/generated-media permissions sufficient; no new
owner-media access or privileged Windows steps are needed. This supersedes the
immediate implementation stop only for those repairs/qualification. TxF/CLFS,
LFS 2.0 and unavailable ARM64 gates remain required and incomplete. Remaining
Phase 3 features and later phases stay on hold; no publication is authorized.

The final Global-session-corrected source passes Windows32focused,177Services,
392reader/security/safety,12guards,168writer and9growth selections, zero skips;
186exact outputs pass876independent Linux commands and84inputs stay unchanged.
This closes retained runtime/tooling defects, not Windows-authored TxF/CLFS,
LFS2/cache safety, GUI or ARM64 gates. Exact current source/media manifests and
compact verified archives: docs/platform-status.md and docs/windows-writer-handoff.md.
Final complete Linux verification passes1178/oneWindows-only skip (1179total),
zero buildwarnings/errors, format/resources/AppRun/fivecopies; allsixpublishes/
localAppImage pass. Correctivebatch is complete and STOPPED for ownerreview.
Do not infer permission
to continue remaining Phase3 or claim full Phase2D completion.

Owner subsequently authorized committing and pushing the verified changes on
2026-10-17, explicitly without a release. This permits source commit/push only,
not tags/releases, further phase implementation or waiver of retained blockers.
Keep owner media, generated payloads and private access records out of Git.

## Learnings

Zero-length ISO files can have unused out-of-range extent addresses: tolerate
only the empty-file case, keep other structural checks, warn and return an
empty stream without seeking. Never whitelist marker filenames or relax
block-device bounds; the pinned ISO extent stream seeks even for an EOF read.
The pinned VFS reconstructs parent directories for each path-based entry
property, so large nested ISO/Joliet listings need bounded batch metadata
decoding rather than increased parser budgets. Preserve the separate
Rock Ridge/SUSP path and regress metadata, names, content, caps and cancellation.

DiscUtils FAT writer success and reopened file hashes are insufficient write
validation: Phase 2 exposed a truncation-related lost cluster and incomplete
FAT32 auxiliary metadata despite correct file contents.
Use the qualified writer's allocation/metadata checks and independent
`fsck.fat -n`/mtools checks where available; do not replace these with tree/hash
checks alone.
Keep write fixtures separate from immutable read goldens, whose readable layouts
may intentionally fail stricter writer qualification.
See [current editing restrictions and evidence](docs/staged-editing.md) before
broadening FAT capabilities, especially the NFC BMP/ASCII-alias subset and
legacy floppy exclusions.

FAT lowercase short names use NT-reserved display flags, not necessarily LFN
records. DiscUtils resets ancestor-directory case flags during timestamp updates;
restore the changed entry and its ancestors after disposing the writer adapter.
Keep FAT lookup case-insensitive but output spelling validation ordinal/exact.
Phase 2B qualifies LFN/mixed-case/NFC BMP names separately; do not normalize
unsupported names or change untouched aliases. Partitioned FAT qualification
must compare the original BPB hidden-sector count against the absolute partition
LBA, including its FAT32 backup, without rewriting it to qualify the volume.

Existing-volume NTFS creation fails independently of formatting: DiscUtils ACL
inheritance calls unsupported RawSecurityDescriptor APIs on Linux and can leave
an indexed Security-ID-0 file before throwing. `ntfsfix -n` may still pass;
retain bitmap/index/security checks. Owner reaffirmed NTFS writing as required
for 1.0; the bounded investigation found binary SID/ACL/ACE/descriptor APIs fail
too and installed NTFS directory creation uses file inheritance. The owner
subsequently authorized the managed repair's implementation and qualification,
superseding that investigation's stop point, not the Phase 2B/Phase 3 boundary.
Keep the isolated source-repair assembly distinct from unchanged signed packages;
only independently qualified ordinary-volume subsets may enable writing.
Do not demote required NTFS writing to read-only or treat narrower probes as
complete delivery.
See [dependency evidence](docs/dependency-evaluation.md).

Existing `$SDS` descriptor hashes cover the original stored bytes; canonicalize
new insertions only, not the input before checking an existing record's hash.
Self-relative component order/alignment can differ while the descriptor remains
valid, so a canonical reserialization is not necessarily its original byte span.

Imported NTFS code must retain checked address/count arithmetic. Intentional
bitmap low-byte complements and signed run-delta reinterpretation need narrowly
scoped unchecked conversions; run lengths remain unsigned/bounded and header
zero is a one-byte terminator. MFT mirror extent is
`max(4 * recordSize, clusterSize)`, not always four records; regress large-cluster
fixtures and native backward/full-volume runs before broadening the writer.

Pre-XP NTFS FILE records with USA at 42/attributes at 48 use bytes 44..47 as
fixup slots, not NTFS 3.1 MFT identity; check header offset 44 only with the
extended layout (USA offset >=48).
Resident `INDEX_ROOT` compression flags can mean directory defaults, and empty
nonresident metadata may have no runs/highest VCN 0 or maximum only when
allocation/data/initialized lengths are all zero; never widen writer defaults.
The pinned `ntfsinfo` 2022.10.3 readall 64 KiB guard can mislabel a large
`INDEX_ALLOCATION` corrupt; compare bounded index graphs and independent native
directory/stream reads rather than assume corruption.
See [reader bounds](docs/storage-phase1.md#resource-cancellation-and-concurrency-policy)
and [pinned evidence](docs/platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15).

DiscUtils VHDX ContentStream.Flush is unimplemented. The qualified writer uses
synchronous payload/BAT writes, disposes the container, flushes the underlying
file to disk, then independently reopens and checks metadata and virtual bytes.
Preserve that ordering; a successful tree read is not equivalent to a durable
container flush.

Direct commit must suspend the original reader before source-write acquisition
and validate against a reopened, hash-checked backup, not disposed reader state.
Linux uses `F_OFD_SETLK`: ordinary process-owned `FileStream.Lock` loses
protection when any reader of that inode closes, so retain the unrelated-reader/
independent-writer regression. Windows uses deny-write sharing plus one
`FileRenameInfoEx` replace/POSIX operation; never unlock early, widen ordinary
reader sharing or substitute `File.Replace` with its partial-failure/merge
semantics. The native filename buffer must preserve exact UTF-16 code units and
include a trailing NUL excluded from its byte-length field.

Dynamic containers can grow physically after an ordinary qualified edit:
source/backup hashes use input length, committed-output hashes use actual output
length, and successful commit must refresh the session baseline/reset staging.
An earlier journal stage does not prove finalize never happened; recovery must
refuse a source that differs from its backup instead of rolling back a possibly
completed atomic replacement. Keep exact-input Windows execution and independent
Linux validation of the same output hashes; Linux authoring/check tools are not
a product Windows runtime dependency or permission to skip coverage.
See [commit architecture](docs/architecture.md) and
[portable verification](docs/windows-writer-handoff.md).

Named mutexes need the `Global\` namespace on Linux as well as Windows:
unprefixed .NET mutexes do not exclude writers in a different Linux session.
Establish the test child's session before CLR startup (`setsid --wait`), not
from managed `Main`, because the runtime can retain its original mutex session
namespace. Keep the actual log-gate before/after regression and independent-
session append/rotation coverage, not just same-session worker processes.
