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

## Learnings

DiscUtils FAT writer success and reopened file hashes are insufficient write
validation: Phase 2 exposed a truncation-related lost cluster and incomplete
FAT32 auxiliary metadata despite correct file contents.
Use the qualified writer's allocation/metadata checks and independent
`fsck.fat -n`/mtools checks where available; do not replace these with tree/hash
checks alone.
Keep write fixtures separate from immutable read goldens, whose readable layouts
may intentionally fail stricter writer qualification.
See [Phase 2 restrictions and evidence](docs/staged-editing.md) before broadening
FAT capabilities, especially DOS 8.3 naming and legacy floppy exclusions.
