# Foundation fixtures and direct-library experiments

These are synthetic, legally generated test media, not operating-system media.
They contain the text `SectorWorks foundation sentinel v1\n` in `SENTINEL.TXT`.
They are **not bootable** and establish no SectorWorks application support.
No source image is mounted. All writes target generated or disposable images.

## Immutable golden catalog

`fixtures/golden/manifest.json` lists every image's expanded byte length and
SHA256 plus its gzip byte length and SHA256. Keep golden archives and manifest
immutable after review: intentional replacements require documented review of
both content and manifest, not silent hash updates to make tests pass.

| Golden | Expanded bytes | Contents / purpose |
|---|---:|---|
| fat12.img.gz | 1,474,560 | 1.44 MB raw FAT12 floppy; DOCS directory and sentinel |
| fat16.img.gz | 16,777,216 | Raw FAT16; actual FAT variant asserted |
| fat32.img.gz | 536,870,912 | Raw FAT32; actual FAT variant asserted |
| mbr.img.gz | 33,554,432 | MBR, one FAT partition; count/bounds/content checked |
| gpt.img.gz | 33,554,432 | GPT, one FAT partition; count/bounds/content checked |
| ntfs.img.gz | 33,554,432 | Independently authored NTFS; resident sentinel + nonresident LARGE.BIN |
| joliet.iso.gz | 59,392 | ISO9660 + Joliet, DOCS and `Unicode-λ.txt`; both name views checked |
| dynamic.vhd.gz | 2,100,224 | 32 MiB virtual dynamic VHD, FAT12 volume at offset 0 |
| dynamic.vhdx.gz | 37,748,736 | 32 MiB virtual dynamic VHDX, FAT12 volume at offset 0 |
| truncated-fat.img.gz | 31 | Actual leading bytes of valid FAT12 image, truncated |
| corrupt-fat.img.gz | 512 | Valid FAT12 boot sector with bytes/sector field zeroed |
| truncated.iso.gz | 31 | Actual leading bytes of valid ISO, truncated |
| truncated.vhd.gz | 31 | Actual leading bytes of valid dynamic VHD, truncated |
| truncated.vhdx.gz | 31 | Actual leading bytes of valid dynamic VHDX, truncated |

Totals: **695,694,972 expanded bytes**, **802,460 compressed bytes** (14 gzip
archives; approximately 784 KiB, excluding manifest/docs). The VHDX golden's
container overhead/allocation is larger than its virtual capacity; do not
confuse file length with virtual disk length. FAT32's 512 MiB zero-heavy golden
compresses to approximately 510 KiB; it is never loaded into a single array.

The VHD/VHDX FAT volume deliberately occupies only the first floppy-sized
portion of the virtual disk. This is a filesystem-only synthetic layout, not
a partitioned operating-system disk. Fixed and differencing containers, missing
parents, GPT backup/CRC damage, FAT cycles, full volumes, NTFS compression,
sparse files, links, deep trees, and boot media are not all represented by this
immutable foundation catalog. Later disposable fixtures are documented below.

## Phase 2B disposable writer and interoperability fixtures

`DiskWriterFixtures` generates separate, exact-sized conventional FAT12/16/32
volumes, valid primary MBR/GPT layouts, sentinel partition gaps and multi-volume
images. It converts raw media with `qemu-img` to independently authored fixed/
dynamic VHD and fixed/dynamic VHDX; the fixed-VHDX reader-only synthetic fixture
is not substituted for that evidence. No immutable golden is made writable or
silently changed to satisfy stricter writer checks.

The matrix has 63 FAT rows: five container variants, three layouts and three FAT
variants at 512 logical sectors, plus separate 512/4096 physical-sector VHDX
rows. Outputs reopen through both the internal parser and qemu raw conversion;
their virtual hashes agree. Extracted bounded volumes run `fsck.fat -n` and
mtools when available. Long-name fixtures separately cover aliases, NFC BMP
Unicode, directory growth/fullness, metadata and allocation regressions.
`qemu-img` is a test-authoring prerequisite, not an application runtime dependency;
the available host uses qemu 8.2.2, dosfstools 4.2 and mtools 4.0.43.

