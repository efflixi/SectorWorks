using System.Buffers.Binary;
using System.Diagnostics;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using Xunit.Abstractions;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsStagedWorkflowTests(ITestOutputHelper outputLog)
{
    public static IEnumerable<object[]> Rows()
    {
        foreach (string container in new[] { "raw", "vhd-fixed", "vhd-dynamic", "vhdx-fixed", "vhdx-dynamic" })
        {
            foreach (string layout in new[] { "filesystem", "mbr", "gpt" })
            {
                yield return [container, layout, 512];
                if (container.StartsWith("vhdx", StringComparison.Ordinal)) { yield return [container, layout, 4096]; }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task EveryQualifiedNtfsDiskStagesReviewsAndSavesWithoutChangingTheSource(string container, string layout, int physical)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, layout, "ntfs", physical: physical);
        string sourceHash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        // Phase 3: direct commit reuses this same qualified Apply/ValidateOutput pipeline
        // wrapped with locking/backup/journal, so it is qualified wherever Save As is.
        Assert.True(volume.Capabilities.CommitInPlace.Available, volume.Capabilities.CommitInPlace.Reason);
        string small = work.PathFor("small.txt");
        File.WriteAllText(small, "managed NTFS overlay\n");
        string large = work.PathFor("large.bin");
        byte[] payload = Enumerable.Range(0, 20000).Select(index => (byte)(index % 251)).ToArray();
        File.WriteAllBytes(large, payload);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Mixed λ directory"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/Mixed λ directory/Added λ.txt", small));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/Mixed λ directory/Large.bin", large));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/SENTINEL.TXT", large));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/LARGE.BIN", small));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/Mixed λ directory", NewName: "Renamed λ directory"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/Renamed λ directory/Added λ.txt", NewName: "ADDED λ.txt"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/Temporary.txt", small));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Delete, volume.Id, "/Temporary.txt"));
        Assert.Equal(9, (await service.GetEditingSnapshotAsync(workspace.Id)).Changes.Count);
        await service.UndoAsync(workspace.Id);
        Assert.Contains(await service.ReadDirectoryAsync(workspace.Id, volume.Id, "/"), entry => entry.Name == "Temporary.txt");
        await service.RedoAsync(workspace.Id);
        const string staged = "/Renamed λ directory/ADDED λ.txt";
        Assert.Equal("managed NTFS overlay\n", (await service.PreviewAsync(workspace.Id, volume.Id, staged)).Text);
        Assert.Contains("6D 61 6E 61", (await service.ReadHexAsync(workspace.Id, volume.Id, staged, 0)).Text, StringComparison.Ordinal);
        SearchHit hit = Assert.Single(await service.SearchAsync(workspace.Id, "ADDED λ"));
        Assert.True(hit.Entry.IsChanged);
        string extraction = work.PathFor("extracted");
        Directory.CreateDirectory(extraction);
        Assert.Empty((await service.ExtractAsync(workspace.Id, volume.Id, [hit.Entry], extraction)).Failures);
        Assert.Equal("managed NTFS overlay\n", File.ReadAllText(Path.Combine(extraction, "ADDED λ.txt")));

        string output = work.PathFor("saved.disk");
        SaveImageResult result = await service.SaveAsAsync(workspace.Id, output);
        Assert.True(result.FilesValidated >= 4);
        using (IImageSession reopened = new ImageOpener().OpenReadOnly(output))
        {
            Assert.Equal(workspace.Metadata.Format, reopened.Metadata.Format);
            Assert.Equal(workspace.Metadata.VirtualLength, reopened.Device.Length);
            Assert.Equal(workspace.Partitions, reopened.Partitions);
            IReadOnlyFileSystem fs = reopened.Volumes[0].FileSystem;
            Assert.Contains(fs.ReadDirectory("/Renamed λ directory"), entry => entry.Name == "ADDED λ.txt");
            using var text = new StreamReader(fs.OpenRead(staged));
            Assert.Equal("managed NTFS overlay\n", text.ReadToEnd());
            using var shrunk = new StreamReader(fs.OpenRead("/LARGE.BIN"));
            Assert.Equal("managed NTFS overlay\n", shrunk.ReadToEnd());
            using Stream grown = fs.OpenRead("/SENTINEL.TXT");
            using var bytes = new MemoryStream();
            grown.CopyTo(bytes);
            Assert.Equal(payload, bytes.ToArray());
            Assert.DoesNotContain(fs.ReadDirectory("/"), entry => entry.Name == "Temporary.txt");
        }
        string raw = work.PathFor("independent.raw");
        if (container == "raw") { File.Copy(output, raw); }
        else
        {
            DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f",
                container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vpc", "-O", "raw", output, raw);
        }
        CheckNative(work, raw, payload);
        await service.DiscardAsync(workspace.Id);
        Assert.Empty((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
        Assert.Equal(sourceHash, Hash(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-save-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-volume-*"));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task EveryQualifiedNtfsDiskDirectlyCommitsInPlaceAndLeavesNoResidue(string container, string layout, int physical)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, layout, "ntfs", physical: physical);
        string directory = Path.GetDirectoryName(source)!;
        using var service = new ExplorerService(stagingDirectory: directory);
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.CommitInPlace.Available, volume.Capabilities.CommitInPlace.Reason);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "committed NTFS overlay\n");
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/SENTINEL.TXT", payload));

        CommitResult result = await service.CommitAsync(workspace.Id);
        Assert.True(result.FilesValidated > 0);

        Assert.Empty(Directory.GetFiles(directory, "*.sectorworks-journal-*"));
        Assert.Empty(Directory.GetFiles(directory, "*.sectorworks-backup-*"));
        Assert.Empty(Directory.GetFiles(directory, ".sectorworks-commit-*"));
        Assert.Empty(CommitRecovery.Discover(directory));

        string raw = work.PathFor("commit-independent.raw");
        if (container == "raw") { File.Copy(source, raw); }
        else
        {
            DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f",
                container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vpc", "-O", "raw", source, raw);
        }
        string volumePath = work.PathFor("native-check.img");
        using (IImageSession image = new ImageOpener().OpenReadOnly(raw))
        using (var file = File.Create(volumePath))
        {
            ImageVolume vol = Assert.Single(image.Volumes);
            long start = vol.Partition?.Offset ?? 0;
            long length = vol.Partition?.Length ?? image.Device.Length;
            byte[] buffer = new byte[65536];
            for (long offset = 0; offset < length;)
            {
                int count = image.Device.Read(start + offset, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)));
                Assert.True(count > 0);
                file.Write(buffer, 0, count);
                offset += count;
            }
        }
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsfix", "-n", volumePath)));
        Assert.Equal("committed NTFS overlay\n"u8.ToArray(), RunNative("ntfscat", volumePath, "/SENTINEL.TXT"));

        using IImageSession reopened = new ImageOpener().OpenReadOnly(source);
        using var text = new StreamReader(reopened.Volumes[0].FileSystem.OpenRead("/SENTINEL.TXT"));
        Assert.Equal("committed NTFS overlay\n", text.ReadToEnd());
    }

    [Fact]
    public async Task CreationAndDeletionOfAnEntireNewSubtreeLeavesNoUnexpectedEntries()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        string payload = work.PathFor("payload");
        File.WriteAllText(payload, "temporary data");
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/temporary"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/temporary/child.txt", payload));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Delete, volume.Id, "/temporary"));
        string output = work.PathFor("saved.img");
        await service.SaveAsAsync(workspace.Id, output);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(output);
        Assert.DoesNotContain(reopened.Volumes[0].FileSystem.ReadDirectory("/"), entry => entry.Name == "temporary");
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    [InlineData(16384)]
    [InlineData(32768)]
    [InlineData(65536)]
    public async Task FreshNativeAuthoredNtfsSupportsPortableCreationAndInheritedSecurity(int clusterSize)
    {
        using var work = new WorkDirectory();
        string source = work.PathFor("native.img");
        using (FileStream file = File.Create(source)) { file.SetLength(32 * 1024 * 1024); }
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("mkntfs",
            "-F", "-Q", "-T", "-L", "SWNTFS", "-s", "512", "-c",
            clusterSize.ToString(System.Globalization.CultureInfo.InvariantCulture), "-p", "0", "-H", "4", "-S", "32", source)));
        string sourceHash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "native-authored managed repair");
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Parent λ"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Parent λ/Child"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/Parent λ/Child/Created λ.txt", payload));
        string large = work.PathFor("large.bin");
        File.WriteAllBytes(large, Enumerable.Range(0, 140000).Select(index => (byte)(index % 251)).ToArray());
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/Parent λ/Child/Created λ.txt", large));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/Parent λ/Child/Created λ.txt", payload));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/Parent λ/Child/Temporary.bin", large));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Delete, volume.Id, "/Parent λ/Child/Temporary.bin"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/Parent λ/Child", NewName: "CHILD"));
        string saved = work.PathFor("saved.img");
        await service.SaveAsAsync(workspace.Id, saved);
        Assert.Equal(File.ReadAllBytes(payload), RunNative("ntfscat", saved, "/Parent λ/CHILD/Created λ.txt"));
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsinfo", "-F", "/Parent λ/CHILD/Created λ.txt", saved)));
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsinfo", "-m", saved)));
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public async Task MultipleNtfsVolumesShareHistoryButKeepTheirEditsAndSecurityIndependent()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "vhdx-dynamic", "gpt", "ntfs", twoFatVolumes: true);
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.Equal(2, workspace.Volumes.Count);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "multi-volume");
        foreach (VolumeInfo volume in workspace.Volumes)
        {
            Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
            await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Volume " + volume.Id));
            await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/Volume " + volume.Id + "/New λ.txt", payload));
        }
        await service.UndoAsync(workspace.Id);
        Assert.Equal(3, (await service.GetEditingSnapshotAsync(workspace.Id)).Changes.Count);
        await service.RedoAsync(workspace.Id);
        string saved = work.PathFor("saved.vhdx");
        await service.SaveAsAsync(workspace.Id, saved);
        using IImageSession image = new ImageOpener().OpenReadOnly(saved);
        foreach (ImageVolume volume in image.Volumes)
        {
            using var text = new StreamReader(volume.FileSystem.OpenRead("/Volume " + volume.Id + "/New λ.txt"));
            Assert.Equal("multi-volume", text.ReadToEnd());
        }
        Assert.Equal(baseline, Hash(source));
    }

    [Fact]
    public async Task QualifiedFatAndNtfsVolumesDispatchTheirOwnWriterWithinOneSaveAs()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "gpt", "fat12", readOnlyNtfs: true);
        PartitionInfo ntfsPartition;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            ntfsPartition = image.Volumes.Single(volume => volume.FileSystem.Name == "NTFS").Partition!;
        }
        using (var file = new FileStream(source, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] boot = new byte[512];
            file.Position = ntfsPartition.Offset;
            file.ReadExactly(boot);
            BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(28), checked((uint)(ntfsPartition.Offset / 512)));
            file.Position = ntfsPartition.Offset;
            file.Write(boot);
            file.Position = ntfsPartition.Offset + ntfsPartition.Length - 512;
            file.Write(boot);
        }
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.Equal(2, workspace.Volumes.Count);
        Assert.Contains(workspace.Volumes, volume => volume.FileSystem == "NTFS");
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "mixed-volume");
        foreach (VolumeInfo volume in workspace.Volumes)
        {
            Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
            await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/Mixed λ.txt", payload));
        }
        string saved = work.PathFor("saved.raw");
        await service.SaveAsAsync(workspace.Id, saved);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(saved);
        foreach (ImageVolume volume in reopened.Volumes)
        {
            using var text = new StreamReader(volume.FileSystem.OpenRead("/Mixed λ.txt"));
            Assert.Equal("mixed-volume", text.ReadToEnd());
        }
        Assert.Equal(baseline, Hash(source));
    }

    [Fact]
    public async Task NtfsLowSpaceAndCanceledOrConflictingDestinationsNeverPublishOrModifyTheSource()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        string payload = work.PathFor("too-large.bin");
        using (FileStream file = File.Create(payload)) { file.SetLength(new FileInfo(source).Length); }
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(workspace.Id,
            new(ImageChangeKind.AddFile, volume.Id, "/Too large.bin", payload)));
        Assert.Empty((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
        File.WriteAllText(payload, "small payload");
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, volume.Id, "/New.txt", payload));
        string canceled = work.PathFor("canceled.img");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveAsAsync(workspace.Id, canceled, cancellationToken: cancellation.Token));
        Assert.False(File.Exists(canceled));
        string existing = work.PathFor("existing.img");
        File.WriteAllText(existing, "untouched destination");
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(workspace.Id, existing));
        Assert.Equal("untouched destination", File.ReadAllText(existing));
        Assert.Equal(baseline, Hash(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-save-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-volume-*"));
    }

    [Theory]
    [InlineData("Copying image")]
    [InlineData("Applying staged changes")]
    [InlineData("Validating tree and SHA-256")]
    [InlineData("Rechecking source baseline")]
    public async Task NtfsCancellationAtSaveBoundariesPreservesTheSourceAndPendingHistory(string stage)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, workspace.Volumes[0].Id, "/New directory"));
        using var cancellation = new CancellationTokenSource();
        string output = work.PathFor("canceled.img");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsAsync(workspace.Id, output,
            new InlineProgress(progress => { if (progress.Stage == stage) { cancellation.Cancel(); } }), cancellation.Token));
        Assert.False(File.Exists(output));
        Assert.Single((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
        Assert.Equal(baseline, Hash(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-save-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-volume-*"));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("validation")]
    public async Task ActualNtfsWriterIoAndValidationFailuresCannotPublishNewOutput(string failure)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        string baseline = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new FaultWriter(failure),
            stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, workspace.Volumes[0].Id, "/New directory"));
        string output = work.PathFor("failed.img");
        Exception error = failure == "validation"
            ? await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsAsync(workspace.Id, output))
            : await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(workspace.Id, output));
        Assert.Contains("Injected NTFS " + failure, error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Single((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
        Assert.Equal(baseline, Hash(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-save-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-volume-*"));
    }

    [Fact]
    public async Task NtfsMftAndDirectoryIndexGrowthThenDeletionRetainsReachableAllocation()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "resident index-growth payload");
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, volume.Id, "/Growth"));
        for (int index = 0; index < 96; index++)
        {
            await service.StageAsync(workspace.Id,
                new(ImageChangeKind.AddFile, volume.Id, $"/Growth/Entry {index:D3} with a long λ filename.txt", payload));
        }
        for (int index = 0; index < 32; index++)
        {
            await service.StageAsync(workspace.Id,
                new(ImageChangeKind.Delete, volume.Id, $"/Growth/Entry {index:D3} with a long λ filename.txt"));
        }
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/Growth", NewName: "GROWTH"));
        string saved = work.PathFor("saved.img");
        await service.SaveAsAsync(workspace.Id, saved);
        using (IImageSession reopened = new ImageOpener().OpenReadOnly(saved))
        {
            FileEntry[] entries = reopened.Volumes[0].FileSystem.ReadDirectory("/GROWTH").ToArray();
            Assert.Equal(64, entries.Length);
            Assert.Equal(Enumerable.Range(32, 64).Select(index => $"Entry {index:D3} with a long λ filename.txt").Order(StringComparer.Ordinal),
                entries.Select(entry => entry.Name).Order(StringComparer.Ordinal));
            _ = NtfsWriteValidation.Capture(reopened.Device);
        }
        Assert.Equal(File.ReadAllBytes(payload), RunNative("ntfscat", saved, "/GROWTH/Entry 095 with a long λ filename.txt"));
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsinfo", "-m", saved)));
        Assert.Equal(baseline, Hash(source));
    }

    [Fact]
    public async Task RenameAndReplacementPreserveDistinctStandardFilenameIndexAndDosAliasDates()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        byte[] longDates = new byte[64];
        byte[] shortDates = new byte[64];
        for (int index = 0; index < 8; index++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(longDates.AsSpan(index * 8), new DateTime(1980 + index, 2, 3, 4, 5, 6, DateTimeKind.Utc).ToFileTimeUtc());
            BinaryPrimitives.WriteInt64LittleEndian(shortDates.AsSpan(index * 8), new DateTime(1990 + index, 3, 4, 5, 6, 7, DateTimeKind.Utc).ToFileTimeUtc());
        }
        DiscUtils.WindowsFileInformation original;
        byte[] security;
        long id;
        using (var stream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite))
        {
            using (var fs = new SectorWorks.ManagedNtfs.NtfsFileSystem(stream))
            {
                fs.SetShortName("SENTINEL.TXT", "LEGACY~1.TXT");
                original = fs.GetFileStandardInformation("SENTINEL.TXT");
                original.CreationTime = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
                original.LastAccessTime = original.CreationTime.AddDays(1);
                original.LastWriteTime = original.CreationTime.AddDays(2);
                original.ChangeTime = original.CreationTime.AddDays(3);
                fs.SetFileStandardInformation("SENTINEL.TXT", original);
                fs.SetFileNameTimestamps("SENTINEL.TXT", longDates);
                fs.SetFileNameTimestamps("LEGACY~1.TXT", shortDates);
                security = fs.GetSecurityBytes("SENTINEL.TXT");
                id = fs.GetFileId("SENTINEL.TXT");
            }
            stream.Flush(true);
        }
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        string payload = work.PathFor("payload.bin");
        File.WriteAllBytes(payload, Enumerable.Range(0, 20000).Select(index => (byte)(index % 251)).ToArray());
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, volume.Id, "/SENTINEL.TXT", payload));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/SENTINEL.TXT", NewName: "Renamed λ.txt"));
        string saved = work.PathFor("saved.img");
        await service.SaveAsAsync(workspace.Id, saved);
        using (var stream = File.OpenRead(saved))
        using (var fs = new SectorWorks.ManagedNtfs.NtfsFileSystem(stream))
        {
            DiscUtils.WindowsFileInformation actual = fs.GetFileStandardInformation("Renamed λ.txt");
            Assert.Equal(original.CreationTime, actual.CreationTime);
            Assert.Equal(original.LastAccessTime, actual.LastAccessTime);
            Assert.Equal(original.LastWriteTime, actual.LastWriteTime);
            Assert.Equal(original.ChangeTime, actual.ChangeTime);
            Assert.Equal(original.FileAttributes, actual.FileAttributes);
            Assert.Equal(longDates, fs.GetFileNameTimestamps("Renamed λ.txt"));
            Assert.Equal(shortDates, fs.GetFileNameTimestamps("LEGACY~1.TXT"));
            Assert.Equal("LEGACY~1.TXT", fs.GetShortName("Renamed λ.txt"));
            Assert.Equal(security, fs.GetSecurityBytes("Renamed λ.txt"));
            Assert.Equal(id, fs.GetFileId("Renamed λ.txt"));
        }
        Assert.Equal(File.ReadAllBytes(payload), RunNative("ntfscat", saved, "/LEGACY~1.TXT"));
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsinfo", "-F", "/Renamed λ.txt", saved)));
        Assert.Equal(baseline, Hash(source));
    }

    [Fact]
    public async Task FullNtfsRejectsNewAllocationButCanDeleteAnExistingNonresidentFile()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        long length;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            NtfsWriteSnapshot allocation = NtfsWriteValidation.Capture(image.Device);
            using Stream file = image.Volumes[0].FileSystem.OpenRead("/LARGE.BIN");
            length = checked(file.Length + allocation.FreeClusters * allocation.ClusterSize);
        }
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsfallocate", "-f", "-l",
            length.ToString(System.Globalization.CultureInfo.InvariantCulture), source, "/LARGE.BIN")));
        string baseline = Hash(source);
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            Assert.Equal(0, NtfsWriteValidation.Capture(image.Device).FreeClusters);
        }
        using (FileStream stream = File.OpenRead(source))
        using (var fs = new SectorWorks.ManagedNtfs.NtfsFileSystem(stream))
        {
            Assert.Equal(length, fs.GetFileLength("LARGE.BIN"));
            _ = fs.GetFileNameTimestamps("LARGE.BIN");
        }
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.Modify.Available, volume.Capabilities.Modify.Reason);
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(workspace.Id,
            new(ImageChangeKind.CreateDirectory, volume.Id, "/No room")));
        Assert.Empty((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Delete, volume.Id, "/LARGE.BIN"));
        string saved = work.PathFor("saved.img");
        await service.SaveAsAsync(workspace.Id, saved);
        using (IImageSession image = new ImageOpener().OpenReadOnly(saved))
        {
            Assert.True(NtfsWriteValidation.Capture(image.Device).FreeClusters > 0);
            Assert.DoesNotContain(image.Volumes[0].FileSystem.ReadDirectory("/"), entry => entry.Name == "LARGE.BIN");
        }
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsinfo", "-m", saved)));
        Assert.Equal(baseline, Hash(source));
    }

    [Fact]
    public async Task NearFullReplacementAllocatesAcrossFallbackZonesWithoutLosingClusters()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "ntfs");
        long length;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            NtfsWriteSnapshot allocation = NtfsWriteValidation.Capture(image.Device);
            using Stream file = image.Volumes[0].FileSystem.OpenRead("/LARGE.BIN");
            length = checked(file.Length + (allocation.FreeClusters - 16) * allocation.ClusterSize);
        }
        string baseline = Hash(source);
        string payload = work.PathFor("near-full.bin");
        using (FileStream file = File.Create(payload)) { file.SetLength(length); }
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, workspace.Volumes[0].Id, "/LARGE.BIN", payload));
        string saved = work.PathFor("saved.img");
        await service.SaveAsAsync(workspace.Id, saved);
        using (IImageSession image = new ImageOpener().OpenReadOnly(saved))
        {
            Assert.Equal(16, NtfsWriteValidation.Capture(image.Device).FreeClusters);
            using Stream file = image.Volumes[0].FileSystem.OpenRead("/LARGE.BIN");
            Assert.Equal(length, file.Length);
        }
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfscluster", "-F", "/LARGE.BIN", saved)));
        Assert.Equal(baseline, Hash(source));
    }

    private void CheckNative(WorkDirectory work, string raw, byte[] largePayload)
    {
        string volumePath = work.PathFor("native-check.img");
        using (IImageSession image = new ImageOpener().OpenReadOnly(raw))
        using (var file = File.Create(volumePath))
        {
            ImageVolume volume = Assert.Single(image.Volumes);
            long start = volume.Partition?.Offset ?? 0;
            long length = volume.Partition?.Length ?? image.Device.Length;
            byte[] buffer = new byte[65536];
            for (long offset = 0; offset < length;)
            {
                int count = image.Device.Read(start + offset, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)));
                Assert.True(count > 0);
                file.Write(buffer, 0, count);
                offset += count;
            }
        }
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsfix", "-n", volumePath)));
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsinfo", "-m", volumePath)));
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfsinfo", "-F", "/Renamed λ directory/ADDED λ.txt", volumePath)));
        outputLog.WriteLine(System.Text.Encoding.UTF8.GetString(RunNative("ntfscluster", "-F", "/SENTINEL.TXT", volumePath)));
        Assert.Equal(largePayload, RunNative("ntfscat", volumePath, "/SENTINEL.TXT"));
        Assert.Equal(largePayload, RunNative("ntfscat", volumePath, "/Renamed λ directory/Large.bin"));
        Assert.Equal("managed NTFS overlay\n"u8.ToArray(), RunNative("ntfscat", volumePath, "/LARGE.BIN"));
        Assert.Equal("managed NTFS overlay\n"u8.ToArray(), RunNative("ntfscat", volumePath, "/Renamed λ directory/ADDED λ.txt"));
    }

    private static byte[] RunNative(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start) ?? throw new IOException("Could not start " + command);
        using var bytes = new MemoryStream();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(bytes);
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(true); throw new IOException(command + " timed out."); }
        copy.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, command + ": " + error.GetAwaiter().GetResult());
        return bytes.ToArray();
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private sealed class FaultWriter(string failure) : IStagedImageWriter
    {
        private readonly DiskImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public Capability QualifyVolume(IImageSession image, string volumeId) => inner.QualifyVolume(image, volumeId);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateCapacity(image, operations, token);
        public void Apply(string path, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            if (failure == "validation") { inner.Apply(path, operations, token); return; }
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var stream = new FaultStream(file, failure);
            new NtfsImageWriter().ApplyToStream(stream, operations, token);
        }
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            inner.ValidateOutput(source, output, operations, token);
            if (failure == "validation") { throw new ImageFormatException("Injected NTFS validation failure."); }
        }
    }

    private sealed class FaultStream(Stream inner, string failure) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Flush()
        {
            if (failure == "flush") { throw new IOException("Injected NTFS flush failure."); }
            inner.Flush();
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (failure == "write") { throw new IOException("Injected NTFS write failure."); }
            inner.Write(buffer);
        }
    }
}
