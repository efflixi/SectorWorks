# Phase 4 — optical editing and rebuild

The owner authorized Phase 4 on November 17, 2026. This is a local review
delivery, not a release or authorization for Phase 5.

**Phase 4 handoff: verified and ready for owner review.** No Phase 4 exit
criteria were open. The separately authorized [Phase 5](phase5-bin-cue.md)
work is tracked in its own report.

## What can be edited

| Input | Qualified behavior | Important limits |
| --- | --- | --- |
| ISO 9660 primary | Level 1 uppercase 8.3 and bounded Level 2 uppercase single-extent identifiers | Up to eight directory levels including the root; no loss-inducing name conversion or Level 3 multi-extent files |
| ISO 9660 + Joliet | Separate primary and Joliet trees; composed BMP names up to 64 Joliet code units per name | Retains unchanged primary aliases; collision-free aliases for newly edited names not representable in primary; refuses ambiguous associations |
| Rock Ridge | Bounded RRIP_1991A ordinary files/directories, UTF-8 alternate names, supported POSIX mode/ownership, timestamps and root extension continuation | No relocated directories, hard/symbolic links, devices, arbitrary continuations or unknown extensions; directory link counts follow the rebuilt tree |
| UDF-only | UDF 1.02 NSR02 DVD-ROM-style closed layout, one read-only Type-1 physical partition, 2048-byte blocks, CS0 names, ordinary short-AD files/directories | Checks descriptor copies/anchors/CRC locations, allocation and IDs; no VAT, sparing, metadata partitions, extended attributes/entries, inline data or multiple extents |
| ISO/UDF bridge | Rebuilds primary, Joliet, Rock Ridge and UDF views as present when independently parsed trees agree | Ambiguous names, visibility, trees or payload extents are refused; never silently drops a namespace |
| El Torito, bounded non-hybrid ISO | One-sector catalog, at most eight no-emulation BIOS/UEFI entries, complete catalog/boot members uniquely visible as files in the fixed region | Retains their bytes and LBAs. Neither those files nor their ancestors can be moved, deleted or replaced. Emulation, unknown catalogs and hybrid layouts stay read-only |

UDF allows one recorded extent of at most 1,073,741,823 bytes per file; empty
files are supported. ISO uses one extent with a 32-bit length. Other bounds
include 64 GiB output, 100,000 entries, 32 directory levels overall, 16 MiB per
directory and bounded aggregate metadata/opaque-sector inspection. Shared NFC
name/path, staging and history limits also apply. The prior bounded ISO reader
still handles its supported read-only inputs when rebuild is refused. Other
UDF-only layouts may be detection-only; unqualified bridges may show only ISO.

The app **does not guarantee bootability or exact layout**. The generated
self-contained BIOS and UEFI guests booted before and after rebuild; arbitrary
boot images may have additional firmware, geometry or filesystem dependencies.
Hybrid MBR/GPT/system areas, multisession, unknown extensions and untracked
nonzero data are refused, not stripped to obtain a rebuild.

## Workflow and safety

1. Open a supported optical file. **Rebuild Required** identifies an eligible
   layout; read-only images show the refusal reason.
2. Stage add, replace, delete, rename and new directory; review, undo/redo,
   browse, preview, search or extract the staged view. Review estimated size.
3. **Rebuild** and **Ctrl+Shift+S** both call validated Save As. **Commit** is
   unavailable for optical images; neither the source nor an existing destination
   may be overwritten.
4. Services checks the source baseline and staged tree/content, creates an
   exclusive same-directory temporary output, streams and flushes it. A reopen
   independently checks layout, namespace, metadata, allocation, file lengths
   and SHA-256. It rechecks the source before the non-overwriting move. Cancel
   before publication removes the temporary output; any cleanup failure names
   the residual path. Success retains the source and staged history.

Core owns separate `IImageRebuildSource` / `IImageRebuildPlan` contracts. The
existing staged disk writer and Phase 3 commit/recovery pipeline are unchanged.
Storage uses checked allocation and streams payloads without host mounts,
subprocesses or new production dependencies.

## Qualification

Linux tests use `genisoimage` (authoring), `isoinfo` (ISO/Joliet/Rock Ridge) and
`7z` (UDF). The UDF-only input is an independently authored bridge derivative:
only ISO descriptors/path tables/directories were removed and the UDF recognition
sequence moved. UDF descriptors and payloads were left intact and independently
read. No supplied owner disk/ISO or proprietary OS content is a fixture.

| Check | Result |
| --- | --- |
| Final optical/storage regression selection | 51 passed, including existing optical compatibility and container tests |
| Final complete UI suite and ordinary editing/commit selection | 76 UI and 87 Services tests passed |
| Independent file and layout checks | 49 on the initial portable workflow outputs; 17 on the exact final desktop outputs |
| Final desktop runs | Windows x64 bridge and UDF-only, and Linux x64 AppImage bridge; all confirmed source unchanged and output reopened |
| Representative firmware execution | Generated no-emulation BIOS and UEFI guests each reached the expected marker on both the authored input and its rebuild from the final Linux x64 package |
| Final runnable builds | Four self-contained Windows/Linux x64 and ARM64 packages, plus a Linux x64 AppImage; all decoded ZIP members verified |

Windows ran as a standard user throughout. No UAC prompts, native disk
attachments, elevated helpers or tunnel changes were needed. The temporary
Windows test trees were removed after verifying their retained artifacts. Final
desktop and firmware inputs, exact outputs, logs and screenshots are preserved
in a verified private archive referenced by the evidence index.

See the [evidence index](../artifacts/phase4-review/evidence-index.json) and
[local review packages](../artifacts/phase4-review/packages/manifest.json) for
exact test snapshots and runnable builds. Physical Linux and Windows ARM64
remain NOT VERIFIED — DEFERRED POST-1.0 BY OWNER.

To try a candidate, extract the ZIP matching your OS and architecture and run
`SectorWorks.UI.exe` on Windows or `./SectorWorks.UI` on Linux. Alternatively,
run the executable Linux x64 AppImage. Use a disposable image in one of the
qualified profiles above, then choose a new destination for **Rebuild to New
Image**. The package manifest includes SHA-256 checksums.

## Final verification and handoff

The [full Linux verification log](../artifacts/phase4-review/full-verification.log)
for the Phase 4 review candidate records **2,417 passed, 1 Windows-only skip,
zero failures**.
Restore/build, shell and AppRun contracts, authoritative reference copies,
formatting and UI-resource verification all passed. The earlier run ended
without a storage summary; that interrupted result is retained separately
and is not counted as a pass. No changes were made to NTFS admission, qualified
Phase 2/3 writers or the accepted deferrals.

Review the bounded namespace/metadata admission in
[Storage](../src/SectorWorks.Storage/OpticalRebuildDetection.cs), the validated
[rebuild publication](../src/SectorWorks.Services/ExplorerService.Rebuild.cs),
and the staged-editing UI. No Phase 4 exit criteria remain open. The checklist
keeps later-phase and post-1.0 work separate. Stop here for owner review.

## Format references

- [ECMA-119: ISO 9660 and Joliet](https://ecma-international.org/wp-content/uploads/ECMA-119_5th_edition_december_2024.pdf)
- [OSTA UDF 1.02, republished by Ecma](https://ecma-international.org/wp-content/uploads/ECMA_TR-112-7_1st_edition_december_2023.pdf)
- [IBM/Phoenix El Torito specification](https://pdos.csail.mit.edu/6.828/2014/readings/boot-cdrom.pdf)
- [UEFI optical media-access specification](https://uefi.org/specs/UEFI/2.11/13_Protocols_Media_Access.html)
