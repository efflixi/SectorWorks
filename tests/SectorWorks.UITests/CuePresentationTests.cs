using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

[Collection("Headless UI")]
public sealed class CuePresentationTests
{
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
}
