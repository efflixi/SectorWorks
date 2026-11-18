using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using Xunit.Abstractions;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsExpandedGrowthTests(ITestOutputHelper log)
{
    [Theory]
    [MemberData(nameof(NtfsExpandedWorkflowTests.Features), MemberType = typeof(NtfsExpandedWorkflowTests))]
    public async Task CandidateMftAndDirectoryGrowthRetainAllocationAndSecurity(byte major, byte minor, string feature)
    {
        using var work = new WorkDirectory();
        string source = NtfsExpandedWorkflowTests.CreateCandidateVolume(work, major, minor, feature);
        string original = Hash(source);
        long originalMftLength;
        using (var device = new FileBlockDevice(source)) { originalMftLength = MftLength(device); }
        using var service = new ExplorerService(new ImageOpener(), writer: new DiskImageWriter(includeNtfsCandidates: true),
            stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        string id = Assert.Single(info.Volumes).Id;
        string payload = work.PathFor("payload.txt"); File.WriteAllText(payload, "expanded index growth\n");
        // Grow the actual root, including root index-allocation extensions, and
        // the MFT; then delete entries and rename while keeping retained content.
        for (int index = 0; index < 288; index++)
        {
            await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, id, $"/Growth {index:D3} long λ name.txt", payload));
        }
        for (int index = 0; index < 96; index++)
        { await service.StageAsync(info.Id, new(ImageChangeKind.Delete, id, $"/Growth {index:D3} long λ name.txt")); }
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, id, "/Growth 287 long λ name.txt", NewName: "RENAMED λ.txt"));
        string output = work.PathFor("grown.img");
        await service.SaveAsAsync(info.Id, output);
        using (IImageSession image = new ImageOpener().OpenReadOnly(output))
        {
            IReadOnlyFileSystem fs = Assert.Single(image.Volumes).FileSystem;
            Assert.Equal(191, fs.ReadDirectory("/").Count(e => e.Name.StartsWith("Growth ", StringComparison.Ordinal)));
            Assert.Contains(fs.ReadDirectory("/"), e => e.Name == "RENAMED λ.txt");
            _ = NtfsWriteValidation.CaptureLegacyCandidate(image.Device);
            Assert.True(MftLength(image.Device) > originalMftLength, "The test must grow actual MFT data, not merely reuse allocated records.");
        }
        log.WriteLine(System.Text.Encoding.UTF8.GetString(NtfsStagedWorkflowTests.RunNative("ntfsfix", "-n", output)));
        log.WriteLine(System.Text.Encoding.UTF8.GetString(NtfsStagedWorkflowTests.RunNative("ntfsinfo", "-m", output)));
        Assert.Equal(File.ReadAllBytes(payload), NtfsStagedWorkflowTests.RunNative("ntfscat", output, "/RENAMED λ.txt"));
        Assert.Equal(original, Hash(source));
    }

    [Theory]
    [MemberData(nameof(NtfsExpandedWorkflowTests.Features), MemberType = typeof(NtfsExpandedWorkflowTests))]
    public async Task CandidateNearFullReplacementRetainsExactFreeAllocationAndContent(byte major, byte minor, string feature)
    {
        using var work = new WorkDirectory();
        string source = NtfsExpandedWorkflowTests.CreateCandidateVolume(work, major, minor, feature);
        string original = Hash(source);
        long length;
        long originalListClusters;
        using (var device = new FileBlockDevice(source))
        {
            NtfsWriteSnapshot allocation = NtfsWriteValidation.CaptureLegacyCandidate(device);
            length = checked(8192 + (allocation.FreeClusters - 64) * allocation.ClusterSize);
            originalListClusters = ChangedFileAllocation(device).ListClusters;
        }
        string payload = work.PathFor("near-full.bin");
        using (FileStream data = File.Create(payload)) { data.SetLength(length); }
        using var service = new ExplorerService(new ImageOpener(), writer: new DiskImageWriter(includeNtfsCandidates: true),
            stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, Assert.Single(info.Volumes).Id, "/LARGE.BIN", payload));
        string output = work.PathFor("near-full.img");
        await service.SaveAsAsync(info.Id, output);
        using (var device = new FileBlockDevice(output))
        {
            (long listClusters, long dataClusters) = ChangedFileAllocation(device);
            Assert.Equal((length + 4095) / 4096, dataClusters);
            // A changed file's now-unnecessary nonresident attribute list can
            // legitimately be released as its extension collapses. Account for
            // that exact metadata allocation, rather than assuming it leaks or
            // weakening the assertion to an arbitrary free-cluster range.
            Assert.Equal(64 + originalListClusters - listClusters,
                NtfsWriteValidation.CaptureLegacyCandidate(device).FreeClusters);
        }
        log.WriteLine(System.Text.Encoding.UTF8.GetString(NtfsStagedWorkflowTests.RunNative("ntfsfix", "-n", output)));
        byte[] actual = NtfsStagedWorkflowTests.RunNative("ntfscat", output, "/LARGE.BIN");
        Assert.Equal(length, actual.LongLength);
        Assert.Equal(-1, actual.AsSpan().IndexOfAnyExcept((byte)0));
        Assert.Equal(original, Hash(source));
    }

    private static long MftLength(IReadOnlyBlockDevice device)
    {
        byte[] boot = new byte[512]; Assert.Equal(512, device.Read(0, boot));
        NtfsResolvedMetadata metadata = NtfsReadValidation.ResolveForWriter(device, boot, 512, 4096, 1024, 4096, default);
        NtfsResolvedFile mft = Assert.Single(metadata.Files, f => f.Id == 0);
        return checked((long)PartitionReader.U64(Assert.Single(mft.Streams, s => s.Type == 0x80 && s.Name == "").Extents[0].Bytes, 48));
    }

    private static (long ListClusters, long DataClusters) ChangedFileAllocation(IReadOnlyBlockDevice device)
    {
        byte[] boot = new byte[512]; Assert.Equal(512, device.Read(0, boot));
        NtfsResolvedMetadata metadata = NtfsReadValidation.ResolveForWriter(device, boot, 512, 4096, 1024, 4096, default);
        NtfsResolvedFile file = Assert.Single(metadata.Files, f => f.Streams.Any(s => s.Type == 0x30 && s.Extents.Any(a =>
        {
            int value = PartitionReader.U16(a.Bytes, 20);
            return System.Text.Encoding.Unicode.GetString(a.Bytes, value + 66, a.Bytes[value + 64] * 2) == "LARGE.BIN";
        })));
        long list = file.AttributeList is { Bytes: var listBytes } && listBytes[8] == 1 ? checked((long)PartitionReader.U64(listBytes, 40)) / 4096 : 0;
        byte[] data = Assert.Single(file.Streams, s => s.Type == 0x80 && s.Name == "").Extents[0].Bytes;
        return (list, checked((long)PartitionReader.U64(data, 40)) / 4096);
    }
}
