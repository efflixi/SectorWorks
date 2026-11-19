# CD audio metadata lookup feasibility

**Feasible with MusicBrainz.** An optional **Look up track names…** command is
small enough for a focused update. Estimate **2–4 engineering days** for the
basic workflow and targeted Windows and Linux checks, or **3–6 days total**
to qualify data-first game discs against known reference layouts. These
estimates assume UI labels for the BIN/CUE layouts SectorWorks already opens.

## How it would work

1. The user chooses **Look up track names…** in Disc Layout. SectorWorks
   derives a MusicBrainz Disc ID from the sector-level table of contents.
   The current disc model has original track numbers, modes, program starts,
   lengths, indexes and gaps. The request carries the Disc ID and, when needed
   for approximate matching, track offsets. Audio and image files stay local.
   [SectorWorks disc model](../src/SectorWorks.Core/OpticalDiscContracts.cs),
   [MusicBrainz Disc ID calculation](https://musicbrainz.org/doc/Disc_ID_Calculation)
2. Show the album, artist, disc number and track list before applying a
   match. Where several releases share the same layout, let the user choose
   the edition. If an exact lookup yields nothing, MusicBrainz can search by
   similar offsets. Present those results as candidates that require
   confirmation. [MusicBrainz Disc ID lookup](https://musicbrainz.org/doc/MusicBrainz_API#discid)
3. Fill missing titles and performers in the track list and audio pane from
   the selected release, giving existing CUE labels precedence. Keep online
   labels in the workspace. The original track numbers still identify playback
   and WAV export, and the existing WAV filename pattern is `Track NN.wav`.
   [Track display](../src/SectorWorks.UI/ViewModels/WorkspaceViewModel.Audio.cs),
   [WAV export](../src/SectorWorks.Services/ExplorerService.AudioExport.cs)

## Data-first discs and coverage

The main work is reconstructing the lookup offsets and lead-out from the
image accurately. For a single-session disc with a data track first, the
MusicBrainz reference implementation includes that data track in the ID.
SectorWorks would apply labels to the audio tracks by their original
positions. For example, if the game data is Track 01, the first song belongs
to Track 02. Check the chosen medium's track ordering when the service exposes
data tracks separately. The CD-Extra case with audio first and a later data session has
separate lead-out rules. SectorWorks currently models one supported CUE session.
[MusicBrainz reference TOC code](https://github.com/metabrainz/libdiscid/blob/master/src/toc.c),
[Disc ID examples](https://musicbrainz.org/doc/Disc_ID_Calculation),
[Picard's handling of data tracks](https://github.com/metabrainz/picard/blob/master/picard/album.py),
[SectorWorks CUE session model](../src/SectorWorks.Storage/CueImageSession.cs)

Disc IDs depend on the pressing and recorded layout. Rips with altered gaps
or trimmed audio can miss an exact match. A soundtrack album entry may have
a different track list from the game disc.
When there is no credible match, keep the numbered labels or existing CUE
titles. Coverage on the owner's collection remains unmeasured.
[MusicBrainz Disc ID coverage notes](https://musicbrainz.org/doc/Disc_ID_Calculation#Remarks)

## Free service options

- **MusicBrainz** is the first choice. Its public lookup API is free for
  non-commercial use and requires no API key for public reads. Send an
  identifying application User-Agent, limit requests to one per second,
  and reuse results within a session. Its core metadata is CC0. The hosted
  API has its own terms for commercial use.
  [API and usage terms](https://musicbrainz.org/doc/MusicBrainz_API),
  [Database license](https://musicbrainz.org/doc/MusicBrainz_Database#License)
- **AcoustID** is a possible later fallback for unmatched individual songs.
  It is free for non-commercial use with application registration. It sends a
  locally computed fingerprint and track duration. Fingerprinting adds
  audio processing and dependency work across the four supported targets.
  A recording match can identify a song that appears on several editions.
  [AcoustID API](https://acoustid.org/webservice)

## Verification

A scratch calculation reproduced the published MusicBrainz Disc ID for its
six-track audio example. A separate exact lookup of a public reference ID
returned five release candidates with track titles and artist credits.
Before implementation ships, use known audio-only and data-first layouts
to check the IDs and track positions. Mock service responses for ambiguous
matches, unknown discs, cancellation, timeout and rate limiting.
[Published calculation example](https://musicbrainz.org/doc/Disc_ID_Calculation),
[Public reference lookup](https://musicbrainz.org/doc/MusicBrainz_API#discid)
