using System.Text;
using SectorWorks.Core;
using SectorWorks.DependencyProbe;
using SectorWorks.Platform;
using SectorWorks.Services;

namespace SectorWorks.ServicesTests;

public sealed class ExplorerTests
{
    [Fact]
    public async Task MultiWorkspaceSearchPreviewAndCloseRemainIndependent()
    {
        var log = new TestLog();
        using var service = new ExplorerService(new TestOpener(), log);
        WorkspaceInfo first = await service.OpenAsync("one");
        WorkspaceInfo second = await service.OpenAsync("two");
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.Capabilities.Modify.Available);
        Assert.False(first.Capabilities.PlayAudioTrack.Available);
        Assert.False(first.Capabilities.Validate.Available);
        Assert.Equal("hello\n", (await service.PreviewAsync(first.Id, "volume", "/hello.txt")).Text);
        Assert.Single(await service.SearchAsync(first.Id, "HELLO"));
        await service.CloseAsync(first.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadDirectoryAsync(first.Id, "volume", "/"));
        Assert.Equal(3, (await service.ReadDirectoryAsync(second.Id, "volume", "/")).Count);
        Assert.Contains(log.Items, item => item.Stage == "Closed");
        Assert.DoesNotContain(log.Items, item => item.Message.Contains("hello\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedOpenDoesNotCloseExistingImage()
    {
        var log = new TestLog();
        using var service = new ExplorerService(new TestOpener(), log);
        WorkspaceInfo workspace = await service.OpenAsync("good");
        await Assert.ThrowsAsync<ImageFormatException>(() => service.OpenAsync("bad"));
        Assert.NotEmpty(await service.ReadDirectoryAsync(workspace.Id, "volume", "/"));
        Assert.Contains(log.Items, item => item.Stage == "Failed" && item.Level == "Error");
    }

    [Fact]
    public async Task EffectiveCapabilitiesAreDerivedFromFilesystemNotVolumeCount()
    {
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo info = await service.OpenAsync("metadata-only");
        Assert.Single(info.Volumes);
        Assert.False(info.Capabilities.Browse.Available);
        Assert.False(info.Capabilities.Extract.Available);
        Assert.False(info.Capabilities.Preview.Available);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.ReadDirectoryAsync(info.Id, "volume", "/"));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.SearchAsync(info.Id, "hello"));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.PreviewAsync(info.Id, "volume", "/hello.txt"));
        Assert.Contains("Read-only hex", (await service.ReadHexAsync(info.Id, null, null, 0)).Description);
    }

    [Fact]
    public async Task FileHexIsPagedAndOffsetsAreValidated()
    {
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo info = await service.OpenAsync("good");
        PreviewResult page = await service.ReadHexAsync(info.Id, "volume", "/large.bin", 4096);
        Assert.StartsWith("000000001000", page.Text);
        Assert.True(page.Truncated);
        Assert.True(page.Text.Length < 25000);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadHexAsync(info.Id, null, null, -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadHexAsync(info.Id, "volume", "/hello.txt", 7));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewAndHexRejectPrematureEndWithoutSuccess(bool hex)
    {
        var log = new TestLog();
        using var service = new ExplorerService(new TestOpener(), log);
        WorkspaceInfo info = await service.OpenAsync("short-stream");
        await Assert.ThrowsAsync<EndOfStreamException>(() => hex
            ? service.ReadHexAsync(info.Id, "volume", "/hello.txt", 0)
            : service.PreviewAsync(info.Id, "volume", "/hello.txt"));
        Assert.Contains(log.Items, item => item.Level == "Error");
        Assert.NotEmpty(await service.ReadDirectoryAsync(info.Id, "volume", "/"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewAndHexRejectFileMetadataLengthMismatch(bool hex)
    {
        var log = new TestLog();
        using var service = new ExplorerService(new TestOpener(), log);
        WorkspaceInfo info = await service.OpenAsync("length-mismatch");
        await Assert.ThrowsAsync<InvalidDataException>(() => hex
            ? service.ReadHexAsync(info.Id, "volume", "/hello.txt", 0)
            : service.PreviewAsync(info.Id, "volume", "/hello.txt"));
        Assert.Contains(log.Items, item => item.Level == "Error");
    }

    [Fact]
    public async Task RawHexCompletesPartialReadsAndRejectsPrematureEnd()
    {
        var log = new TestLog();
        using var service = new ExplorerService(new TestOpener(), log);
        WorkspaceInfo valid = await service.OpenAsync("partial-block-reads");
        PreviewResult page = await service.ReadHexAsync(valid.Id, null, null, 0);
        Assert.Equal(256, page.Text.Count(character => character == '\n'));
        Assert.Contains("000000000FF0", page.Text);
        WorkspaceInfo invalid = await service.OpenAsync("short-block-device");
        await Assert.ThrowsAsync<EndOfStreamException>(() => service.ReadHexAsync(invalid.Id, null, null, 0));
        Assert.Contains(log.Items, item => item.Stage == "Hex" && item.Level == "Error");
    }

    [Fact]
    public async Task ExtractionRechecksSelectedMetadataAndNeverOverwrites()
    {
        using var directory = new TestDirectory();
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo info = await service.OpenAsync("good");
        var forged = new FileEntry("/hello.txt", "../../escape", false, long.MaxValue, null, "");
        ExtractionResult first = await service.ExtractAsync(info.Id, "volume", [forged], directory.Path);
        Assert.Equal(1, first.FilesCompleted);
        Assert.Equal(6, first.BytesCompleted);
        Assert.Equal("hello\n", File.ReadAllText(System.IO.Path.Combine(directory.Path, "hello.txt")));
        ExtractionResult second = await service.ExtractAsync(info.Id, "volume", [forged], directory.Path);
        Assert.Equal(0, second.FilesCompleted);
        Assert.Single(second.Failures);
        Assert.Equal("hello\n", File.ReadAllText(System.IO.Path.Combine(directory.Path, "hello.txt")));
    }

    [Fact]
    public async Task RecursiveExtractionPreservesEmptyDirectoriesAndReportsImageLinks()
    {
        using var directory = new TestDirectory();
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo info = await service.OpenAsync("good");
        FileEntry docs = (await service.ReadDirectoryAsync(info.Id, "volume", "/")).Single(e => e.Name == "DOCS");
        ExtractionResult result = await service.ExtractAsync(info.Id, "volume", [docs], directory.Path);
        Assert.Equal(1, result.FilesCompleted);
        Assert.True(Directory.Exists(System.IO.Path.Combine(directory.Path, "DOCS", "empty")));
        Assert.Equal("nested\n", File.ReadAllText(System.IO.Path.Combine(directory.Path, "DOCS", "nested.txt")));
        Assert.Single(result.Failures);
    }

    [Fact]
    public async Task CancellationRemovesPartialFileAndKeepsWorkspaceUsable()
    {
        using var directory = new TestDirectory();
        using var cancellation = new CancellationTokenSource();
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo info = await service.OpenAsync("good");
        FileEntry large = (await service.ReadDirectoryAsync(info.Id, "volume", "/")).Single(e => e.Name == "large.bin");
        var progress = new InlineProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExtractAsync(info.Id, "volume", [large], directory.Path, progress, cancellation.Token));
        Assert.Empty(Directory.GetFiles(directory.Path));
        Assert.NotEmpty(await service.ReadDirectoryAsync(info.Id, "volume", "/"));
    }

    [Theory]
    [InlineData("/../outside")]
    [InlineData("/DOCS/../hello.txt")]
    [InlineData("/DOCS//nested.txt")]
    [InlineData("/DOCS\\nested.txt")]
    public async Task InvalidImagePathsAreRejectedWithExplicitErrors(string path)
    {
        var log = new TestLog();
        using var service = new ExplorerService(new TestOpener(), log);
        WorkspaceInfo info = await service.OpenAsync("good");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadDirectoryAsync(info.Id, "volume", path));
        Assert.Contains(log.Items, item => item.Level == "Error");
    }

    [Fact]
    public async Task InvalidChildMetadataAndDeepNestingFailWithoutUnboundedTraversal()
    {
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo bad = await service.OpenAsync("hostile");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadDirectoryAsync(bad.Id, "volume", "/"));
        WorkspaceInfo deep = await service.OpenAsync("deep");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SearchAsync(deep.Id, "d"));
    }

    [Fact]
    public async Task SearchAndDirectoryLimitsFailExplicitlyInsteadOfReturningPartialSuccess()
    {
        var log = new TestLog();
        using var service = new ExplorerService(new TestOpener(), log);
        WorkspaceInfo many = await service.OpenAsync("many");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SearchAsync(many.Id, "entry"));
        WorkspaceInfo huge = await service.OpenAsync("huge");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadDirectoryAsync(huge.Id, "volume", "/"));
        Assert.Contains(log.Items, item => item.Stage == "Search" && item.Level == "Error");
        Assert.Contains(log.Items, item => item.Stage == "ReadDirectory" && item.Level == "Error");
    }

    [Fact]
    public async Task InvalidHostNamesAreReportedWithoutProducingFiles()
    {
        using var directory = new TestDirectory();
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo image = await service.OpenAsync("unsafe");
        IReadOnlyList<FileEntry> entries = await service.ReadDirectoryAsync(image.Id, "volume", "/");
        ExtractionResult result = await service.ExtractAsync(image.Id, "volume", entries, directory.Path);
        Assert.Equal(0, result.FilesCompleted);
        Assert.Equal(entries.Count, result.Failures.Count);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task CloseWaitsForOperationAndDoesNotDisposeLiveStreams()
    {
        using var directory = new TestDirectory();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var service = new ExplorerService(new TestOpener(), new TestLog());
        WorkspaceInfo info = await service.OpenAsync("good");
        FileEntry large = (await service.ReadDirectoryAsync(info.Id, "volume", "/")).Single(e => e.Name == "large.bin");
        var progress = new InlineProgress(_ =>
        {
            started.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        });
        Task<ExtractionResult> extraction = service.ExtractAsync(info.Id, "volume", [large], directory.Path, progress);
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        Task close = service.CloseAsync(info.Id);
        Assert.False(close.IsCompleted);
        release.Set();
        Assert.Equal(1, (await extraction).FilesCompleted);
        await close;
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    [InlineData("mbr.img")]
    [InlineData("gpt.img")]
    [InlineData("ntfs.img")]
    [InlineData("joliet.iso")]
    [InlineData("dynamic.vhd")]
    [InlineData("dynamic.vhdx")]
    public async Task GoldenEndToEndBrowsePreviewSearchAndExtractionLeaveSourceUnchanged(string name)
    {
        using var working = new TestDirectory();
        using var destination = new TestDirectory();
        string root = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "golden");
        FixtureEntry fixture = FixtureCatalog.ReadManifest(root).Single(e => e.Name == name);
        string path = FixtureCatalog.Materialize(root, working.Path, fixture);
        byte[] before = await HashAsync(path);
        using var service = new ExplorerService(new SectorWorks.Storage.ImageOpener(), new TestLog());
        WorkspaceInfo info = await service.OpenAsync(path);
        VolumeInfo volume = Assert.Single(info.Volumes);
        FileEntry sentinel = (await service.ReadDirectoryAsync(info.Id, volume.Id, "/")).Single(e => e.Name == "SENTINEL.TXT");
        Assert.Equal("SectorWorks foundation sentinel v1\n", (await service.PreviewAsync(info.Id, volume.Id, sentinel.Path)).Text);
        Assert.NotEmpty(await service.SearchAsync(info.Id, "sentinel"));
        ExtractionResult result = await service.ExtractAsync(info.Id, volume.Id, [sentinel], destination.Path);
        Assert.Empty(result.Failures);
        Assert.Equal(1, result.FilesCompleted);
        Assert.Equal("SectorWorks foundation sentinel v1\n", await File.ReadAllTextAsync(System.IO.Path.Combine(destination.Path, "SENTINEL.TXT")));
        await service.CloseAsync(info.Id);
        Assert.Equal(before, await HashAsync(path));
    }

    private static async Task<byte[]> HashAsync(string path)
    {
        await using FileStream file = File.OpenRead(path);
        return await System.Security.Cryptography.SHA256.HashDataAsync(file);
    }

    private sealed class InlineProgress(Action<OperationProgress> action) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => action(value);
    }

    private sealed class TestOpener : IImageOpener
    {
        public IImageSession OpenReadOnly(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path == "bad")
            {
                throw new ImageFormatException("Intentionally invalid image.");
            }
            return new TestSession(path);
        }
    }

    private sealed class TestSession(string path) : IImageSession
    {
        public ImageMetadata Metadata { get; } = new(path, "Test", 8192, 8192, 512, new Dictionary<string, string>());
        public IReadOnlyBlockDevice Device { get; } = new TestDevice(path);
        public IReadOnlyList<PartitionInfo> Partitions => [];
        public IReadOnlyList<ImageVolume> Volumes { get; } = [new("volume", null, new TestFileSystem(path))];
        public IReadOnlyList<string> Warnings => [];
        public void Dispose()
        {
            Device.Dispose();
            foreach (ImageVolume volume in Volumes)
            {
                volume.FileSystem.Dispose();
            }
        }
    }

    private sealed class TestDevice(string mode) : IReadOnlyBlockDevice
    {
        public long Length => 8192;
        public int LogicalSectorSize => 512;
        public int? PhysicalSectorSize => null;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset > Length || buffer.Length > Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (mode == "short-block-device" && offset >= 10)
            {
                return 0;
            }
            int count = mode is "partial-block-reads" or "short-block-device" ? Math.Min(buffer.Length, 7) : buffer.Length;
            buffer[..count].Fill(0x41);
            return count;
        }
        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, buffer.Span));
        }
        public void Dispose() { }
    }

    private sealed class TestFileSystem(string mode) : IReadOnlyFileSystem
    {
        private static readonly byte[] Hello = Encoding.UTF8.GetBytes("hello\n");
        private static readonly byte[] Nested = Encoding.UTF8.GetBytes("nested\n");
        public string Name => "Test";
        public string VolumeLabel => "TEST";
        public bool IsCaseSensitive => true;
        public WorkspaceCapabilities Capabilities => mode == "metadata-only"
            ? new(new(false, "Metadata only."), new(false, "No file reads."), new(false, "No file reads."), new(false, "No validation."))
            : WorkspaceCapabilities.ReadOnlyFileSystem;
        public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mode == "hostile")
            {
                return [new("/../outside", "../outside", false, 0, null, "")];
            }
            if (mode == "deep")
            {
                return [new((path == "/" ? "" : path) + "/d", "d", true, 0, null, "")];
            }
            if (mode is "many" or "huge")
            {
                return Enumerable.Range(0, mode == "many" ? 10001 : 100001)
                    .Select(i => Entry("/entry" + i, 0)).ToArray();
            }
            if (mode == "unsafe")
            {
                return [Entry("/NUL.txt", 0), Entry("/stream:name", 0), Entry("/trailing.", 0)];
            }
            return path switch
            {
                "/" => [Entry("/hello.txt", Hello.Length), Entry("/large.bin", 200000), Entry("/DOCS", 0, true)],
                "/DOCS" => [Entry("/DOCS/nested.txt", Nested.Length), Entry("/DOCS/empty", 0, true), new("/DOCS/link", "link", true, 0, null, "ReparsePoint", IsLink: true)],
                "/DOCS/empty" => [],
                _ => throw new DirectoryNotFoundException(path),
            };
        }
        public Stream OpenRead(string path) => path switch
        {
            "/hello.txt" when mode == "short-stream" => new PrematureEndStream(Hello[..3], Hello.Length),
            "/hello.txt" when mode == "length-mismatch" => new MemoryStream(Hello[..3], writable: false),
            "/hello.txt" => new MemoryStream(Hello, writable: false),
            "/DOCS/nested.txt" => new MemoryStream(Nested, writable: false),
            "/large.bin" => new MemoryStream(new byte[200000], writable: false),
            _ => throw new FileNotFoundException(path),
        };
        public void Dispose() { }
        private static FileEntry Entry(string path, long length, bool directory = false) =>
            new(path, path[(path.LastIndexOf('/') + 1)..], directory, length, null, directory ? "Directory" : "Archive");
    }

    private sealed class PrematureEndStream(byte[] bytes, long advertisedLength) : MemoryStream(bytes, writable: false)
    {
        public override long Length => advertisedLength;
    }
}

internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sectorworks-tests-" + Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal sealed class TestLog : IExplorerLog
{
    public List<(string Stage, string Path, string Message, string Level)> Items { get; } = [];
    public void Write(string stage, string path, string message, string level = "Information")
    {
        lock (Items)
        {
            Items.Add((stage, path, message, level));
        }
    }
}
