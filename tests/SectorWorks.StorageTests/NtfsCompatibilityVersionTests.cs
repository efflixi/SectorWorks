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
            Assert.Equal(3, children.Count);
            Assert.Contains(children, entry => entry.Name == "NOTE.TXT" && entry.Length == NtfsCompatibilityFixtures.ResidentData.Length);
            Assert.Contains(children, entry => entry.Name == "PAYLOAD.BIN" && entry.Length == NtfsCompatibilityFixtures.NonresidentData.Length);
            AssertFile(fs, NtfsCompatibilityFixtures.ResidentPath, NtfsCompatibilityFixtures.ResidentData);
            AssertFile(fs, NtfsCompatibilityFixtures.NonresidentPath, NtfsCompatibilityFixtures.NonresidentData);
            AssertFile(fs, NtfsCompatibilityFixtures.UnicodePath, NtfsCompatibilityFixtures.UnicodeData);
            FileEntry unicode = Assert.Single(children, entry => entry.Name == "Unicode-λ.txt");
            Assert.Equal(NtfsCompatibilityFixtures.ModifiedUtc, unicode.ModifiedUtc);
            Assert.Equal(DateTimeKind.Utc, unicode.ModifiedUtc!.Value.Kind);
            Assert.True(fs.Capabilities.Browse.Available);
            Assert.True(fs.Capabilities.Extract.Available);
            Assert.False(fs.Capabilities.Modify.Available);
            Assert.False(fs.Capabilities.CommitInPlace.Available);
            Assert.Throws<NotSupportedException>(() => session.Device.Write(0, new byte[1]));
            if (version == "1.2")
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
        Assert.Equal(NtfsCompatibilityFixtures.ModifiedUtc, layout.UnicodeModifiedUtc);
        Assert.Equal(major == 1 ? 48 : 72, layout.UnicodeStandardInformationLength);
        if (major == 1)
        {
            Assert.True(layout.ShortStandardInformation > 20);
            Assert.Equal(layout.ActiveRecords, layout.PerFileSecurityDescriptors);
            Assert.Equal(0, layout.ModernStandardInformation);
            Assert.Equal(0u, layout.UnicodeSecurityId);
            Assert.Equal("4597B39D4646C2E2120FDDFED1F7A92A90CB223B96D0572AA69FD7C0B2FE2D56",
                layout.UnicodeSecurityHash);
        }
        else
        {
            // Native-authored modern metadata may legally retain 48-byte SI.
            Assert.True(layout.ModernStandardInformation > 0);
            Assert.True(layout.ShortStandardInformation + layout.ModernStandardInformation > 20);
            Assert.NotEqual(0u, layout.UnicodeSecurityId);
            Assert.Null(layout.UnicodeSecurityHash);
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
    public async Task LegacyLayoutReadWorkflowsRespectTheIndependentQualifiedWriterSubset(byte major, byte minor)
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
        if (major == 1)
        {
            Assert.Equal(WorkspaceState.ReadyReadOnly, info.State);
            Assert.False(volume.Capabilities!.Modify.Available);
            Assert.False(volume.Capabilities.SaveAs.Available);
            await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id,
                new(ImageChangeKind.Delete, volume.Id, NtfsCompatibilityFixtures.ResidentPath)));
            string rejected = work.PathFor("must-not-save.img");
            await Assert.ThrowsAsync<NotSupportedException>(() => service.SaveAsAsync(info.Id, rejected));
            Assert.False(File.Exists(rejected));
            // This retained NTFS 1.2 reader fixture has deliberately unqualified
            // allocation/modern metadata. The separate writer fixture is required.
            Assert.False(info.Capabilities.CommitInPlace.Available);
        }
        else
        {
            // Both this structural 3.0 layout and the existing 3.1 ordinary
            // subset now have separate staged/Save As and commit evidence.
            Assert.True(info.Capabilities.CommitInPlace.Available, info.Capabilities.CommitInPlace.Reason);
        }
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
