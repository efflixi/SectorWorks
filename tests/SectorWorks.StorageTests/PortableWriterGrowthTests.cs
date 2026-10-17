using System.Diagnostics;
using System.Security.Cryptography;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class PortableWriterGrowthTests
{
    private const int PayloadLength = 20 * 1024 * 1024 + 17;

    public static IEnumerable<object[]> Rows() => DiskWriterTests.FatRows()
        .Where(row => ((string)row[0]).EndsWith("dynamic", StringComparison.Ordinal) && (string)row[2] == "fat32");

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task DynamicContainersGrowAndSecondCommitUsesTheRefreshedBaseline(
        string container, string layout, string fat, int physical)
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, container, layout, fat,
            physical: physical, withLongNames: true);
        string payload = work.PathFor("growth-payload.bin");
        using (var file = File.Create(payload))
        {
            byte[] buffer = new byte[65536];
            for (int offset = 0; offset < PayloadLength;)
            {
                int count = Math.Min(buffer.Length, PayloadLength - offset);
                for (int index = 0; index < count; index++) { buffer[index] = (byte)((offset + index) % 251); }
                file.Write(buffer, 0, count);
                offset += count;
            }
        }
        string expected = PortableWriterHandoff.Hash(payload);
        long before = new FileInfo(source).Length;
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(source));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.True(volume.Capabilities!.CommitInPlace.Available, volume.Capabilities.CommitInPlace.Reason);
        long previous = before;
        for (int sequence = 1; sequence <= 2; sequence++)
        {
            await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile,
                volume.Id, $"/GROWTH{sequence}.BIN", payload));
            CommitResult result = await service.CommitAsync(workspace.Id);
            Assert.True(result.FilesValidated > 0);
            long actual = new FileInfo(source).Length;
            Assert.True(actual > previous, $"Commit {sequence} must physically grow {container}: {previous} -> {actual}");
            Console.WriteLine($"PHYSICAL_GROWTH {container} {layout} p{physical} commit{sequence}: {previous} -> {actual}");
            EditingSnapshot editing = await service.GetEditingSnapshotAsync(workspace.Id);
            Assert.False(editing.IsModified);
            Assert.Empty(editing.Changes);
            using (IImageSession reopened = new ImageOpener().OpenReadOnly(source))
            {
                Assert.Equal(workspace.Metadata.VirtualLength, reopened.Device.Length);
                for (int index = 1; index <= sequence; index++)
                {
                    using Stream contents = reopened.Volumes[0].FileSystem.OpenRead($"/GROWTH{index}.BIN");
                    Assert.Equal(PayloadLength, contents.Length);
                    Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(contents)));
                }
            }
            string workflow = sequence == 1 ? "growth-first" : "growth-second";
            if (!PortableWriterHandoff.Queue(source, source,
                PortableWriterHandoff.Row(container, layout, fat, physical, true), workflow))
            {
                string raw = work.PathFor($"growth-{sequence}.raw");
                DiskWriterFixtures.Run("qemu-img", "convert", "-q", "-f",
                    container.StartsWith("vhdx", StringComparison.Ordinal) ? "vhdx" : "vpc", "-O", "raw", source, raw);
                DiskWriterFixtures.CheckFat(work, raw);
                for (int index = 1; index <= sequence; index++)
                {
                    AssertNativeContent(work.PathFor("native-check.img"), $"::/GROWTH{index}.BIN", expected);
                }
            }
            Assert.Empty(CommitRecovery.Discover(Path.GetDirectoryName(source)!));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(source)!, ".sectorworks-commit-*"));
            previous = actual;
        }
        Assert.True(previous > before);
    }

    private static void AssertNativeContent(string volume, string path, string expected)
    {
        var start = new ProcessStartInfo("mtype") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-i", volume, path }) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start) ?? throw new IOException("Could not start mtype.");
        Task<byte[]> hash = SHA256.HashDataAsync(process.StandardOutput.BaseStream).AsTask();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(true); throw new IOException("mtype timed out."); }
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        Assert.Equal(expected, Convert.ToHexString(hash.GetAwaiter().GetResult()));
    }
}
