using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;

namespace SectorWorks.UI.ViewModels;

public sealed record CdLayoutRow(string Track, string Region, int ImageLba, int SectorCount, string FileName, string FileTime);

public sealed partial class WorkspaceViewModel
{
    private const int AudioPlayerTab = 5, TrackInformationTab = 6, DiscLayoutTab = 7;
    private readonly Dictionary<int, StructureNode> trackNodes = [];
    private CancellationTokenSource? playback;
    private Task playbackTask = Task.CompletedTask;
    private long audioFrame;
    private double playbackGain = 0.6;
    private bool updatingSeek;
    private bool seekEdited;

    public IReadOnlyList<CdTrack> CdTracks { get; private set; } = [];
    public IReadOnlyList<CdLayoutRow> CdLayout { get; private set; } = [];
    public bool HasCdTracks => CdTracks.Count != 0;
    public string ImageMapTabLabel => HasCdTracks ? "Disc Layout" : "Image Map";
    public string? ImageMapTabTip => HasCdTracks ? null : "Graphical image maps are not available.";
    [ObservableProperty] private CdTrack? selectedCdTrack;
    [ObservableProperty] private bool isAudioPlaying;
    [ObservableProperty] private double seekSeconds;
    [ObservableProperty] private double volumeLevel = 0.6;
    [ObservableProperty] private string audioStatus = "Select an audio track.";
    [ObservableProperty] private string audioElapsed = "0:00";

    public double AudioDuration => SelectedCdTrack?.Duration.TotalSeconds ?? 0;
    public string AudioTrackTitle => SelectedCdTrack is { } track ? TrackTitle(track) : "No track selected";
    public string AudioFormat => SelectedCdTrack is { IsAudio: true } track
        ? $"{track.Performer}{(track.Performer.Length == 0 ? "" : " · ")}44.1 kHz · 16-bit stereo · {Clock(track.Duration.TotalSeconds)}"
        : "";
    public bool CanPlayCd => !disposed && !IsAudioPlaying && explorer is ICdTrackSource && SelectedCdTrack?.Playback.Available == true;
    public bool CanPauseCd => IsAudioPlaying;
    public bool CanStopCd => IsAudioPlaying || audioFrame != 0;
    public bool CanSeekCd => explorer is ICdTrackSource && SelectedCdTrack?.Playback.Available == true;
    public bool CanPreviousCd => AdjacentAudio(-1) is not null;
    public bool CanNextCd => AdjacentAudio(1) is not null;
    public bool CanExportCdAudio => !disposed && !IsBusy && explorer is ICdAudioExportService && dialogs is ICdAudioExportDialogs
        && CdTracks.Any(track => track.IsAudio && track.Playback.Available);

    [RelayCommand(CanExecute = nameof(CanExportCdAudio))]
    private async Task ExportCdAudioAsync(CdTrack? track)
    {
        if (!CanExportCdAudio || explorer is not ICdAudioExportService export || dialogs is not ICdAudioExportDialogs picker)
        { return; }

        await RunOperationAsync(async token =>
        {
            IReadOnlyList<int>? selected = await picker.PickAudioTracksAsync(
                CdTracks.Where(item => item.IsAudio).ToArray(), (track ?? SelectedCdTrack)?.Number, token);
            if (selected is null || selected.Count == 0) { Status = "Audio export cancelled."; return; }
            string? destination = await picker.PickAudioExportFolderAsync(token);
            if (destination is null) { Status = "Audio export cancelled."; return; }

            Status = $"Exporting {selected.Count:N0} audio track(s)…";
            bool finished = false;
            var progress = new Progress<OperationProgress>(p =>
            {
                if (!disposed && !finished && !token.IsCancellationRequested)
                { Status = $"Exporting {p.Path} — {p.FilesCompleted:N0} of {selected.Count:N0} WAV files saved"; }
            });
            CdAudioExportResult result = await export.ExportCdAudioAsync(Info.Id, selected, destination, progress, token);
            finished = true;
            if (disposed) { return; }

            string outcome = result.Cancelled ? "Audio export cancelled" : result.Failures.Count == 0
                ? "Audio export complete" : "Audio export finished with errors";
            Status = $"{outcome} — {result.Completed.Count:N0} of {selected.Count:N0} WAV files saved to {destination}.";
            if (result.Cancelled || result.Failures.Count > 0)
            {
                var details = new List<string>();
                if (result.Completed.Count > 0) { details.Add($"Saved: {string.Join(", ", result.Completed)}"); }
                details.AddRange(result.Failures.Select(f => $"{f.Path}: {f.Error}"));
                if (result.Cancelled)
                {
                    var notAttempted = selected.Order().Select(number => $"Track {number:D2}.wav")
                        .Except(result.Completed).Except(result.Failures.Select(f => f.Path));
                    details.Add($"Not exported: {string.Join(", ", notAttempted)}");
                }
                Error = string.Join("\n", details);
            }
        });
    }

