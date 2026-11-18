using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;
using AttributeType = SectorWorks.ManagedNtfs.AttributeType;
using NtfsFileSystem = SectorWorks.ManagedNtfs.NtfsFileSystem;

namespace SectorWorks.StorageTests;

public sealed class NtfsExpandedMixedVolumeTests
{
    private const long First = 1024 * 1024;
    private const long VolumeLength = 32 * 1024 * 1024;
    private const long Second = First + VolumeLength + 2 * 1024 * 1024;

    [Theory]
    [MemberData(nameof(NtfsExpandedWorkflowTests.Features), MemberType = typeof(NtfsExpandedWorkflowTests))]
    public async Task QualifiedPrimaryPreservesReadableDirtySiblingThroughSaveAsAndCommit(byte major, byte minor, string feature)
    {
        using var work = new WorkDirectory();
        using var other = new WorkDirectory();
        string primary = NtfsExpandedWorkflowTests.CreateCandidateVolume(work, major, minor, feature);
        string sibling = NtfsCompatibilityFixtures.Create(other, 3, 1, forWriterCandidate: true).Path;
        MarkDirty(sibling);
        string source = Compose(work, primary, sibling);
        string baseline = work.PathFor("baseline.disk"); File.Copy(source, baseline);
        string original = Hash(source);
        string untouched = OutsidePrimary(source);
        using var service = new ExplorerService(new ImageOpener(),
            writer: new DiskImageWriter(includeNtfsCandidates: true, includeNtfsCommitCandidates: true), stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.Equal(2, info.Volumes.Count);
        VolumeInfo editable = info.Volumes[0], readOnly = info.Volumes[1];
        Assert.True(editable.Capabilities!.Modify.Available, editable.Capabilities.Modify.Reason);
        Assert.True(editable.Capabilities.CommitInPlace.Available, editable.Capabilities.CommitInPlace.Reason);
        Assert.False(readOnly.Capabilities!.Modify.Available);
        Assert.False(readOnly.Capabilities.SaveAs.Available);
        Assert.False(readOnly.Capabilities.CommitInPlace.Available);
        Assert.NotEmpty(await service.ReadDirectoryAsync(info.Id, readOnly.Id, "/"));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.CreateDirectory, readOnly.Id, "/FORBIDDEN")));
        Assert.Empty((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, editable.Id, "/MIXED SAFE"));
        string output = work.PathFor("saved.disk");
        await service.SaveAsAsync(info.Id, output);
        Assert.Equal(original, Hash(source));
        Assert.Equal(untouched, OutsidePrimary(output));
        if (!OperatingSystem.IsWindows()) { NativePrimary(work, output, "saveas"); }
        await service.CommitAsync(info.Id);
        Assert.NotEqual(original, Hash(source));
        Assert.Equal(untouched, OutsidePrimary(source));
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, editable.Id, "/"), e => e.Name == "MIXED SAFE");
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, readOnly.Id, "/"), e => e.Name is "MIXED SAFE" or "FORBIDDEN");
        if (OperatingSystem.IsWindows())
        {
            string root = Environment.GetEnvironmentVariable("SECTORWORKS_MIXED_OUTPUTS")
                ?? throw new InvalidDataException("Exact Windows mixed outputs must be retained for independent Linux validation.");
            string row = $"ntfs{major}-{minor}-{feature.Replace(':', '-')}";
            var files = new List<object>();
            foreach ((string workflow, string path) in new[] { ("input", baseline), ("saveas", output), ("commit", source) })
            {
                string destination = PortableWriterHandoff.Child(root, row + "." + workflow + ".disk");
                File.Copy(path, destination, overwrite: false);
                files.Add(new { workflow, file = Path.GetFileName(destination), length = new FileInfo(destination).Length, sha256 = Hash(destination) });
            }
            File.WriteAllText(PortableWriterHandoff.Child(root, row + ".json"), JsonSerializer.Serialize(new
            {
                schema = 1,
                status = "pending-independent-linux-validation",
                row,
                first = First,
                second = Second,
                volumeLength = VolumeLength,
                original,
                untouched,
                files,
            }));
            return;
        }
        NativePrimary(work, source, "commit");
    }

    private static void MarkDirty(string path)
    {
        long value;
        using (FileStream input = File.OpenRead(path))
        using (var fs = new NtfsFileSystem(input))
        { value = Assert.Single(fs.GetFile(3).GetStream(AttributeType.VolumeInformation, null).GetAbsoluteExtents()).Start; }
        using FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        byte[] boot = new byte[512]; file.ReadExactly(boot);
        long mft = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(boot.AsSpan(48)) * 4096);
        long mirror = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(boot.AsSpan(56)) * 4096);
        foreach (long offset in new[] { value + 10, mirror + value - mft + 10 })
        { file.Position = offset; file.WriteByte(1); file.WriteByte(0); }
    }

    private static string Compose(WorkDirectory work, string primary, string sibling)
    {
        string result = work.PathFor("mixed.disk");
        using FileStream file = File.Create(result);
        file.SetLength(Second + VolumeLength + 1024 * 1024);
        byte[] mbr = new byte[512]; "EXPANDED-NTFS-OPAQUE"u8.CopyTo(mbr.AsSpan(32));
        foreach ((int index, long start, string source) in new[] { (0, First, primary), (1, Second, sibling) })
        {
            Assert.Equal(VolumeLength, new FileInfo(source).Length);
            int entry = 446 + index * 16; mbr[entry + 4] = 7;
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(entry + 8), checked((uint)(start / 512)));
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(entry + 12), checked((uint)(VolumeLength / 512)));
            file.Position = start; using (FileStream input = File.OpenRead(source)) { input.CopyTo(file); }
            foreach (long boot in new[] { start, start + VolumeLength - 512 })
            {
                file.Position = boot + 28;
                byte[] hidden = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(hidden, checked((uint)(start / 512))); file.Write(hidden);
            }
        }
        mbr[510] = 0x55; mbr[511] = 0xaa; file.Position = 0; file.Write(mbr); file.Flush(true);
        return result;
    }

    private static string OutsidePrimary(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        foreach ((long start, long length) in new[] { (0L, First), (First + VolumeLength, file.Length - First - VolumeLength) })
        {
            file.Position = start; long remaining = length;
            while (remaining > 0) { int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); Assert.True(count > 0); hash.AppendData(buffer, 0, count); remaining -= count; }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void NativePrimary(WorkDirectory work, string disk, string phase)
    {
        string path = work.PathFor(phase + ".volume");
        using (FileStream input = File.OpenRead(disk))
        using (FileStream output = File.Create(path))
        {
            input.Position = First; byte[] buffer = new byte[65536]; long remaining = VolumeLength;
            while (remaining > 0) { int count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); Assert.True(count > 0); output.Write(buffer, 0, count); remaining -= count; }
        }
        _ = NtfsStagedWorkflowTests.RunNative("ntfsfix", "-n", path);
        _ = NtfsStagedWorkflowTests.RunNative("ntfsinfo", "-F", "/MIXED SAFE", path);
        Assert.Equal(NtfsCompatibilityFixtures.ResidentData,
            NtfsStagedWorkflowTests.RunNative("ntfscat", path, NtfsCompatibilityFixtures.ResidentPath));
    }
}
