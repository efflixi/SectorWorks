using System.Security.Cryptography;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsCompatibilityVersionTests
{
    [Theory]
    [InlineData(1, 2, "1.2")]
    [InlineData(3, 0, "3.0")]
    [InlineData(3, 1, "3.1")]
    public void SyntheticVersionLayoutsExposeVersionAndOrdinaryDataReadOnly(byte major, byte minor, string version)
    {
        using var work = new WorkDirectory();
        NtfsCompatibilityFixtures.Fixture fixture = NtfsCompatibilityFixtures.Create(work, major, minor);
        string before = Hash(fixture.Path);
        using (IImageSession session = new ImageOpener().OpenReadOnly(fixture.Path))
        {
            IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
            Assert.Equal("NTFS", fs.Name);
            Assert.Equal(version, fs.Properties["NTFS version"]);
            Assert.Contains(fs.ReadDirectory("/"), entry => entry.Name == "Legacy" && entry.IsDirectory);
            IReadOnlyList<FileEntry> children = fs.ReadDirectory("/Legacy");
            Assert.Equal(2, children.Count);
            Assert.Contains(children, entry => entry.Name == "NOTE.TXT" && entry.Length == NtfsCompatibilityFixtures.ResidentData.Length);
            Assert.Contains(children, entry => entry.Name == "PAYLOAD.BIN" && entry.Length == NtfsCompatibilityFixtures.NonresidentData.Length);
            AssertFile(fs, NtfsCompatibilityFixtures.ResidentPath, NtfsCompatibilityFixtures.ResidentData);
            AssertFile(fs, NtfsCompatibilityFixtures.NonresidentPath, NtfsCompatibilityFixtures.NonresidentData);
            Assert.True(fs.Capabilities.Browse.Available);
            Assert.True(fs.Capabilities.Extract.Available);
            Assert.False(fs.Capabilities.Modify.Available);
            Assert.False(fs.Capabilities.CommitInPlace.Available);
            Assert.Throws<NotSupportedException>(() => session.Device.Write(0, new byte[1]));
            if (version != "3.1")
            {
                Capability write = new DiskImageWriter().QualifyVolume(session, session.Volumes[0].Id);
                Assert.False(write.Available);
                Assert.False(string.IsNullOrWhiteSpace(write.Reason));
            }
        }
        Assert.Equal(before, Hash(fixture.Path));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    public void SyntheticFixturesContainStructuralVersionDifferencesNotOnlyChangedVersionBytes(byte major, byte minor)
    {
        using var work = new WorkDirectory();
        NtfsCompatibilityFixtures.Evidence layout = NtfsCompatibilityFixtures.Create(work, major, minor).Layout;
        Assert.True(layout.ActiveRecords > 20);
        Assert.True(layout.ResidentFile);
        Assert.True(layout.NonresidentFile);
        if (major == 1)
        {
            Assert.True(layout.ShortStandardInformation > 20);
            Assert.Equal(layout.ActiveRecords, layout.PerFileSecurityDescriptors);
            Assert.Equal(0, layout.ModernStandardInformation);
        }
        else
        {
            // Native-authored modern metadata may legally retain 48-byte SI.
            Assert.True(layout.ModernStandardInformation > 0);
            Assert.True(layout.ShortStandardInformation + layout.ModernStandardInformation > 20);
        }
        Assert.Equal(minor == 1 ? 0 : layout.ActiveRecords, layout.PreXpHeaders);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    [InlineData(255, 255)]
    public void UnsupportedSyntheticVolumeVersionsDoNotExposeAnOrdinaryFilesystem(byte major, byte minor)
    {
        using var work = new WorkDirectory();
        string path = NtfsCompatibilityFixtures.Create(work, major, minor).Path;
        string before = Hash(path);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Hash(path));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    public async Task LegacyLayoutReadWorkflowsUseTheSameServicesWithoutBroadeningWriterEligibility(byte major, byte minor)
    {
        using var work = new WorkDirectory();
        string path = NtfsCompatibilityFixtures.Create(work, major, minor).Path;
        string before = Hash(path);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(path));
        WorkspaceInfo info = await service.OpenAsync(path);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(NtfsCompatibilityFixtures.ResidentData),
            (await service.PreviewAsync(info.Id, volume.Id, NtfsCompatibilityFixtures.ResidentPath)).Text);
        Assert.Contains("00 01 02 03", (await service.ReadHexAsync(info.Id, volume.Id,
            NtfsCompatibilityFixtures.NonresidentPath, 0)).Text, StringComparison.Ordinal);
        SearchHit hit = Assert.Single(await service.SearchAsync(info.Id, "PAYLOAD.BIN"));
        string output = work.PathFor("legacy-extracted");
        Directory.CreateDirectory(output);
        ExtractionResult result = await service.ExtractAsync(info.Id, volume.Id, [hit.Entry], output);
        Assert.Empty(result.Failures);
        Assert.Equal(NtfsCompatibilityFixtures.NonresidentData, File.ReadAllBytes(Path.Combine(output, "PAYLOAD.BIN")));
        if (minor != 1)
        {
            Assert.Equal(WorkspaceState.ReadyReadOnly, info.State);
            Assert.False(volume.Capabilities!.Modify.Available);
            Assert.False(volume.Capabilities.SaveAs.Available);
            await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id,
                new(ImageChangeKind.Delete, volume.Id, NtfsCompatibilityFixtures.ResidentPath)));
            string rejected = work.PathFor("must-not-save.img");
            await Assert.ThrowsAsync<NotSupportedException>(() => service.SaveAsAsync(info.Id, rejected));
            Assert.False(File.Exists(rejected));
        }
        Assert.False(info.Capabilities.CommitInPlace.Available);
        Assert.Equal(before, Hash(path));
    }

    private static void AssertFile(IReadOnlyFileSystem fs, string path, byte[] expected)
    {
        using Stream stream = fs.OpenRead(path);
        Assert.False(stream.CanWrite);
        using var content = new MemoryStream();
        stream.CopyTo(content);
        Assert.Equal(expected, content.ToArray());
        Assert.Equal(SHA256.HashData(expected), SHA256.HashData(content.ToArray()));
    }
}
