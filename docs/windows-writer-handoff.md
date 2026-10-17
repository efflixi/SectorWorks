# Portable writer-matrix verification handoff

This opt-in test pipeline does **not** require Linux authoring/check utilities
on Windows. It does not replace independent checks with managed reopen checks.
Only legal repository goldens and disposable generated images are inputs.
No native mounting, administrator operation, real disks, owner media, feature
gate changes, or writer-capability expansion is involved.

## Three required stages

1. **Linux preparation** runs the existing FAT/ordinary-NTFS Save As workflows,
   including their existing independent checks, and exports 84 input rows:
   raw/fixed and dynamic VHD/VHDX; filesystem-only/MBR/GPT; FAT12/16/32 and
   ordinary NTFS; VHDX physical-sector variants 512/4096. All required native
   tools must exist. A successful 84-case TRX, full per-image SHA256/length,
   authoring-tool hashes and source-file hashes seal `inputs/catalog.json`.
2. **Actual Windows execution** verifies the separately supplied catalog SHA256
   and source snapshot, copies and rehashes each input into a disposable test
   directory, and runs the existing 168 Save As/direct-commit matrix workflows.
   Their namespace/content/staging/history/managed-validation/source/residue
   assertions remain. Exact output containers are copied outside disposable
   test directories, with row/workflow/input/output hashes and partition bounds.
   Every independent check is explicitly **pending**, never passed or skipped.
3. **Linux return verification** requires exactly 168 successful Windows TRX
   cases, every expected queue entry and output, matching catalog/input/output
   hashes, and Windows provenance. It converts the exact returned containers
   with QEMU, independently parses MBR/GPT bounds/CRCs, compares those bounds
   with the queue, and extracts only disposable volume copies. It then runs
   mandatory `fsck.fat -n`/`mdir` plus `mtype` content checks, or the existing
   `ntfsfix -n`/`ntfsinfo`/`ntfscluster`/`ntfscat` checks and byte comparisons
   appropriate to each NTFS workflow. Returned containers are rehashed afterward.
   Only a complete successful report qualifies this cross-host matrix run.

Each retained image is bounded to 1 GiB: existing qualified FAT32 fixed
containers can exceed 512 MiB after container/partition metadata. This transfer
budget does not change any product reader/writer resource or capability gate.

Neither Windows test-runner success alone nor a pending queue is an independent
verification pass. Failed/missing tools, changed media/source, missing rows,
skipped/failed tests and incomplete handoffs are errors. Prior evidence is
single-use and is never overwritten; use fresh roots/report names for retries.
No input/output image is committed or distributed.

## Commands

From a restored Linux checkout with `qemu-img`, `fsck.fat`, `mdir`, `mtype`,
`ntfsfix`, `ntfsinfo`, `ntfscluster` and `ntfscat`:

```sh
python3 scripts/test-writer-handoff.py
python3 scripts/writer-handoff.py prepare --root artifacts/writer-handoff-run
```

Coordinate shared builds and freeze the source snapshot before preparation.
If that exact test build was already verified, `prepare --no-build`
reuses it and records its assembly SHA256; this avoids rebuilding shared projects
while separate verification runs. Select `--configuration Release` to reuse the
verified Release build (default: Debug). Do not use it with stale binaries.
Transfer `inputs/` and that exact source snapshot to the authorized Windows test
host through the owner's approved private channel. Verify the catalog hash from
`catalog.sha256` through the trusted Linux record; do not derive the expected
hash solely from an unverified transferred catalog. Transfer scripts in the same
snapshot. No addresses/credentials or transfer inventory belong in this document.

In a finite **standard-user** Windows PowerShell session, with the already
installed checksum-pinned user-local SDK:

```powershell
& .\scripts\windows-writer-handoff.ps1 `
  -SourceRoot $source -Dotnet $dotnet -Inputs $inputs -Outputs $newOutputRoot `
  -CatalogSha256 $verifiedLinuxCatalogSha256
```

The output parent must already exist; `$newOutputRoot` must not. If dependencies
are not restored for that source snapshot, perform the normal authorized
user-local restore first; the runner itself uses `--no-restore`. Its managed
process has a twenty-minute deadline, exact exit reporting and owned-process-tree
termination on timeout. Linked input/source/tool/output-parent paths are refused.

