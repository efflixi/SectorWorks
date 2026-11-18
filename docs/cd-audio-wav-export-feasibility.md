# CD audio to WAV: feasibility study

**Feasible for supported BIN/CUE images, including discs that start with a data track.** SectorWorks already locates each audio track and exposes its PCM samples. WAV export would be a read-only extraction operation, not an image edit, rebuild, or recording of the player. It needs no new codec, audio device, or administrator access. This study does not implement the feature.

## Recommended workflow

Add **Export Audio Tracks…** to the disc or track view. Let the user select individual tracks or **Select All**, then choose a destination folder once. Write one WAV per selected track, using original track numbers (for example, `Track 02.wav` if track 01 is data). Show track titles in the picker. Data tracks are never selectable; show a reason if an audio track is unsupported. Export should work even on a read-only disc or a machine without working audio playback hardware.

## How it would work

- **Read the existing track stream.** [CueImageSession](../src/SectorWorks.Storage/CueImageSession.cs) already bounds a track from its file-relative `INDEX 01` to the next track’s first stored index or the end of that backing file. Use the guarded audio-track read path, not disc-relative offsets or the live player. This covers both a data track and audio sharing a 2,352-byte-sector BIN and a separate 2,048-byte-sector data file alongside audio members. The data filesystem need not be browsable as long as the CUE opens and the audio members are readable.
- **Write a standard PCM WAV header and stream the samples.** For ordinary CD audio, use 44,100 Hz, signed 16-bit stereo. A minimal [RIFF/WAVE](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-) PCM header is 44 bytes; each stored sector contributes 2,352 sample bytes (588 stereo frames). The existing 120-minute bound means at most 1,270,080,000 sample bytes, within standard RIFF chunk sizes. Copy `BINARY` audio bytes unchanged. For big-endian `MOTOROLA` audio, swap the two bytes of each 16-bit sample for standard WAV. That changes byte order only, not sample values, timing, or channel order. No re-encoding, resampling, or volume adjustment.
- **Apply existing extraction safeguards.** Reuse the [destination picker](../src/SectorWorks.UI/AvaloniaPlatformDialogs.cs) pattern and [safe output path handling](../src/SectorWorks.Platform/SafeExtractionTarget.cs). Stream in bounded chunks, check the expected length, and abort on source changes that can be detected, short reads, or write failure. Publish each WAV only after it is complete, never overwrite an existing file, and remove unfinished output on cancellation or error. If a batch stops partway through, report which files completed and which did not. Keep the CUE and every source member unchanged.

## Scope and verification

Export the same track program area used for playback: no `INDEX 00` audio, stored or synthetic gaps, or disc-level metadata. Keep pre-emphasis (`PRE`) and four-channel (`4CH`) tracks outside the first version rather than silently misrepresent them. This is not physical-disc ripping, a full-disc archival export, or a change to what images SectorWorks can open.

A format-only check on synthetic samples confirmed that an independent WAV reader decoded the expected 588 stereo frames, with identical PCM payloads for the direct copy and byte-swapped cases. No SectorWorks export was run. Before shipping, verify selected/all export against generated shared-BIN and separate-file mixed-mode discs, with distinct data, gap, and audio bytes; compare WAV samples independently, test cancellation and name collisions, and check the destination flow on Windows and Linux as a standard user.
