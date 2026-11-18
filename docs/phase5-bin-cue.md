# Phase 5 — BIN/CUE and optical expansion

The owner authorized this phase after the roadmap reorder. Phase 6 is not
authorized. Tests use generated media only. No privileged Windows actions,
device mounts, audio driver installs, or source replacements are required.

**Phase 5 is verified and ready for owner review.** No Phase 6 work has started.

This records the original Phase 5 qualification. For subsequently qualified
raw data rebuilds and CUE syntax, see the [current support matrix](support-matrix.md#phase-5-bincue-capabilities)
and [compatibility audit](../Existing_File_Compatibility.md).

## Qualified formats

| Feature | Supported envelope |
| --- | --- |
| CUE | One session, UTF-8 (optional BOM), up to 99 consecutively numbered tracks, relative quoted or unquoted file names, `INDEX 00/01` and successive later indexes, `PREGAP` and `POSTGAP`. Disc and track title/performer are exposed; supported auxiliary metadata is retained in the original CUE text. |
| Track storage | `BINARY` sector files; `MOTOROLA` for big-endian PCM audio. A single backing file can contain multiple tracks of the same sector width. Frame addresses are relative to the represented program, not a physical lead-in/TOC. |
| Data read | `MODE1/2048`, `MODE1/2352`, and `MODE2/2352` Form 1. Raw sector reads check the framing and form before exposing 2048-byte user data. The existing ISO 9660/Joliet/Rock Ridge and bounded UDF readers inspect the resulting data extent. No EDC/ECC or whole-disc health claim. |
| CD-DA | 44,100 Hz, signed 16-bit stereo PCM. Play, pause, stop, previous/next, seek and software volume. Playback starts at INDEX 01 and excludes stored or virtual gaps. Select a track and **Hex View** to inspect its stored program bytes without changing the source. The disc-level hex viewer shows the CUE text bytes. |
| Rebuild | Independently qualified non-boot ISO data in a dedicated `MODE1/2048` file with only `INDEX 01 00:00:00`. Includes single-track data, separate-file multi-data, and mixed data/audio sets. The staged ISO must fit the existing sector count; writes change data contents, not the disc layout. Other tracks can be retained unchanged. No in-place commit. |

Compressed audio, WAVE/AIFF companions, variable sector widths inside one file,
true multisession structures, Mode 2 Form 2 filesystem reads and subchannel
decoding are not supported. PRE and 4CH audio flags prevent playback rather than
misrendering that track. Unknown directives are rejected; opaque REM metadata
or detected disc metadata sidecars disable rebuilding. Raw-sector, bootable
and UDF tracks cannot themselves be rebuilt. An untouched companion is copied
byte-for-byte where the complete CUE layout remains qualified.

The parser caps a catalog at 256 KiB, individual lines at 4 Ki characters and
the represented disc at 120 minutes. Companion paths are bounded, anchored to
the CUE directory and cannot reference links, devices, FIFOs, drive or UNC
paths, or parent traversal. Linux opens relative to held directory descriptors
and uses a regular-file check before reading; Windows validates path component
and file handles without following reparse points. Linux directory/no-follow
flags follow the corresponding
[x64](https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git/tree/include/uapi/asm-generic/fcntl.h)
and [ARM64](https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git/tree/arch/arm64/include/uapi/asm/fcntl.h)
kernel ABIs for both CUE input and shared extraction. The ARM64 build is not
physical runtime evidence.

## Editing and publishing

Open the `.cue`, not a companion `.bin`. Select a data track to browse its files
and stage changes. Select an audio track to open playback; **Track Information**
shows the session tracks and their geometry. **Rebuild** or **Ctrl+Shift+S** asks
for a new CUE name. A selection of `Example.cue` publishes
`Example.cue-set/Example.cue` and its companions together. Keep that folder
together when moving the result.

Services reuse the shared staging/history and ISO reconstruction. The source
and all companion file identities are checked by SHA-256. Rebuild keeps the
CUE itself verbatim, preserves all track lengths and copies audio and untouched
companions exactly. A fresh parse of the complete output and independent
staged tree/SHA-256 comparisons precede publication. A hidden sibling work
folder allows one no-replace directory rename: no individual member is
published early. Cancellation/refusals remove scoped staging; crash leftovers,
if any, remain unpublished under a hidden task name. This is not a power-loss
or source-set recovery journal. Existing disk commit and standalone optical
rebuild safeguards remain separate.

## Playback backends

The application has no new managed package dependency and does not ship an
audio engine. Windows uses the system waveform output API; Linux uses the
system `libasound.so.2` ALSA default output (which may route through PipeWire
or other desktop audio infrastructure). Playback streams bounded PCM buffers
directly from the selected program area and does not create WAV files. The
backend opens lazily; unavailable host audio is reported in the player, without
disabling data browsing. Playback stops on workspace close or switch. No host
mixer setting is changed.

Implementation references: [Windows waveform audio API](https://learn.microsoft.com/windows/win32/api/mmeapi/),
[ALSA PCM API](https://www.alsa-project.org/alsa-doc/alsa-lib/pcm_8h.html),
and [CUE FILE types and byte order](https://github.com/libyal/libodraw/blob/main/documentation/CUE%20sheet%20format.asciidoc).

## Verification

| Check | Result |
| --- | --- |
| Parser, track mapping and rebuild | 72 final focused storage/optical tests passed, including the CUE path/offset guards, direct PCM stream, raw data modes, all five staged operations, three rebuild layouts, source mutation, cancellation and destination collision |
| Application UI | 77 final UI suite tests passed. Native desktop checks also covered select/play/pause/seek/next/stop, active-track highlighting, raw-track hex, Linux keyboard playback and end-of-track |
| Data readers | Exact final application libraries browsed and extracted previously generated UDF-only and bridge images wrapped in CUE; selected bytes matched independent 7-Zip output |
| Final Linux x64 AppImage | GUI published a mixed-mode set; independently verified directory and unchanged data/audio. Loopback captured the generated 997 Hz output and playback reached end-of-track |
| Final Windows x64 | Standard-user GUI published a mixed-mode set; independently verified directory and unchanged data/audio. The R3 endpoint meter measured the expected 0.04947 normalized peak (22/80 samples with signal) without changing the hardware mute setting |
| Packaging | Verified self-contained ZIPs for all four supported RIDs, plus the Linux x64 AppImage |

The earlier Linux run captured both generated test frequencies while testing
pause/resume and track switching. A simultaneous R2 Windows meter attempt
returned silence and is retained; a fresh meter process signaled readiness
before successful R2 and R3 measurements. The endpoint
[meter is before endpoint attenuation](https://learn.microsoft.com/windows/win32/api/endpointvolume/nn-endpointvolume-iaudiometerinformation),
so this measures emitted audio data, not speaker audibility while muted.
All Windows work used standard-user access: zero UAC prompts. Windows task roots
were removed after hash checks; normal preferences, layout and the manual
tunnel were unchanged.

See the [evidence index](../artifacts/phase5-review/evidence-index.json) and
[review packages](../artifacts/phase5-review/packages/manifest.json). To try an
exact candidate, extract the matching ZIP and start `SectorWorks.UI.exe` on
Windows or `./SectorWorks.UI` on Linux, or run the Linux x64 AppImage.
Physical ARM64 remains NOT VERIFIED — DEFERRED POST-1.0 BY OWNER.
Four-target cross-publishing is not physical testing.

## Final regression and handoff

The [full Linux verification log](../artifacts/phase5-review/full-verification.log)
for the final feature build records **2,439 tests passed, 1
Windows-only skip, zero failures**. Restore/build, shell/AppRun, authoritative
reference copies, formatting and UI-resource checks passed. The initial
regression was stopped after the final raw-track hex/UI refinements, then
restarted on the complete feature build; the partial run is not counted.
R3 included two narrow corrections after the full run began: braces around
one UI branch and the Linux ARM64 no-follow/directory flags for CUE input
and shared extraction.
R3 passes build/format, 16 focused UI, 72 focused optical/storage and
22 platform tests. The UI method bodies and signatures match
R2. R3 was cross-published for all four RIDs and separately tested on the
two physical x64 hosts; the full suite was not repeated for these final changes.

The review bundle retains the exact app package identities, generated input
and output bytes, decoded evidence hashes, and independent meter, loopback
and file extraction results. No test fixtures, source qualification gates,
or archived Phase 4 evidence were replaced. The previously deferred ARM64
and modern NTFS rows are unchanged. Review the parser/anchored paths in
[Storage](../src/SectorWorks.Storage/CueImageSession.cs), the
[publication pipeline](../src/SectorWorks.Services/ExplorerService.Cue.cs)
and [audio backends](../src/SectorWorks.Platform/CdAudioOutput.cs).
No new owner action is needed to finish this milestone; stop for review before
Phase 6.
