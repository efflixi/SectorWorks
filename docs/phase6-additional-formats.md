# Phase 6 additional formats and filesystems

Phase 6 delivers read-only opening, browsing and file extraction for the
bounded layouts below. The owner review remains open. Vendor PMFX restoration,
the legacy Ghost sample and the specific media issues described below still
need disposition before Phase 6 can close.

The [0.6 development prerelease](prerelease-v0.6.md) packages this snapshot
with version metadata and reuses the Phase 6 regression evidence below.

The readers run inside SectorWorks on Windows and Linux. They use the normal
open, browse, preview and extraction paths. Writes to the newly supported
containers and filesystems remain disabled. Ordinary FAT and NTFS write
qualification keeps its existing checks.

## Reader coverage

| Format | Supported layouts | Limits |
| --- | --- | --- |
| VMDK | Standalone VMware version 1 monolithic sparse, with bounded primary and redundant grain maps | Split extent sets, descriptors with an external payload, parents and compressed stream-optimized images are unavailable. |
| VDI | Standalone VirtualBox 1.1 fixed and dynamic maps | Differencing, parent images and layouts outside the block-map bounds are unavailable. |
| QCOW2 | Clean standalone v2/v3, ordinary uncompressed allocations and supported zero clusters | Backing chains, snapshots, compression, encryption and dirty/unsupported feature sets are unavailable. |
| DMG | Single-segment UDIF v4 XML with ordinary raw, zero and complete zlib runs | Encrypted, split, resource-only and other codec profiles are unavailable. |
| ext2, ext3, ext4 | Clean revision 1, 1–4 KiB blocks and bounded supported feature bits. Ordinary entries and data, including extents in the tested ext4 profile. | Recovery, journal replay, unsupported feature bits and extracting links or special files are unavailable. |
| exFAT | Clean 1.0 single-FAT media with validated boot copies, entry-set and name checksums, allocation bitmap, and contiguous or chained streams | TexFAT, multi-FAT and unsupported volumes are unavailable. |
| HFS+ | Clean unjournaled case-insensitive v4 with a bounded catalog and ordinary data forks | HFSX, journals, hard links, compressed forks, resource-fork export and extent overflow are unavailable. |
| APFS | Single-volume, unencrypted 4 KiB containers with a validated current descriptor-ring checkpoint, object maps, ordinary directories and data extents | Snapshots, encrypted, sealed, multivolume and Fusion layouts, compressed attributes and special-file extraction are unavailable. |
| PMFX | Observed 512-byte-sector, 1 MiB-block, single-table layouts with settings 1/9, modes 2/4, sparse, verbatim, zlib and one bounded fragment | Supported as a preview reader pending the vendor oracle. Multi-fragment and other codecs or geometries are unavailable. |
| ImageDisk IMD | Uniform 512-byte sectors with bounded cylinder/head/sector maps and verbatim or byte-fill data | Unavailable, deleted, error-state and nonuniform sectors cause an explicit refusal. The same reader can identify an IMD file named IMG. |
| Standalone raw CD | Single 2,352-byte data track with recognizable ISO framing, Mode 1 or Mode 2 Form 1 | Mixed-track payloads require their layout descriptor. Raw audio needs a declared track layout. |
| CloneCD CCD/IMG/SUB | Unscrambled single-session v2/v3, validated TOC and track indexes, ordinary CD data or audio, optional size-matched SUB | SUB is reported as a companion. Its subcode content is outside this qualification. |
| Alcohol MDS/MDF | Version 1 CD descriptors with one same-stem payload, 2,048/2,352/2,448-byte qualified layouts, bounded sessions and tracks | Split sets, DVD, and nonordinary program modes are unavailable. Interleaved subchannels are omitted from the ordinary program stream. |
| Nero NRG | NERO and NER5 descriptor footers, single-session DAO tracks with bounded spans and matched cue entries | TAO, multisession and unsupported sector modes are unavailable. |
| Norton Ghost GHO | Single unencrypted linear partition format with verbatim, Fast LZ, or complete zlib blocks | The owner’s legacy C1.GHO uses a different record envelope and remains unavailable. Split/encrypted images are unavailable. |

The new CD containers provide raw program-area inspection and byte-preserving
WAV export for qualified audio. An independent reference uses little-endian
samples for these containers. Generated WAV tests check the complete PCM data
chunk. Pre-emphasis and four-channel layouts remain disabled for audio output.