Return the **whole** output directory, including `windows-run.json`,
`windows.trx`, stdout/stderr, all queue JSON and all `.disk` outputs. Preserve
the original Linux `inputs/`, catalog and preparation evidence.

```sh
python3 scripts/writer-handoff.py verify \
  --inputs artifacts/writer-handoff-run/inputs \
  --outputs artifacts/writer-handoff-returned \
  --report artifacts/writer-handoff-native-verification.json
```

The final report records exact commands/status/tool hashes and full input/output
hashes. Commands are finite and nonrepairing; no log reset/replay/normalization
is performed. Nonzero exits remain failures, including filesystem-check warnings.

## Coverage boundaries

With no handoff environment variables, existing Linux tests still author and
check locally as before. The three `SECTORWORKS_WRITER_INPUTS`,
`SECTORWORKS_WRITER_OUTPUTS`, `SECTORWORKS_WRITER_CATALOG_SHA256` variables are
set only by the Windows runner. Linux export uses
`SECTORWORKS_WRITER_EXPORT` only during preparation.

The Windows selector covers the four existing FAT/NTFS matrix workflow methods,
not Linux-native-only fresh-cluster authoring tests or the entire solution.
Those existing tests remain required Linux coverage, not silently passed Windows
cases. Passing this pipeline proves Windows managed workflows on verified legal
Linux-authored fixtures; it does **not** establish Windows-authored-media, TxF,
LFS 2.0/cache safety, ARM64, or new writer/Phase 3 qualification.

## Separate physical-growth regression

`PortableWriterGrowthTests` reuses nine existing FAT32 dynamic-VHD/VHDX catalog
rows (all layouts and VHDX physical-sector variants). Each test adds a generated
20 MiB + 17 byte file, commits, asserts **actual physical container length grew**,
checks exact content and reset editing state, and repeats with a second file and
second real growth. Virtual length must not change. FAT32 is used because the
small ordinary-NTFS VHDX fixtures already allocate their entire virtual payload;
using them would not prove physical growth.

Run this class on Linux as well as the existing 84-row preparation. With no
handoff mode it performs independent QEMU/FAT/mtools checks after both commits.
On Windows, run `windows-writer-handoff.ps1` again with `-GrowthOnly` and a fresh
output root. This requires exactly **9 passed tests** and retains **18 pending
outputs**, including the first-commit image before the second commit mutates it.
Return the whole separate output directory and run:

```sh
python3 scripts/writer-handoff.py verify \
  --inputs artifacts/writer-handoff-run/inputs \
  --outputs artifacts/writer-growth-returned \
  --report artifacts/writer-growth-native-verification.json --growth
```

The verifier checks both output hashes, proves each retained physical length
exceeds the previous baseline, runs mandatory independent checks and compares
the full payload hashes. These nine cases supplement, not replace, the original
168-case matrix and do not broaden writer capabilities.

## Final Global-session-corrected archive

The final logger-corrected source rerun is separate from the historical snapshot
below. Its exact source archive SHA256 is
`F5CA20B3A4F790ECD821985A5DA9FB6AD23236846DAB9A3F2C7E782CE599E06C`;
its input catalog SHA256 is
`873C84E162D2B361FA610D3A1B0FBB45B02FA09F413F156DD6873D93D90E1E31`.
Linux preparation passed 84/84. Actual Windows results were 32/32 focused,
177/177 Services, 392/392 concurrent reader/security/safety, 12/12 handoff guards,
168/168 writer workflows and 9/9 two-commit growth cases, without skips.
The exact 186 resulting outputs passed all 876 required native commands with
all exit codes zero; all 84 original inputs stayed unchanged on both hosts.

The current exact 270 media files and replay evidence are compactly retained:

```text
artifacts/writer-handoff-global-final-run/qualified-media.zip
Bytes: 36939989
SHA256: BB50936B998E742931202949FE3D51BF5FFCA42B889689474558B3B0D2849C78
```

All 568 archive members were completely decompressed and hash-verified before
the 270 individually rehashed originals were removed. Their media byte total
is 26,242,378,752. The external `qualified-media-manifest.json`,
`qualified-media-full-verification.json`, `archive-storage-ledger.json`, compact
catalogs/TRX/native reports and original qualification hashes remain preserved.
Windows retains the same verified archive; only the exact manifest-listed,
hash-matching generated duplicates and temporary transfer ZIPs were removed.
The owner-assisted Native-R2 fixture and all owner media remain outside this
workflow.