    private void AddDiscNodes(StructureNode root, CdDisc disc)
    {
        CdTracks = disc.Sessions.SelectMany(session => session.Tracks).ToArray();
        CdLayout = CdTracks.SelectMany(DescribeTrackLayout).ToArray();
        foreach (CdSession session in disc.Sessions)
        {
            var branch = new StructureNode($"Session {session.Number}", "Optical") { IsExpanded = true };
            root.Children.Add(branch);
            foreach (CdTrack track in session.Tracks)
            {
                VolumeInfo? volume = Info.Volumes.FirstOrDefault(v => v.Id == track.VolumeId);
                StructureNode node = volume is null
                    ? new StructureNode(TrackTitle(track), track.IsAudio ? "Play" : "Optical") { Track = track }
                    : DirectoryNode(volume, "/", track);
                branch.Children.Add(node);
                trackNodes.Add(track.Number, node);
            }
        }
    }

    [RelayCommand]
    private void ShowDiscLayout()
    {
        ToolVisible = true;
        ToolIndex = DiscLayoutTab;
    }

    private static IEnumerable<CdLayoutRow> DescribeTrackLayout(CdTrack track)
    {
        string label = $"{track.Number:D2} · {(track.IsAudio ? "CD audio" : track.ModeName)}";
        int programStart = track.Indexes.Single(index => index.Number == 1).FileSector;
        int programEnd = track.StartLba + track.SectorCount;
        if (track.Pregap > 0)
        {
            yield return new(label, "PREGAP · Not stored", track.StartLba - track.Pregap, track.Pregap, "—", "—");
        }

        for (int i = 0; i < track.Indexes.Count; i++)
        {
            CdTrackIndex index = track.Indexes[i];
            int startLba = track.StartLba + index.FileSector - programStart;
            int count = i + 1 < track.Indexes.Count ? track.Indexes[i + 1].FileSector - index.FileSector : programEnd - startLba;
            string section = index.Number switch
            {
                0 => "Stored pregap",
                1 => "Program start",
                _ => "Program subdivision",
            };
            yield return new(label, $"INDEX {index.Number:D2} · {section}", startLba, count,
                track.SourceName, $"{index.FileSector / 4500:D2}:{index.FileSector / 75 % 60:D2}:{index.FileSector % 75:D2}");
        }

        if (track.Postgap > 0)
        {
            yield return new(label, "POSTGAP · Not stored", programEnd, track.Postgap, "—", "—");
        }
    }

    private static string TrackTitle(CdTrack track) =>
        $"Track {track.Number:D2} — {(track.Title.Length == 0 ? track.IsAudio ? "Audio" : track.ModeName : track.Title)}";

    [RelayCommand]
    private Task OpenCdTrackAsync(CdTrack? track) =>
        track is not null && trackNodes.TryGetValue(track.Number, out StructureNode? node)
            ? SelectTrackNodeAsync(node, track) : Task.CompletedTask;