## Existing media reopened

| Prior issue | Current disposition |
| --- | --- |
| DREAMWEB, HEXEN_V10 and Earthsiege2 CUE/BIN | All three CUE sheets open for data browse and audio. Stored INDEX 00 sectors and the declared unstored PREGAP stay separate. Use the CUE to open its BIN. Rebuild remains disabled for this layout. |
| POWERPNT.CUE | The file starts with a binary header and fails the text parser. Valid BOM-marked UTF-16 sheets now open read-only under the same CUE structure and companion requirements. |
| MK3CD CUE and payload | The sheet names `CD.iso`, which is absent. `MK3CD.iso` is present and fits all 47 declared INDEX 01 positions. Its Mode 1 data starts at sector 0, its ISO descriptor sits at sector 16, and the first declared audio start at sector 14,250 is outside data framing. That supports a misnamed companion. SectorWorks still requires the confirmed companion name or a corrected CUE. |
| Corel Linux, Lindows and Mandrake ISOs | The read-only Rock Ridge path validates CL/PL/RE links, extent bounds and acyclic physical and logical parent paths. The physical relocation directory can also appear. |
| Wing Commander IV CD 2 and CD 6, Windows XP SP1 ISO | Their advertised volume sizes exceed the actual images by 74, 50 and 150 blocks, respectively. The read-only ISO path verifies every referenced directory and file extent fits before exposing the namespace. Rebuild stays disabled. |
| Audigy 2 ZS ISO | Both ISO and Joliet entries agree on SUPPORT.CHM at block 289,417 with a length of 54,667 bytes. The physical image ends after block 289,422, leaving 42,379 referenced bytes missing. It remains rejected. |
| FAT32EBD IMAGE.DSK | The 36,864-byte input declares a 1,474,560-byte FAT12 volume. It lacks the complete declared data area. |
| DOSWIN bootdisk.img and bootdisk_eltorito.img | Read-only FAT12 access uses the mirrored `FF FF FF` reserved entries despite the `F0` BPB marker. The writer still refuses the disagreement. |
| deathgate.iso | The named Nero copy lacks its footer and track layout. The intact `.nrg` opens and exposes the ISO filesystem. |

The 2026-11-19 full compatibility inventory remains the v0.5.5 baseline.
The focused in-place follow-up recorded 50 observations. It found five FAT16
and six FAT32 PMFX inputs that opened and listed the root, ten CloneCD sets,
five MDS/MDF sets, and the intact Nero image. File identity, length, and
timestamps stayed equal across those probes. The smallest PMFX and 22 sheet
or descriptor files also matched before/after SHA-256. Source payloads stayed
on the owner's volume. The two DOSWIN floppies still report unused clusters
under `fsck.fat -n`; the compatibility change allows bounded reading only.

## Conventional floppy editing

Phase 6 enables direct commit on the independently checked unpartitioned
360 KB and 720 KB FAT12 profiles. They use 512-byte sectors, 9 sectors per
track, two heads, a signed extended BPB, two identical FATs and 112 root
entries. The two profiles use the respective FD/2-sector FAT and F9/3-sector
FAT patterns. Existing allocation, capacity, boot-sector, backup, journal
and source replacement checks still apply. Early or nonconforming profiles
retain their separate access limits.

Four outputs from independent mkfs.fat-authored fixtures (a Save As and a
direct commit at each capacity) passed `fsck.fat -n`, byte comparisons of
the original boot record, and mtools reads of both the original file and a
new multi-cluster file. The sources used for comparison remained intact.

## DiskGenius verification

Generated FAT16 and FAT32 controls opened through SectorWorks and extracted
the expected proof file. The latter decoded byte-for-byte against its
independently authored raw input, including sparse, verbatim, chained and
partial blocks. Codec tests reject malformed termination, checksum, extra
input and output overflow. This qualifies the tested decoder behavior.

A generated 1,056,768-byte PMFX with a 64 GiB virtual span opened in 40 ms
on the Linux test host. Its 256 random reads across mapped and sparse areas
matched the expected bytes in 17 ms. Peak process working set was 42.3 MB
and a pre-cancelled read stopped before I/O. These figures describe that run.

