using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiscUtils.Iso9660;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

[Collection("Headless UI")]
public sealed class HexTabTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1494)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(9000)]
    public async Task RealBinaryFileDisplaysExactFirstPageAndPagingBounds(int length)
    {
        string image = Path.Combine(Path.GetTempPath(), $"sectorworks-hex-{Guid.NewGuid():N}.iso");
        byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i % 256)).ToArray();
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "HEX_TEST" };
        builder.AddFile("BINARY.DAT", bytes);
        builder.Build(image);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
            await session.Dispatch(async () =>
            {
                using var service = new ExplorerService();
                WorkspaceInfo info = await service.OpenAsync(image);
                using var workspace = new WorkspaceViewModel(service, new FakeDialogs(), info, new LayoutSettings());
                await workspace.InitializeAsync();
                var view = new WorkspaceView { DataContext = workspace };
                var window = new Window { Content = view };
                window.Show();
                try
                {
                    Dispatcher.UIThread.RunJobs();
                    workspace.SelectEntries([workspace.Entries.Single()]);
                    await workspace.PreviewCommand.ExecutionTask!;
                    if (length > 1) { Assert.Contains("Hex View", workspace.PreviewText); }
                    view.FindControl<TabControl>("ToolTabs")!.SelectedIndex = 1;
                    Dispatcher.UIThread.RunJobs();
                    Assert.NotNull(workspace.HexCommand.ExecutionTask);
                    await workspace.HexCommand.ExecutionTask;
                    PreviewResult expected = await service.ReadHexAsync(info.Id, workspace.Volume!.Id, workspace.Selection[0].Path, 0);
                    Assert.Equal($"{expected.Description}\nOffset: {0:N0} bytes\n\n{expected.Text}", workspace.HexText);
                    byte[] displayed = expected.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                        .SelectMany(row => row.Substring(14, 48).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        .Select(hex => Convert.ToByte(hex, 16)).ToArray();
                    Assert.Equal(bytes.Take(4096), displayed);
                    Assert.Equal(length > 4096, workspace.NextHexCommand.CanExecute(null));
                    Assert.False(workspace.PreviousHexCommand.CanExecute(null));
                    if (length > 4096)
                    {
                        await workspace.NextHexCommand.ExecuteAsync(null);
                        PreviewResult next = await service.ReadHexAsync(info.Id, workspace.Volume.Id, workspace.Selection[0].Path, 4096);
                        Assert.EndsWith(next.Text, workspace.HexText);
                        await workspace.PreviousHexCommand.ExecuteAsync(null);
                        Assert.EndsWith(expected.Text, workspace.HexText);
                    }
                    Assert.Equal("", workspace.Error);
                }
                finally { window.Close(); }
                await service.CloseAsync(info.Id);
                return true;
            }, CancellationToken.None);
        }
        finally { File.Delete(image); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1494)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(9000)]
    public async Task SelectingHexTabLoadsFirstPageWithoutPaging(int length)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var fake = new FakeExplorer();
            using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("hex.img"), new LayoutSettings());
            var view = new WorkspaceView { DataContext = workspace };
            var window = new Window { Content = view, Width = 1000, Height = 640 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                workspace.SelectEntries([FakeExplorer.Readme with { Length = length }]);
                await workspace.PreviewCommand.ExecutionTask!;
                TabControl tabs = view.FindControl<TabControl>("ToolTabs")!;
                tabs.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(workspace.HexCommand.ExecutionTask);
                await workspace.HexCommand.ExecutionTask;
                Assert.Equal(1, workspace.ToolIndex);
                Assert.Equal(0, fake.LastHexOffset);
                Assert.Equal(1, fake.HexReadCount);
                Assert.Contains("01 02 03", workspace.HexText);
                window.UpdateLayout();
                Assert.Contains(view.GetVisualDescendants().OfType<TextBox>(),
                    box => box.Text == workspace.HexText && box.IsVisible);
                Assert.False(workspace.PreviousHexCommand.CanExecute(null));
                Assert.Equal(length > 4096, workspace.NextHexCommand.CanExecute(null));
                if (length > 4096)
                {
                    await workspace.NextHexCommand.ExecuteAsync(null);
                    Assert.Equal(4096, fake.LastHexOffset);
                    await workspace.PreviousHexCommand.ExecuteAsync(null);
                    Assert.Equal(0, fake.LastHexOffset);
                }
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LateTextPreviewCannotReplaceExplicitHexSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessBootstrap));
        await session.Dispatch(async () =>
        {
            using var fake = new FakeExplorer();
            using var workspace = new WorkspaceViewModel(fake, new FakeDialogs(), fake.Create("hex.img"), new LayoutSettings());
            var view = new WorkspaceView { DataContext = workspace };
            var window = new Window { Content = view };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                fake.SlowPreview = new TaskCompletionSource<PreviewResult>();
                workspace.SelectEntries([FakeExplorer.Readme]);
                Task pending = workspace.PreviewCommand.ExecutionTask!;
                view.FindControl<TabControl>("ToolTabs")!.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                await workspace.HexCommand.ExecutionTask!;
                fake.SlowPreview.SetResult(new PreviewResult("", false, "Unsupported encoding; use Hex View"));
                await pending;
                Assert.Equal(1, workspace.ToolIndex);
                Assert.Contains("01 02 03", workspace.HexText);
                Assert.Equal("", workspace.Error);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }
}