Safe restore uses a fresh directory only:

```sh
python3 artifacts/writer-handoff-global-final-run/restore-qualified-media.py \
  --archive artifacts/writer-handoff-global-final-run/qualified-media.zip \
  --expected-sha256 BB50936B998E742931202949FE3D51BF5FFCA42B889689474558B3B0D2849C78 \
  --destination artifacts/writer-handoff-global-restored
```

The complete verification, exclusive creation, symlink rejection and replay
rules below apply equally to this archive. For native rechecks, substitute
`artifacts/writer-handoff-global-restored` for the historical restore root in
the commands below. This bounded pass does not close TxF/LFS/cache-safety,
Windows-authored-media, GUI/ARM64 or remaining Phase 3 gates.

## Historical archive-backed qualification snapshot

The completed qualification's **270 exact media files** (84 inputs, 168 matrix
outputs and 18 growth outputs) are retained in:

```text
artifacts/writer-handoff-final-current/qualified-media.zip
SHA256: 34A10592057CC3ECCFFEA05824063CA4CBECBE524A49682EC2D2B5B4580E6F8B
```

The archive is 36,935,231 bytes and contains 569 fully verified files, including
the media, compact replay metadata and qualification evidence. Media alone
expands to 26,242,378,752 bytes (24.440 GiB). The embedded
`qualified-media-files.json` and external
`artifacts/writer-handoff-final-current/qualified-media-manifest.json` record
every member's full SHA256 and byte length. Full decompression/hash verification
preceded removal of only the explicitly enumerated, rehashed generated originals.
Catalogs, TRX, native reports and original qualification hashes/counts remain
unchanged outside the archive as well.

`archive-storage-ledger.json` in the same directory records before/after allocated
sizes and which historical summaries now refer to archive-backed media. The
extra post-format 84-image set was **never Windows-qualified**: its redundant
images were individually hash-checked and removed, while its catalog, TRX and
hash ledger remain. The qualified archive belongs to catalog
`DDCFDD15217417041B5F7214857DC57D3659700303EC98A546F18E0848ABBE83`,
not that later independent input set.

This is evidence for the preserved source archive
`EAF98B30A25CB5829896D88D3B9BC77552DBF77582A1FCAA8978D28BF1A49828`,
also retained inside the media archive. It is not automatically qualification
of subsequent repository changes. The storage ledger records any later source
hash differences separately without altering the original results or hashes.

### Safe exact-byte restore

From the repository root, restore only into a **new** directory whose parent
already exists:

```sh
python3 artifacts/writer-handoff-final-current/restore-qualified-media.py \
  --archive artifacts/writer-handoff-final-current/qualified-media.zip \
  --expected-sha256 34A10592057CC3ECCFFEA05824063CA4CBECBE524A49682EC2D2B5B4580E6F8B \
  --destination artifacts/writer-handoff-restored
```

The helper first checks the pinned archive hash and **every complete member**,
rejects unsafe names/links, refuses an existing destination, and restores with
exclusive file creation and per-file hash checks. It never overwrites the old
generated roots or any existing user file. The helper itself is also retained
inside the archive at `evidence/restore-qualified-media.py`. Omitting
`--destination` performs full archive verification without extracting media.

The self-contained replay layout permits independent native rechecks with the
existing Linux tools, without reauthoring or changing the original evidence:

```sh
python3 scripts/writer-handoff.py verify \
  --inputs artifacts/writer-handoff-restored/qualification/inputs \
  --outputs artifacts/writer-handoff-restored/qualification/matrix \
  --report artifacts/writer-handoff-restored/matrix-recheck.json

python3 scripts/writer-handoff.py verify \
  --inputs artifacts/writer-handoff-restored/qualification/inputs \
  --outputs artifacts/writer-handoff-restored/qualification/growth \
  --report artifacts/writer-handoff-restored/growth-recheck.json --growth
```

Use new report names for subsequent rechecks. No owner-assisted Native-R2
fixture, Tower image, ISO, PMFX file, native mount, repair, log normalization or
real-disk access is involved in archiving, cleanup or restore.
