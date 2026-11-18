using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

[Collection("Headless UI")]
public sealed class CuePresentationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TrackViewExportsSelectedOrAllEligibleAudio(bool all, bool cancelFolder)
    {
        string directory = Directory.CreateTempSubdirectory("sectorworks-wav-ui-").FullName;
        try
        {
            string image = Path.Combine(directory, "mixed.cue");
            string destination = Directory.CreateDirectory(Path.Combine(directory, "output")).FullName;
            File.WriteAllBytes(Path.Combine(directory, "tracks.bin"), new byte[8 * 2352]);
            File.WriteAllText(image, """
                FILE tracks.bin BINARY
                  TRACK 01 MODE1/2352
                    INDEX 01 00:00:00
                  TRACK 02 AUDIO
                    TITLE "First song"
                    INDEX 01 00:00:02
                  TRACK 03 AUDIO
                    INDEX 01 00:00:04
                  TRACK 04 AUDIO
                    FLAGS PRE
                    INDEX 01 00:00:06
                """);

            using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
            await session.Dispatch(async () =>
            {
                using var service = new ExplorerService(stagingDirectory: Path.Combine(directory, "stage"));
                WorkspaceInfo info = await service.OpenAsync(image);
                var window = new Window();
                var dialogs = new AudioExportDialogs(window, cancelFolder ? null : destination);
                using var workspace = new WorkspaceViewModel(service, dialogs, info, new LayoutSettings());
                await workspace.OpenCdTrackCommand.ExecuteAsync(workspace.CdTracks[1]);
                workspace.ToolIndex = 6;
                var view = new WorkspaceView { DataContext = workspace };
                window.Content = view;
                window.Show();
                try
                {
                    Dispatcher.UIThread.RunJobs();
                    var grid = view.FindControl<DataGrid>("TrackGrid")!;
                    grid.SelectedItem = workspace.CdTracks[1];
                    Button export = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
                        button => Equals(button.Content, "Export Audio Tracks…") && button.IsVisible);
                    Assert.True(export.IsEnabled);
                    Assert.Equal(workspace.CdTracks[1], export.CommandParameter);
                    export.Command!.Execute(export.CommandParameter);
                    Window dialog = Assert.Single(window.OwnedWindows);
                    CheckBox[] rows = dialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
                    Assert.Equal(3, rows.Length);
                    Assert.Contains("First song", Assert.IsType<TextBlock>(rows[0].Content).Text);
                    Assert.True(rows[0].IsChecked);
                    Assert.False(rows[1].IsChecked);
                    Assert.False(rows[2].IsEnabled);
                    if (all)
                    {
                        Click(dialog, "Select All");
                        Assert.True(rows[1].IsChecked);
                        Assert.False(rows[2].IsChecked);
                    }
                    Click(dialog, "Choose Folder…");
                    await workspace.ExportCdAudioCommand.ExecutionTask!;
                    Assert.Equal(1, dialogs.FolderPicks);
                    Assert.Empty(workspace.Error);
                    string?[] files = Directory.GetFiles(destination).Select(Path.GetFileName).Order().ToArray();
                    if (cancelFolder)
                    {
                        Assert.Equal("Audio export cancelled.", workspace.Status);
                        Assert.Empty(files);
                    }
                    else
                    {
                        Assert.Contains("Audio export complete", workspace.Status);
                        Assert.Equal(all ? ["Track 02.wav", "Track 03.wav"] : new[] { "Track 02.wav" }, files);
                    }
                }
                finally { window.Close(); }
                await service.CloseAsync(info.Id);
                return true;
            }, CancellationToken.None);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MixedDiscSelectionTransitionsBetweenFilesystemAndAudioMetadata()
    {
        using var fake = new FakeExplorer();
        WorkspaceInfo seed = fake.Create("mixed.cue");
        VolumeInfo volume = Assert.Single(seed.Volumes);
        var data = new CdTrack(1, CdTrackMode.Mode1Cooked, "Disc data", "", "data.bin", 2048, 0, 240, 0, 0, 0,
            [new(1, 0)], volume.Id, new(false, "Data track"));
        var audio = new CdTrack(2, CdTrackMode.Audio, "Generated audio", "SectorWorks", "test.bin", 2352, 242, 600, 0, 2, 0,
            [new(1, 0)], null, new(true, "Audio track"));
        WorkspaceInfo info = seed with
        {
            Metadata = seed.Metadata with { Format = "BIN/CUE" },
            OpticalDisc = new("Mixed test", "", [new(1, [data, audio])]),
        };
        using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), info, new LayoutSettings());
        Assert.Equal(2, workspace.CdTracks.Count);
        StructureNode session = workspace.Roots[0].Children.Single(n => n.Title == "Session 1");
        Assert.True(session.Children[0].IsDataTrack);
        Assert.True(session.Children[1].IsAudioTrack);

        await workspace.SelectNodeCommand.ExecuteAsync(session.Children[1]);
        Assert.Equal("Track 02 — Generated audio", workspace.AudioTrackTitle);
        Assert.Contains("44.1 kHz", workspace.AudioFormat);
        Assert.Contains("LBA: 242", workspace.Inspector);
        Assert.Equal(5, workspace.ToolIndex);
        Assert.Null(workspace.Volume);
        Assert.Empty(workspace.Entries);

        await workspace.SelectNodeCommand.ExecuteAsync(session.Children[0]);
        Assert.Equal(volume.Id, workspace.Volume?.Id);
        Assert.NotEmpty(workspace.Entries);
        Assert.Contains("MODE1/2048", workspace.Inspector);
        Assert.Equal(6, workspace.ToolIndex);
    }

    private static void Click(Window dialog, string label)
    {
        Button button = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), item => Equals(item.Content, label));
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private sealed class AudioExportDialogs(Window window, string? destination) : IPlatformDialogs, ICdAudioExportDialogs
    {
        private readonly AvaloniaPlatformDialogs dialogs = new(window);
        public int FolderPicks { get; private set; }

        public Task<IReadOnlyList<int>?> PickAudioTracksAsync(IReadOnlyList<CdTrack> audioTracks, int? selectedTrack,
            CancellationToken cancellationToken = default) => dialogs.PickAudioTracksAsync(audioTracks, selectedTrack, cancellationToken);

        public Task<string?> PickAudioExportFolderAsync(CancellationToken cancellationToken = default)
        {
            FolderPicks++;
            return Task.FromResult<string?>(destination);
        }

        public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
