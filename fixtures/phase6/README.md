# Phase 6 reader fixtures

These fixtures are for automated tests. The application packages exclude them.
All local authoring used disposable ordinary files. The proof text in the
SectorWorks-generated layouts is `SectorWorks Phase 6 legal fixture.\n`.

| Fixtures | Provenance |
| --- | --- |
| `disk.vmdk`, `disk.vdi`, `disk.qcow2` | QEMU conversion of the generated 16 MiB FAT16 source. The VMDK uses a monolithic sparse extent. |
| `disk.dmg` | Generated single-segment UDIF with zero and zlib runs over the FAT16 source. 7-Zip independently listed the file. |
| `disk.pmfx`, `chain-fat32.pmfx` | Generated encoders over Linux-authored FAT16/FAT32. The second includes a single fragment, verbatim mapping, missing allocation, and a short terminal logical block. These are decoder controls pending a vendor comparison. |
| `disk.gho` | Generated single-partition Fast LZ archive using [nyarime/gho at the pinned commit](https://github.com/nyarime/gho/tree/40af0e8eca5ce80aec8788320a98faadb666cb40). The upstream decoder reproduced the original raw partition hash. |
| `floppy.imd` | Generated ImageDisk track map over a FAT12 floppy with verbatim and byte-fill sectors. |
| `file-ext2.raw`, `file-ext3.raw`, `file-ext4.raw` | e2fsprogs formatted and populated these ordinary 16 MiB filesystems. The ext4 fixture omits unsupported metadata checksums. |
| `exfat.raw` | exfatprogs boot and allocation structures plus a generated nested file. `fsck.exfat -n` independently verified the populated image. |
| `hfs-content.raw` | 64 MiB HFS+ image from `mkfs.hfsplus`; populated with [libdmg-hfsplus](https://github.com/planetbeing/libdmg-hfsplus). `fsck.hfsplus -n` and independent extraction passed. |
| `track.bin`, `clone.*`, `alcohol.*`, `nero5.img`, `nero4.nrg` | Generated from a genisoimage-authored ISO. Raw CD framing stores the same payload. The CCD and MDS examples include sized subchannel placeholders to exercise program offsets. |
| `audio.*`, `music.*` | One generated sector of known little-endian PCM in each optical container. The tests compare each WAV data chunk byte-for-byte. |
| `rock-ridge.iso` | genisoimage with Rock Ridge relocation of ten nested directories and the proof file. |
| `apfs.img` | The exact gzip member `tests/fixtures/apfs-fixture.img.gz` from [qnxprobe at f485dbfc3a98370185a506478279afdf86e1701e](https://github.com/abrignoni/qnxprobe/tree/f485dbfc3a98370185a506478279afdf86e1701e/tests/fixtures). Retained under MIT with [the upstream license](APFS-fixture-LICENSE.txt). Its manifest supplies expected file SHA-256 values. |