NTFS modification probes create disposable copies of the immutable independently
authored NTFS fixture; fresh `mkntfs` media independently reproduces creation's
ACL blocker. Native NTFS tools and managed bitmap/index/security checks provide
evidence beyond content hashes. Failed probes are not product outputs or
qualified write fixtures; see [the dependency finding](dependency-evaluation.md#phase-2b-existing-volume-ntfs-blocker-2026-10-15).
The subsequent bounded managed-repair investigation adds seven read-only
dependency-boundary tests: five security-primitive constructor probes and two
installed-assembly inheritance-call checks. They describe the approved
dependency's defects, not a qualified fix or an NTFS write fixture. Update those
baseline expectations deliberately when a reviewed dependency patch is adopted.

The managed repair is a separate assembly; the original-package diagnostics
therefore remain unchanged. `NtfsStagedWorkflowTests` adds 21 actual Services
disk rows and fresh `mkntfs` fixtures covering cluster sizes 512 through 65536,
all five operations, transitions, growth, independent metadata/ACL preservation,
multiple NTFS volumes and FAT/NTFS composition. `ntfsfallocate` authors a genuinely
full volume independently; near-full replacement must leave exactly 16 free
clusters. Allocation/security checks and native tools are not replaced by hashes.
`ManagedNtfsRunTests` supplies compiled mapping-pair binary vectors.
Clean LFS 1.1 fixtures are independently specified synthetic restart pages,
not Windows-authored media. Actual Windows reference fixtures and LFS 2.0 remain
qualification gaps. The one Windows-only codec oracle is explicitly skipped on
Linux; it is not counted as passed.

The actual desktop `--phase2b` harness authors its own legal FAT12 MBR disk and
optionally converts it with qemu. It exercises rendered commands with mixed-case
directory and Unicode file names, not owner-supplied or operating-system media.
Diagnostic output and screenshots stay in ignored `artifacts/`; no proprietary
owner media is used. Prefix its variant with `ntfs-` to author a fresh NTFS
fixture with `mkntfs`/`ntfscp`; `ntfs-raw`, `ntfs-raw-mbr`, and both VHD/VHDX
variants have real Mint/X11 evidence and independent output checks.
No proprietary sample enters a fixture, archive or release.

## Repeatable verification

From the repository root, use the portable repository test entry point:

```sh
./scripts/test.sh
```

For optional dependency-only tests or direct probe commands, discover the
foundation-selected SDK in a Bash shell using the shared script:

```sh
source scripts/common.sh
"$DOTNET" test tests/SectorWorks.DependencyTests/SectorWorks.DependencyTests.csproj
```

SDK discovery honors `SECTORWORKS_DOTNET`, then `dotnet` on PATH, then the
user-local SectorWorks SDK installation. No developer-specific path is required.
The full `./scripts/verify.sh` workflow also includes these dependency tests.

The dependency suite contains **27 tests**, no skips: the 20 foundation cases
below plus seven NTFS security-boundary cases. It hashes/decompresses
every golden, reopens all nine normal images with the
direct library and checks structures/sentinels, runs three disposable FAT write
round trips, checks read-only rejection, asserts complete catalog membership,
and runs five malformed cases in child processes (15-second individual budget).
The 1 TiB virtual dynamic-VHDX test separately checks bounded reads and content
round trip without physically allocating a terabyte. No native tool is invoked.

After sourcing `scripts/common.sh`, the probe can independently expand and
inspect the full catalog:

```sh
"$DOTNET" run --project tools/SectorWorks.DependencyProbe -- \
  verify fixtures/golden fixtures/inspection-work
```

The output directory must not already contain the named images: materialization
uses `CreateNew` and never overwrites existing data. `fixtures/*-work/` is ignored;
delete only the disposable directory you created after inspection. Budget about
664 MiB free space for an expanded complete catalog (plus tests/build output).
The CLI verifies hashes of malformed entries but does not itself assert parser
rejection; use the test command for that. A malformed child can also be run as:

```sh
"$DOTNET" run --project tools/SectorWorks.DependencyProbe -- \
  malformed fixtures/inspection-work/truncated.vhdx
```

That child prints the observed exception and exits 1 on rejection; an unexpectedly
accepted image exits 0. The test runner supplies the external deadline and
asserts the particular expected error. `docs/dependency-evaluation.md` records
the observed errors and dependency limitations.

## Determinism and initial authoring

**Deterministic regeneration for tests is streaming decompression of the
committed compressed goldens**, followed by exact expanded SHA256 comparison.
This works on any host with the managed SDK; no timestamp/random identifier
regeneration, native tools or mounting are required. The seed authoring API
itself is deliberately **not claimed byte-deterministic**: DiscUtils creates
timestamps, volume IDs/GUIDs and other metadata. Re-running the original seed
recipe creates new candidates, not byte-identical goldens. Never use seeding as
routine verification or overwrite reviewed goldens with its output.

For provenance, the initial NTFS image was created with the already installed
ntfs-3g `mkntfs`/`ntfscp` utilities on a regular 32 MiB file. This independent
authoring path was necessary because DiscUtils's NTFS formatter throws
`PlatformNotSupportedException` on Linux .NET 10 (Windows principal/SID APIs).
The recipe below is for an **entirely new candidate directory**, not the golden
directory; `seed` refuses an existing manifest.

```sh
source scripts/common.sh
mkdir -p fixtures/candidate-work
truncate -s 32M fixtures/candidate-work/ntfs.img
/usr/sbin/mkntfs -F -Q -T -L SECTORWORKS -s 512 -p 0 -H 4 -S 32 \
  fixtures/candidate-work/ntfs.img
printf 'SectorWorks foundation sentinel v1\n' > fixtures/candidate-work/sentinel.txt
python3 -c "from pathlib import Path; Path('fixtures/candidate-work/large.bin').write_bytes(bytes(i % 251 for i in range(8192)))"
/usr/sbin/ntfscp -f fixtures/candidate-work/ntfs.img \
  fixtures/candidate-work/sentinel.txt /SENTINEL.TXT
/usr/sbin/ntfscp -f fixtures/candidate-work/ntfs.img \
  fixtures/candidate-work/large.bin /LARGE.BIN
"$DOTNET" run --project tools/SectorWorks.DependencyProbe -- \
  seed fixtures/new-candidates-work fixtures/candidate-work
```

The retained seed code generates all other normal media with pinned direct
DiscUtils APIs, adds the sentinel, closes them, derives the damaged prefixes/
BPB corruption, compresses them, and writes the manifest. The foundation
verification reopens those media after creation; successful construction alone
is never treated as sufficient validation.

The installed authoring tools are not bundled/linked dependencies, required
runtime utilities, or production storage backends. SectorWorks-authored fixture
content follows the repository GPL-3.0-only license. No proprietary content or
upstream sample images are included.
