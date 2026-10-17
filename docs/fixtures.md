# Foundation fixtures and direct-library experiments

These are synthetic, legally generated test media, not operating-system media.
They contain the text `SectorWorks foundation sentinel v1\n` in `SENTINEL.TXT`.
They are **not bootable** and establish no SectorWorks application support.
No source image is mounted. All writes target generated or disposable images.

## ISO reader compatibility regressions

`IsoCompatibilityTests` authors disposable media with the unchanged DiscUtils
`CDBuilder`; no owner recovery-disc content is included. Builder paths use
backslashes even on Linux. Separate ISO9660/Joliet trees exercise empty markers
and unrelated empty filenames with unused out-of-range addresses, ordinary text,
directories, hidden attributes, timezone offsets and mixed-case Unicode.
Malformed derivatives cover nonempty/out-of-range extents, empty directories,
endian/identifier mismatches, multi-extent/interleaved/extended allocation,
duplicate/unsafe names, invalid Joliet encoding and invalid dates.

Two nested 1600-file directories per naming variant reproduce repeated-parent
metadata I/O. Counting-device assertions require at most 4 MiB and 4096 reads
per listing, at most 1 MiB per request, without changing production budgets.
Selected metadata/content is compared with the signed reader. Direct synthetic
directory records test the 16 MiB and 131072-entry caps without a large image.
Empty streams exercise sync/async/EOF/seek/cancellation, and shared Services
exercise preview/hex/search/extraction with staging denied. Source hashes are
checked. All **20 new cases** pass in the **102-case selected reader run**;
see [exact verification and owner-image results](platform-status.md#iso-reader-compatibility-refinement-2026-10-15).

The local owner Toshiba ISO is neither a fixture nor a release/package input.
It must remain unmodified, untracked and undistributed; its reader verification
streams data in place without copying the image or persisting its contents.

## Phase 2C NTFS reader fixtures and deferred authentic-media qualification

Owner authorization **2026-10-15** adds Phase 2C before Phase 3 for separate
**NTFS 1.2 (NT4), 3.0 (Windows 2000) and 3.1 (XP+)** read-only qualification.
The owner subsequently moved authentic historical-media qualification and
dependent **2C.3/2C.5/2C.V2/2C.V3/2C.V6/2C.E1/2C.E5/2C.E6** checks to the
[checklist's post-1.0 milestone](../checklist.txt), **P1.NTFS.1-P1.NTFS.6**.
These are incomplete follow-ups, not 1.0 blockers. A disposable disk formatted
and populated by the historical OS in a VM is sufficient; vintage hardware is
not required. Current automated/safety/source-integrity/general-platform and
writer checks remain in 1.0; macOS is excluded from general-platform checks
as a standing unsupported, build-only target (2026-10-16).
Completed structural version and attribute-list fixture work
below qualifies the bounded synthetic subset, not authentic historical media.
No authentic NT4-authored fixture exists yet. Synthetic version-field
mutations can test parser branching but are not authentic legacy qualification.
Record authoring provenance/version/layout separately from a synthetic claim.

Legal disposable synthetic fixtures cover resident/nonresident attribute lists,
base/extension identity and sequence references, directory index continuations,
MFT-bootstrap boundary cases, ordinary content and Unicode metadata.
Rejected bootstrap-list layouts have explicit negative tests and exact blockers;
their presence does not imply bootstrap-list support. Keep observed version
metadata and structural synthetic evidence separate from authentic legacy
authoring qualification.
Malformed companions cover truncated lists/runs, cycles, stale/mismatched
references, duplicate/overlapping/missing VCN extents, overflow and bounds.
Each advertised bounded structural subset still needs independent read comparisons
and shared service workflow/source-hash evidence. Authentic-media repetitions
are deferred under **P1.NTFS.3-P1.NTFS.4**. Keep immutable goldens unchanged and separate
reader fixtures from any subsequently qualified disposable writer fixtures.

The owner's approximately 8.42 GB raw Tower NTFS 3.0 image remains **outside
the repository and must not be copied, modified or distributed**, including
into ignored artifacts, fixtures or packages. Initial diagnosis: live directory
attribute list at MFT 26 references indexing extension 8336; an independent
native index warning is now diagnosed as a whole-attribute native-tool size
limit (**2C.6/2C.V5**), not corruption or health certification. Initial in-place
enumeration succeeds; subsequent bounded Phase 2D source-hash/selected-extraction
evidence is recorded below, not whole-volume qualification. Do not
promote owner content into synthetic fixtures or infer successful qualification.

Newly readable legacy/attribute-list variants remain noneditable until Phase 3
**3.0.1-3.0.5** qualifies staged editing, validated Save As and new-output NTFS
writing before **3.7** direct commit. Existing Phase 2B fixtures/results and
required actual Windows-authored/security-reference/LFS 2.0 gaps under **2B.6**
remain unchanged; reader fixtures cannot close writer gates.

### Verified structural version fixtures

`NtfsCompatibilityFixtures` creates runtime synthetic
derivatives of the lawful 32 MiB `mkntfs`/`ntfscp` golden in a disposable
`WorkDirectory`; the immutable compressed golden remains unchanged.
The existing-volume managed backend authors `Legacy`, resident `NOTE.TXT`,
16 KiB nonresident `PAYLOAD.BIN` and resident `Unicode-λ.txt` before structural
reconstruction. The Unicode file has the independently specified last-write
timestamp `2000-02-29T12:34:56Z`:

| Synthetic target | Structural changes under test |
| --- | --- |
| NTFS 1.2 | All standard-information records truncated to 48 bytes; valid per-file self-relative security descriptors without shared Security IDs; pre-XP MFT USA at 42/attributes at 48, no record-number field |
| NTFS 3.0 | Lawful mixture of 48/72-byte standard information/shared security metadata retained; ordinary added files have 72-byte records; pre-XP MFT USA at 42/attributes at 48, no record-number field |
| NTFS 3.1 | Modern MFT USA at 48/attributes at 56; lawful baseline mixture includes ten 48-byte standard-information records |

The NTFS 1.2 descriptors contain separate owner/group `S-1-5-18` SID copies and
an empty DACL. MFT USA protection and the mirror are regenerated. Expected observed metadata
is `NTFS version` = `1.2`, `3.0` or `3.1`. These are more than version-byte
mutations, but **are not genuine NT4-, Windows 2000- or XP-authored fixtures**:
baseline modern hidden metadata remains. They establish neither a full
historical-format image nor native-OS interoperability.

The completed fixture work now passes targeted **`NtfsCompatibilityVersionTests`:
14 passed** against the updated reader: public browse, resident/
nonresident content hashes and read-only/writer gates for 1.2/3.0/3.1, serialized
layout assertions and rejection of five unsupported versions. Routine tests
derive fixtures from the golden without invoking native authoring tools.
Shared-service cases cover browse/preview/hex/search/extraction, staging and
Save As denial, plus unchanged source hashes for all three versions.
Scoped formatting passes; immutable goldens remain unchanged. These tests
**exclude attribute lists**, whose separate reader fixtures/results must be
recorded independently.
This is structural synthetic evidence, not authentic NT4/2000/XP authoring or
runtime qualification. The latest focused selection is
**49/49 (35 attribute-list + 14 version/layout/service)**, adding named-list
guard, GPT mixed-volume isolation, modern/legacy extension identity and empty
nonresident index-bitmap cases.
The latter proves qualified FAT staging/Save As retains the unqualified NTFS
partition byte hash and never grants it editing capabilities.
See [canonical verification chronology/logs](platform-status.md#phase-2c-authorization-and-initial-reader-evidence-2026-10-15)
for the earlier full run and final focused coverage.

Executed selector: `dotnet test --configuration Release --no-restore --filter
FullyQualifiedName~NtfsCompatibilityVersionTests` in the storage-test project.
Scoped `dotnet format --verify-no-changes` passes for both new files.

The installed `mkntfs` offers no version-selection option. The isolated managed
formatter is excluded/throws and is not an established portable fixture-authoring
solution. No proprietary media or downloaded historical OS image is used.
Authentic legacy provenance remains incomplete under post-1.0
**P1.NTFS.1-P1.NTFS.2**, not a current **2C.5/2C.E1/2C.E6** release blocker.
Historical writer-media interoperability is separately tracked in **P1.NTFS.5**;
required disposable writer safety tests and **2B.6** Windows-media checks remain.

### Verified bounded attribute-list fixtures

`NtfsAttributeListTests` now passes **35/35**, including named-list guard,
GPT mixed-volume isolation, modern/legacy extension identity and empty
nonresident index-bitmap tests, using legal disposable fixtures,
not Tower or modified immutable goldens. Cases include a moved directory index,
resident/nonresident attribute lists, split ordinary data, 15 malformed cases,
unsupported flags, explicit 1 MiB list-cap rejection and explicitly unsupported
MFT bootstrap lists. Resident `INDEX_ROOT` flags for default directory compression,
zero-length nonresident index streams and cancellation are covered; compressed
file contents remain unsupported. List/reference/VCN and bitmap-driven active
index checks are structural validation, not filesystem repair or certification.
The extension identity guard checks header offset 44 only when USA offset is
at least 48; legacy offset 42 is not misread as a record number. Empty nonresident
index bitmaps do not trigger a spurious empty-run read failure.
See [reader resource bounds](storage-phase1.md#resource-cancellation-and-concurrency-policy).

### Phase 2D reproducible bounded reader rows, 2026-10-17

`NtfsReaderQualificationTests` adds **17 cases** to the retained 49-case reader
selection: **66 passed, zero failed/skipped** on Linux x64. These are synthetic
structural qualification, not NT4/Windows 2000/XP-authored interoperability.
There are **nine shared-workflow rows**, not an untested version/feature cross-product:

| Rows | Serialized layout and independently checked expectations |
| --- | --- |
| NTFS 1.2 ordinary | 25 active pre-XP records; all 25 SI values are 48 bytes and all 25 have per-file descriptors; Unicode file has no shared Security ID |
| NTFS 3.0 ordinary | 25 active pre-XP records; ten 48-byte and fifteen 72-byte SI values; ten baseline per-file descriptors coexist with modern shared metadata; Unicode file has a 72-byte SI and Security ID 259 |
| NTFS 3.1 ordinary | Same declared SI/security mixture as 3.0, with modern USA/header layout, no pre-XP headers |
| NTFS 3.1 attribute-list: six rows | Resident/nonresident lists independently combined with a moved ordinary DATA stream, moved directory index, or split DATA stream |

The first three rows exercise resident/nonresident content, Unicode spelling and
the exact UTC timestamp through the production reader. Raw serialized SI fields
are inspected afresh rather than trusting authoring counters. The 1.2 descriptor
is parsed and checked as self-relative control `0x8004`, separate owner/group
`S-1-5-18` SID copies, present non-null empty DACL and no SACL. Its exact stored
52-byte SHA-256 is
`4597B39D4646C2E2120FDDFED1F7A92A90CB223B96D0572AA69FD7C0B2FE2D56`.
This is bounded on-disk interpretation, not an access-check/permissions editor,
authentic NT4 authoring or complete Windows security interoperability proof.
Attribute-list fixtures are **3.1 only**; do not infer that all combinations
of 1.2/3.0 and attribute-list/security layouts have been qualified.

All nine rows run shared service directory browsing, text preview, first/paged
hex byte checks, filename search and persisted extraction. Every extracted byte
is compared with independently declared generated content; complete SHA-256
also agrees with native `ntfscat` **2022.10.3** on this Linux host. Native comparison
is optional when the external tool is absent, explicitly represented by a null
native hash in per-test evidence; it is not a runtime dependency. The complete
source hash is unchanged after workflows and cancellation/rejection checks.
Legacy/attribute-list staging, Save As and direct commit remain denied;
ordinary 3.1 retains its already-qualified writer/commit capabilities.

Stable generated content vectors:

| Content | Bytes | SHA-256 |
| --- | ---: | --- |
| `NOTE.TXT` UTF-8 literal | 36 | `024526B839EAF510847DE63C2E6FDF7C54D2B24E36F2E3C084F7A5D1F387C7CB` |
| `PAYLOAD.BIN`, byte `i % 251` | 16384 | `4348E3B98E8A327B34CED39C1DA9E67CDB4CD5E48E4D7960607A3AE403D35F0C` |
| `Unicode-λ.txt` UTF-8 literal | 49 | `5A0FD02AA0A76181092ECE007BB35A8A8861099DF585957B77DC66B7907EE866` |

The legal 32 MiB immutable NTFS seed has expanded SHA-256
`32387eca91198698908cfb2852aa2355cc5212a910cde0c9993aeb99ddb0fdfc`
and compressed SHA-256
`e36abcc2e1dcae575b4505f1e08f8cd74ca19a48c4e38ab4203fed460a95ab84`.
Generated derivatives are not byte-deterministic because authoring retains
ordinary creation times. Each workflow row emits its **actual full source hash,
feature parameters, observed metadata and independent content hashes** into TRX
output; do not regenerate fixed hashes to conceal changes. Local evidence:
`artifacts/phase2d-reader.log`, `artifacts/phase2d-reader-tests/phase2d-reader.trx`
and extracted metadata/hash summary `artifacts/phase2d-reader-evidence.json`.
No owner content is part of these fixtures or records.

Six additional instrumented preflight cases assert at most 64 KiB/request,
2 MiB total metadata I/O and 2048 reads on these small fixtures, without raising
production limits. Two cases cancel **during** metadata reads, require
`OperationCanceledException` rather than corruption, release the source handle
and preserve its hash. Each workflow also cancels browse/preview/hex/search/
extraction, leaves no partial extraction and verifies subsequent browsing works.
Existing malformed/reference/VCN/list-cap/bootstrap/index tests now explicitly
check unchanged source hashes on their error/cancellation paths. Existing
mixed-volume capability-isolation coverage remains selected. MFT bootstrap lists,
unqualified sparse/compressed/encrypted/link/reparse/ADS content and legacy/
attribute-list writing remain unsupported.

Repeat the focused selection from the repository root:

```sh
source scripts/common.sh
"$DOTNET" test tests/SectorWorks.StorageTests/SectorWorks.StorageTests.csproj \
  --configuration Release --no-restore \
  --filter 'FullyQualifiedName~NtfsReaderQualificationTests|FullyQualifiedName~NtfsCompatibilityVersionTests|FullyQualifiedName~NtfsAttributeListTests' \
  --logger 'trx;LogFileName=phase2d-reader.trx' \
  --results-directory artifacts/phase2d-reader-tests
```

### Local-only owner-media probe

`tools/SectorWorks.ReaderProbe` is a Linux-only diagnostic executable, not a
fixture generator, product dependency or package input. It hashes the external
source completely **before** opening production readers and **after** closing
all sessions and deleting selected extraction. Linux stat device/inode/size/
mtime/ctime fingerprints must also remain identical. The source is never copied,
mounted, changed or transferred. The caller must provide an existing empty
owner-only directory outside the repository/Dropbox on private local storage;
the tool rejects symlinked output paths and removes its owned child directory.
It never writes content logs: text/hex stay in memory, service logging is
disabled, and selected source filenames are replaced by aliases in JSON.

With the owner's explicit 2026-10-17 permission, three complete runs matched
full before/after source SHA-256 and identity. Final safe hash/metadata-only JSON
is local at `artifacts/phase2d-tower-final-result.json`; the private source path
is deliberately not documented. The final run streamed/extracted a **45-byte
resident text**, **491792-byte nonresident DLL** and **743184-byte nonresident
DLL**, **1235021 bytes total**, below the **67108864-byte** approved output cap.
Native `ntfsinfo` independently confirms residence. Production streams, native
`ntfscat` and persisted extraction hashes match for all three; selected content
and the private output directory were removed. The known 191-byte `boot.ini`
stream is separately hashed in memory and matches native bytes; retained
hidden/system enumeration filters are unchanged.

Counts are now labeled precisely: **904 filtered child directories / 905
including root, 16199 files**, zero enumeration failures; **10 filtered root
entries versus native 24**, and **1844 filtered system32 entries versus native
1851**. Earlier root count 24 was the native view, not a promise that the
unchanged signed-reader filters expose every entry. The strict historical
preflight still rejects attribute lists/extensions; current production reading
succeeds. Current writer qualification separately rejects the BPB hidden-sector/
absolute-offset mismatch. Neither is silently repaired. The documented initial
MFT-26/extension-8336 diagnosis and native **65536-byte readall limit versus
344064-byte index** remain distinct; successful selected reads do not certify
whole-volume health, safe writing or authentic historical authoring.

To repeat only after equivalent explicit owner approval:

```sh
source scripts/common.sh
"$DOTNET" restore tools/SectorWorks.ReaderProbe --locked-mode
"$DOTNET" build tools/SectorWorks.ReaderProbe --configuration Release --no-restore
"$DOTNET" tools/SectorWorks.ReaderProbe/bin/Release/net10.0/SectorWorks.ReaderProbe.dll \
  "$APPROVED_EXTERNAL_SOURCE" "$EXISTING_PRIVATE_LOCAL_OUTPUT" \
  > artifacts/owner-reader-result.json
```

The source is not discovered or hardcoded by the tool. Native `ntfscat`,
`ntfsls`, `ntfsinfo` and `stat` must be available at the declared Linux paths.
The selected workflow has an eight-minute cancellation deadline; native
processes are terminated by their specific process handle on cancellation.
This diagnostic does not broaden product support, filters or writer eligibility.
Remaining full-repository, unavailable-platform and owner-review gates are
tracked separately by the parent Phase 2D ledger.

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