    private async Task SelectTrackNodeAsync(StructureNode node, CdTrack track)
    {
        if (disposed) { return; }
        if (SelectedCdTrack?.Number != track.Number)
        {
            StopAudioForWorkspace();
            HexOffset = 0;
            SelectedCdTrack = track;
            AudioStatus = track.IsAudio
                ? track.Playback.Available ? "Ready to play." : track.Playback.Reason
                : "This is a data track.";
        }
        ToolVisible = true;
        if (track.VolumeId is not null)
        {
            await NavigateToVolumeAsync(Info.Volumes.Single(v => v.Id == track.VolumeId), "/");
        }
        else
        {
            navigation?.Cancel();
            preview?.Cancel();
            Volume = null;
            CurrentPath = "/";
            Breadcrumbs.Clear();
            ReplaceEntries([], preserveSelection: false);
            SelectedNode = node;
        }

        if (disposed || SelectedCdTrack?.Number != track.Number) { return; }
        Properties = $"Track {track.Number:D2} · {track.ModeName}\nTitle: {track.Title}\nPerformer: {track.Performer}\n"
            + $"Source: {track.SourceName}\nProgram start LBA: {track.StartLba:N0}\nProgram sectors: {track.SectorCount:N0}\nSector size: {track.SectorSize} bytes\n"
            + $"Stored pregap: {track.StoredPregap}; unstored pregap: {track.Pregap}; unstored postgap: {track.Postgap} sectors\n"
            + $"Indexes (file sectors): {string.Join(", ", track.Indexes.Select(i => $"{i.Number:D2}={i.FileSector}"))}\n"
            + $"Duration: {Clock(track.Duration.TotalSeconds)}";
        ToolIndex = track.IsAudio ? AudioPlayerTab : TrackInformationTab;
        UpdateInspector();
    }

    partial void OnSelectedCdTrackChanged(CdTrack? value)
    {
        OnPropertyChanged(nameof(AudioDuration));
        OnPropertyChanged(nameof(AudioTrackTitle));
        OnPropertyChanged(nameof(AudioFormat));
        NotifyAudio();
    }

    partial void OnIsAudioPlayingChanged(bool value) => NotifyAudio();

    partial void OnSeekSecondsChanged(double value)
    {
        if (!updatingSeek) { seekEdited = true; }
    }

    partial void OnVolumeLevelChanged(double value) =>
        Volatile.Write(ref playbackGain, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0);

    private CdTrack? AdjacentAudio(int direction)
    {
        if (SelectedCdTrack is not { } selected) { return null; }
        var tracks = CdTracks.Where(t => t.Playback.Available);
        return direction < 0 ? tracks.LastOrDefault(t => t.Number < selected.Number)
            : tracks.FirstOrDefault(t => t.Number > selected.Number);
    }

