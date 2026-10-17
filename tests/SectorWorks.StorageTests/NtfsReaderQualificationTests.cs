using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using Xunit.Abstractions;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsReaderQualificationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    public async Task VersionRowsQualifyIndependentContentMetadataAndEverySharedReadWorkflow(byte major, byte minor)
    {
        using var work = new WorkDirectory();
        NtfsCompatibilityFixtures.Fixture fixture = NtfsCompatibilityFixtures.Create(work, major, minor);
        string before = Hash(fixture.Path);
        await Qualify(fixture.Path, work,
            [(NtfsCompatibilityFixtures.ResidentPath, NtfsCompatibilityFixtures.ResidentData),
             (NtfsCompatibilityFixtures.NonresidentPath, NtfsCompatibilityFixtures.NonresidentData),
             (NtfsCompatibilityFixtures.UnicodePath, NtfsCompatibilityFixtures.UnicodeData)]);
        Assert.Equal(before, Hash(fixture.Path));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Provenance = "Generated structural derivative of committed mkntfs golden; NOT historical OS-authored",
            Version = $"{major}.{minor}",
            SourceSha256 = before,
            fixture.Layout
        }));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task AttributeListRowsQualifyEveryWorkflowContentAndSourceIntegrity(bool nonresidentList, bool directory, bool split)
    {
        using var work = new WorkDirectory();
        var fixture = new NtfsAttributeListTests.ExtensionFixture(work, nonresidentList, directory, split);
        string before = Hash(fixture.Path);
        await Qualify(fixture.Path, work,
            [("/SENTINEL.TXT", "SectorWorks foundation sentinel v1\n"u8.ToArray()),
             ("/LARGE.BIN", Enumerable.Range(0, 8192).Select(i => checked((byte)(i % 251))).ToArray())]);
        using (IImageSession image = new ImageOpener().OpenReadOnly(fixture.Path))
        {
            Assert.False(new DiskImageWriter().QualifyVolume(image, image.Volumes[0].Id).Available);
        }
        Assert.Equal(before, Hash(fixture.Path));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Provenance = "Generated structural derivative of committed mkntfs golden; no owner content",
            Version = "3.1",
            nonresidentList,
            directory,
            split,
            SourceSha256 = before
        }));
    }

    private async Task Qualify(string path, WorkDirectory work, (string Path, byte[] Bytes)[] expected)
    {
        using var service = new ExplorerService(stagingDirectory: work.PathFor("staging"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        foreach ((string filePath, byte[] bytes) in expected)
        {
            string parent = filePath[..filePath.LastIndexOf('/')];
            if (parent.Length == 0) { parent = "/"; }
            FileEntry entry = Assert.Single(await service.ReadDirectoryAsync(workspace.Id, volume.Id, parent),
                item => item.Path == filePath);
            Assert.Equal(bytes.Length, entry.Length);
            SearchHit hit = Assert.Single(await service.SearchAsync(workspace.Id, entry.Name));
            Assert.Equal(filePath, hit.Entry.Path);
            PreviewResult hex = await service.ReadHexAsync(workspace.Id, volume.Id, filePath, 0);
            Assert.False(string.IsNullOrWhiteSpace(hex.Text));
            Assert.Contains(string.Join(" ", bytes.Take(4).Select(value => value.ToString("X2"))),
                hex.Text, StringComparison.Ordinal);
            if (bytes.Length > 4096)
            {
                PreviewResult second = await service.ReadHexAsync(workspace.Id, volume.Id, filePath, 4096);
                Assert.Contains(string.Join(" ", bytes.Skip(4096).Take(4).Select(value => value.ToString("X2"))),
                    second.Text, StringComparison.Ordinal);
            }
            if (filePath.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(System.Text.Encoding.UTF8.GetString(bytes),
                    (await service.PreviewAsync(workspace.Id, volume.Id, filePath)).Text);
            }
            string target = work.PathFor($"qualification-{Array.FindIndex(expected, item => item.Path == filePath)}");
            Directory.CreateDirectory(target);
            ExtractionResult result = await service.ExtractAsync(workspace.Id, volume.Id, [entry], target);
            Assert.Empty(result.Failures);
            Assert.Equal(bytes.Length, result.BytesCompleted);
            byte[] extracted = File.ReadAllBytes(System.IO.Path.Combine(target, entry.Name));
            Assert.Equal(bytes, extracted);
            string expectedHash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(extracted)));
            string? nativeHash = await NativeHash(path, filePath);
            if (nativeHash is not null) { Assert.Equal(expectedHash, nativeHash); }
            output.WriteLine(JsonSerializer.Serialize(new { filePath, Bytes = bytes.Length, ExpectedSha256 = expectedHash, NativeSha256 = nativeHash }));
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReadDirectoryAsync(workspace.Id, volume.Id, "/", canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PreviewAsync(workspace.Id, volume.Id, expected[0].Path, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReadHexAsync(workspace.Id, volume.Id, expected[0].Path, 0, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SearchAsync(workspace.Id, "TXT", canceled.Token));
        string cancelledOutput = work.PathFor("cancelled-extraction");
        Directory.CreateDirectory(cancelledOutput);
        string selectedParent = expected[0].Path[..expected[0].Path.LastIndexOf('/')];
        FileEntry selected = Assert.Single(await service.ReadDirectoryAsync(workspace.Id, volume.Id,
            selectedParent.Length == 0 ? "/" : selectedParent),
            entry => entry.Path == expected[0].Path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExtractAsync(workspace.Id, volume.Id, [selected], cancelledOutput, cancellationToken: canceled.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(cancelledOutput));
        Assert.NotEmpty(await service.ReadDirectoryAsync(workspace.Id, volume.Id, "/"));
        if (workspace.State == WorkspaceState.ReadyReadOnly)
        {
            Assert.False(volume.Capabilities!.Modify.Available);
            Assert.False(volume.Capabilities.SaveAs.Available);
            Assert.False(workspace.Capabilities.CommitInPlace.Available);
            await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(workspace.Id,
                new(ImageChangeKind.Delete, volume.Id, expected[0].Path)));
            string rejected = work.PathFor("rejected.img");
            await Assert.ThrowsAsync<NotSupportedException>(() => service.SaveAsAsync(workspace.Id, rejected));
            Assert.False(File.Exists(rejected));
        }
    }

    private static async Task<string?> NativeHash(string image, string filePath)
    {
        string? tool = new[] { "/usr/bin/ntfscat", "/usr/sbin/ntfscat" }.FirstOrDefault(File.Exists);
        if (tool is null) { return null; }
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(image);
        start.ArgumentList.Add(filePath);
        using Process process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            byte[] hash = await SHA256.HashDataAsync(process.StandardOutput.BaseStream, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await error);
            return Convert.ToHexString(hash);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void ReaderPreflightUsesBoundedRequestsAndMetadataWork(bool nonresidentList, bool directory, bool split)
    {
        using var work = new WorkDirectory();
        var fixture = new NtfsAttributeListTests.ExtensionFixture(work, nonresidentList, directory, split);
        string before = Hash(fixture.Path);
        using (var device = new CountingDevice(fixture.Path))
        {
            var boot = new byte[512];
            using (FileStream input = File.OpenRead(fixture.Path)) { input.ReadExactly(boot); }
            int sector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
            NtfsReadInfo info = NtfsReadValidation.Validate(device, boot, sector, checked(sector * boot[13]), default);
            Assert.True(info.HasAttributeLists);
            Assert.Equal("3.1", info.Version);
            Assert.InRange(device.MaximumRequest, 1, 65536);
            Assert.InRange(device.BytesRead, 1, 2 * 1024 * 1024);
            Assert.InRange(device.ReadCalls, 1, 2048);
        }
        Assert.Equal(before, Hash(fixture.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDuringMetadataReadsIsNotCorruptionAndReleasesSource(bool nonresidentList)
    {
        using var work = new WorkDirectory();
        var fixture = new NtfsAttributeListTests.ExtensionFixture(work, nonresidentList, true, false);
        string before = Hash(fixture.Path);
        using var cancellation = new CancellationTokenSource();
        using (var device = new CountingDevice(fixture.Path, cancellation))
        {
            var boot = new byte[512];
            using (FileStream input = File.OpenRead(fixture.Path)) { input.ReadExactly(boot); }
            int sector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
            Assert.Throws<OperationCanceledException>(() =>
                NtfsReadValidation.Validate(device, boot, sector, checked(sector * boot[13]), cancellation.Token));
            Assert.InRange(device.ReadCalls, 10, 11);
        }
        Assert.Equal(before, Hash(fixture.Path));
        using FileStream unlocked = File.Open(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private sealed class CountingDevice(string path, CancellationTokenSource? cancellation = null) : IReadOnlyBlockDevice
    {
        private readonly FileBlockDevice source = new(path);
        internal long BytesRead { get; private set; }
        internal int ReadCalls { get; private set; }
        internal int MaximumRequest { get; private set; }
        public long Length => source.Length;
        public int LogicalSectorSize => source.LogicalSectorSize;
        public int? PhysicalSectorSize => source.PhysicalSectorSize;

        public int Read(long offset, Span<byte> buffer)
        {
            MaximumRequest = Math.Max(MaximumRequest, buffer.Length);
            int read = source.Read(offset, buffer);
            BytesRead += read;
            if (++ReadCalls == 10) { cancellation?.Cancel(); }
            return read;
        }

        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, buffer.Span));
        }

        public void Dispose() => source.Dispose();
    }
}
