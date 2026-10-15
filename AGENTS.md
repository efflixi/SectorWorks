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

Owner approved the editing-roadmap correction on 2026-10-15: Phase 2B is a
separate staged-editing/Save As expansion milestone before Phase 3. It covers
the required raw/VHD/VHDX, filesystem-only/MBR/GPT, FAT/ordinary-NTFS stacks and
FAT long names. Phase 3 must qualify direct commit per required stack.
ISO/UDF/bridge, BIN/CUE and conventional 360K/720K delivery is explicitly
assigned to Phases 4, 6 and 7.10 respectively. See the checklist and
[planned capability matrix](docs/support-matrix.md#required-editing-delivery-roadmap).
Owner separately authorized Phase 2B implementation on 2026-10-15, after the
1.0 scope correction; no later-phase implementation is authorized.
Keep historical Phase 2 completion limited to its verified envelope; required
unverified rows stay incomplete/blocked unless the owner explicitly approves
a scope decision with a named follow-up milestone.

The current roadmap targets 1.0. VMDK/VDI/QCOW2/DMG/ext2/ext3/ext4/exFAT/HFS+/APFS
target qualified read-only subsets where feasible. Their writers, modified Save
As, commit/rebuild and creation/formatting are outside 1.0, not unassigned gaps;
no future writer milestone/date is required or promised. Phase 7 reader
evaluation/fixtures/tests remain required. Detection is not browsing/extraction;
document exact reader blockers for owner review, never silently drop them.
Apply read-only caps across containers and filesystems compositionally. Internal
parsers determine support, not the host OS's native mounting capabilities.

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

## Learnings

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

DiscUtils VHDX ContentStream.Flush is unimplemented. The qualified writer uses
synchronous payload/BAT writes, disposes the container, flushes the underlying
file to disk, then independently reopens and checks metadata and virtual bytes.
Preserve that ordering; a successful tree read is not equivalent to a durable
container flush.