DiskGenius Portable 6.2.0.1829 x64 had a valid publisher signature. Windows
confirmed elevation was required to launch, and the owner approved it. The
test used an internally opened disposable virtual disk. The Free Edition
blocked saving the PMFX image behind the paid edition. No vendor PMFX was
produced and no vendor restore ran. The owner can make that comparison on
the licensed work PC. Keep the checklist’s independent restore, NTFS PMFX
and broader evidence requirements open.

## Independent reader comparisons

The 16 MiB virtual byte streams from VMDK, VDI, QCOW2, DMG, GHO and PMFX
all matched their common independently generated raw source by SHA-256.
The APFS walk matched all 410 supported file hashes from the fixture's
author. Its one compressed attribute was reported as unsupported. A
separately generated empty APFS container also opened.

## Windows laptop verification

The current Win11 x64 laptop ran the focused selection using the pinned SDK
and standard privileges. It passed 78 tests with four platform-specific skips.
The exact published app displayed PMFX and APFS in the desktop, including the
read-only notices. PMFX file preview and GUI extraction produced the 35-byte
proof with the same SHA-256 as the original. The PMFX input hash matched.

The unfiltered run returned 1,283 passes, 20 skips and 1,221 failures. Of the
failures, 1,199 came from unavailable fixture tools (`qemu-img`, `ntfsfix`,
`mkntfs`, `ntfscat`, `ntfsfallocate`, and `ntfscluster`), 21 came from running
mixed-volume tests without their required output root, and one was a transient
sharing violation after a forced process termination. All four interruption
stages passed on isolated rerun. The configured mixed-volume rerun passed all
21 cases and its 63 exact input, Save As and commit images passed independent
Linux verification. The default Windows suite remains an incomplete platform run.

The desktop session is closed and the manual tunnel remains available. The
task directory and its 2,093 files were removed after receipt verification.
Raw logs and approved generated data stay in the private evidence archives.

## Linux regression and review packages

The final source passed **2,552 tests with one Windows-only skip and zero
failures** on Linux x64. The initial runner returned 143 before its
storage suite finished. Its four completed suites contributed 342 passes.
The storage assembly ran again in three disjoint sections on the same
source and build, yielding 1,307 general passes, 462 expanded Save As
passes and 441 expanded commit passes. The counts include the new
reader, audio, extraction and floppy tests. Formatting, UI resources,
AppRun contracts and the five specification/design copies passed.

All four self-contained RID outputs built and match their review
packages. The Linux x64 AppImage passed its native resource smoke
and the Windows x64 package ran the desktop checks above. Physical
ARM64 remains deferred under the existing owner decision.

The [review packages](../artifacts/phase6-review/packages) contain
the Linux x64 AppImage, Linux ARM64 tarball, and Windows x64/ARM64 ZIPs.
The separately packed [DiskGenius check](../artifacts/phase6-review/DiskGenius-vendor-check.zip)
has a generated source VHD, a blank target VHD and the vendor comparison
steps. The [evidence index](../artifacts/phase6-review/evidence-index.json)
links the exact test summaries, checksums, and archive restore maps.

Phase 6 stopped for owner review after the achievable implementation
and verification. The vendor restore comparison and specific format
limits above remain on the checklist. Phase 7 stays pending.

## Evidence and provenance

- The [Phase 6 fixtures](../fixtures/phase6/README.md) record legal source and authoring provenance.
- [Apple File System Reference](https://developer.apple.com/support/downloads/Apple-File-System-Reference.pdf), [HFS Plus Volume Format](https://developer.apple.com/library/archive/technotes/tn/tn1150.html), and [Microsoft exFAT Specification](https://learn.microsoft.com/en-us/windows/win32/fileio/exfat-specification).
- [LibMirage optical format readers](https://github.com/cdemu/cdemu/tree/master/libmirage/images) and [GNU ccd2cue CUE sheet reference](https://www.gnu.org/software/ccd2cue/manual/ccd2cue.html#CUE-sheet-format).
- [DiskGenius generated virtual disks](https://www.diskgenius.com/manual/operate-virtual-disk.php) and [DiskGenius PMFX backup modes](https://www.diskgenius.com/manual/backup-disk-to-image-file.php).
- [QEMU image format documentation](https://www.qemu.org/docs/master/system/images.html), [DiscUtils](https://github.com/DiscUtils/DiscUtils), and [SharpZipLib](https://github.com/icsharpcode/SharpZipLib).
