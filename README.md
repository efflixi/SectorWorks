# SectorWorks

SectorWorks is an open-source desktop app for browsing, extracting, and editing
disk images on Windows and Linux. Open a floppy image, virtual hard disk, backup,
or optical disc image and explore its files and tracks directly inside the app.
You can inspect a file as text or hex, search by filename, and extract files or
folders without mounting the image as a drive.

[Download SectorWorks](https://github.com/efflixi/SectorWorks/releases) · [Format support matrix](docs/support-matrix.md)

## Get started

- **Windows** — Download the ZIP for x64 or ARM64, extract it, and run `SectorWorks.exe`.
- **Linux** — Download the tarball for x64 or ARM64, extract it, and run `./SectorWorks`.
  Linux x64 also has an AppImage. Open a terminal in the download folder and run
  the following commands, using the filename you downloaded.

  ```sh
  chmod +x SectorWorks-0.6-linux-x64.AppImage
  ./SectorWorks-0.6-linux-x64.AppImage
  ```

  If FUSE is unavailable, prefix the launch command with `APPIMAGE_EXTRACT_AND_RUN=1`.

Keep `libs/`, `docs/`, and `licenses/` alongside the portable launcher. The
packages include the .NET runtime. Physical testing on ARM64 is still pending.

## Open, browse, and extract

1. Choose **File → Open Image…** (`Ctrl+O`), or drop an image onto the **Open**
   button. For a multi-file disc image, open its descriptor (such as `.cue`,
   `.ccd`, or `.mds`) and keep its companion files alongside it.
2. Select a volume, folder, or track in the structure tree. Use the search box
   to find files by name, or select a file and open **File Preview**,
   **Hex View**, or **Properties**.
3. Select files or folders and choose **Extract** (`Ctrl+E`). Choose a folder on
   your computer. Extraction leaves the image untouched and never overwrites
   existing destination files.

### Edit an image

On an editable volume, open the folder you want to change. Drag files from
your file manager into the file list, copy and paste them with `Ctrl+V`, or
use **Add**. You can also replace or rename files, delete entries, and create
folders. Use **Changes** to review pending edits, or **Undo** to step back.

- **Save As** writes a validated copy with your edits applied.
- **Rebuild** writes a new optical image or BIN/CUE set for supported layouts.
- **Commit Changes to Source** replaces the original disk image after
  confirmation and validation. SectorWorks only enables it for eligible images.

### Play and export CD audio

Open **Audio Player** to listen to supported tracks, including on mixed-mode
discs with a data track. **Disc Layout** shows the available tracks, indexes,
and gaps. Choose **Export Audio Tracks…** to save selected tracks as PCM WAV
files without re-encoding. To fetch titles, choose **Look up track names…** in
**Disc Layout**. This sends disc timing to MusicBrainz only when you request the
lookup. Confirm a match to use those titles in the workspace and WAV filenames.

## Formats and limits

| Type | What SectorWorks can open |
| --- | --- |
| Disk and floppy images | Raw images, VHD, VHDX, VMDK, VDI, QCOW2, DMG, and ImageDisk IMD |
| Backup images | DiskGenius PMFX and Norton Ghost GHO |
| Optical images | ISO 9660, Joliet, Rock Ridge, UDF, BIN/CUE, CloneCD CCD/IMG/SUB, Alcohol MDS/MDF, Nero NRG, and standalone raw CD data tracks |
| Filesystems | FAT12/16/32, NTFS, exFAT, ext2/3/4, HFS+, and APFS |

Each row covers supported variants. Editing is available for eligible FAT and
NTFS volumes in raw, VHD, and VHDX images, and selected optical layouts support
rebuilds. Many readable images are read-only, including the supported PMFX,
VMDK, exFAT, and APFS variants. The app explains why an opened image is
ineligible for editing or rebuilding.

SectorWorks cannot mount images as drives, operate on physical disks, burn
discs, repair or format filesystems, or resize partitions. Encrypted images,
split or differencing disks, and some damaged or unusual layouts may fail to open.
Optical rebuilds cannot commit over the source, and bootability is only
preserved for specific layouts. See the [support matrix](docs/support-matrix.md)
for exact limits.

## Build from source

Install the [.NET SDK pinned in global.json](global.json). On Linux, run
`./scripts/restore.sh`, then `./scripts/run.sh` from the repository root.
Use `./scripts/verify.sh` for local verification and `./scripts/publish.sh all`
to build portable packages. The Linux x64 AppImage build requires the pinned
tools installed by `./scripts/setup-appimage-tools.sh`.

[Architecture](docs/architecture.md) · [Platform and test details](docs/platform-status.md) · [Release notes](docs/prerelease-v0.6.md)

## License

SectorWorks code is licensed under [GPL-3.0-only](LICENSE).
[Third-party notices](THIRD-PARTY-NOTICES.txt) document dependency licenses.