    private void NotifyAudio()
    {
        foreach (StructureNode node in trackNodes.Values)
        { node.IsPlayingTrack = IsAudioPlaying && node.Track?.Number == SelectedCdTrack?.Number; }
        PlayCdCommand.NotifyCanExecuteChanged();
        PauseCdCommand.NotifyCanExecuteChanged();
        StopCdCommand.NotifyCanExecuteChanged();
        SeekCdCommand.NotifyCanExecuteChanged();
        PreviousCdCommand.NotifyCanExecuteChanged();
        NextCdCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPlayCd))]
    private void PlayCd()
    {
        if (!CanPlayCd || SelectedCdTrack is not { } track || explorer is not ICdTrackSource source) { return; }
        ToolVisible = true;
        ToolIndex = AudioPlayerTab;
        long length = track.SectorCount * (long)CdAudioPlayback.FramesPerSector;
        if (audioFrame >= length) { SetAudioFrame(0, true); }
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        playback = request;
        IsAudioPlaying = true;
        AudioStatus = "Playing";
        var progress = new Progress<long>(frame =>
        {
            if (playback == request && !disposed) { SetAudioFrame(frame, false); }
        });
        playbackTask = PlayTrackAsync(playbackTask, request, source, track, audioFrame, progress);
    }

    private async Task PlayTrackAsync(Task preceding, CancellationTokenSource request, ICdTrackSource source,
        CdTrack track, long start, IProgress<long> progress)
    {
        try
        {
            await preceding;
            request.Token.ThrowIfCancellationRequested();
            using Stream input = await source.OpenAudioTrackAsync(Info.Id, track.Number, request.Token);
            await Task.Run(() =>
            {
                using ICdAudioOutput output = CdAudioOutput.Open();
                CdAudioPlayback.Run(input, output, track.SectorCount, start, track.BigEndianAudio,
                    () => Volatile.Read(ref playbackGain), progress, request.Token);
            }, request.Token);
            if (playback == request && !disposed)
            {
                SetAudioFrame(track.SectorCount * (long)CdAudioPlayback.FramesPerSector, true);
                AudioStatus = "End of track";
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            if (playback == request && !disposed) { AudioStatus = "Playback failed — " + error.Message; }
        }
        finally
        {
            if (playback == request)
            {
                playback = null;
                if (!disposed) { IsAudioPlaying = false; }
            }
            request.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPauseCd))]
    private void PauseCd()
    {
        CancelPlayback();
        SetAudioFrame(audioFrame, true);
        AudioStatus = "Paused";
    }

    [RelayCommand(CanExecute = nameof(CanStopCd))]
    private void StopCd()
    {
        StopAudioForWorkspace();
        AudioStatus = "Stopped";
    }

    internal void StopAudioForWorkspace()
    {
        CancelPlayback();
        SetAudioFrame(0, true);
        if (SelectedCdTrack?.Playback.Available == true) { AudioStatus = "Stopped"; }
    }

    private void CancelPlayback()
    {
        CancellationTokenSource? prior = playback;
        playback = null;
        prior?.Cancel();
        IsAudioPlaying = false;
    }

    [RelayCommand(CanExecute = nameof(CanSeekCd))]
    private void SeekCd()
    {
        bool playing = IsAudioPlaying;
        CancelPlayback();
        double seconds = double.IsFinite(SeekSeconds) ? Math.Clamp(SeekSeconds, 0, AudioDuration) : 0;
        SetAudioFrame((long)(seconds * CdAudioPlayback.SampleRate), true);
        if (playing && seconds < AudioDuration) { PlayCd(); }
        else { AudioStatus = seconds >= AudioDuration ? "End of track" : "Ready to play."; }
    }

    [RelayCommand(CanExecute = nameof(CanPreviousCd))]
    private Task PreviousCdAsync() => MoveAudioAsync(-1);

    [RelayCommand(CanExecute = nameof(CanNextCd))]
    private Task NextCdAsync() => MoveAudioAsync(1);

    private async Task MoveAudioAsync(int direction)
    {
        bool playing = IsAudioPlaying;
        await OpenCdTrackAsync(AdjacentAudio(direction));
        if (playing) { PlayCd(); }
    }

    private void SetAudioFrame(long frame, bool setSeek)
    {
        audioFrame = frame;
        double seconds = frame / (double)CdAudioPlayback.SampleRate;
        AudioElapsed = Clock(seconds);
        if (setSeek || !seekEdited)
        {
            updatingSeek = true;
            SeekSeconds = Math.Min(seconds, AudioDuration);
            updatingSeek = false;
            seekEdited = false;
        }
        StopCdCommand.NotifyCanExecuteChanged();
    }

    private static string Clock(double seconds)
    {
        int elapsed = (int)seconds;
        return $"{elapsed / 60}:{elapsed % 60:00}";
    }
}
