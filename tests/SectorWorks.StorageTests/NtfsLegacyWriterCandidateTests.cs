using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;
using NtfsFileSystem = SectorWorks.ManagedNtfs.NtfsFileSystem;
using ShortFileNameOption = SectorWorks.ManagedNtfs.ShortFileNameOption;

namespace SectorWorks.StorageTests;

// Candidate investigation only. These checks do not enable a product capability,
// certify authentic historical authoring, or substitute for the disk matrix.
public sealed class NtfsLegacyWriterCandidateTests
{
    public static IEnumerable<object[]> ExtensionRows()
    {
        foreach ((byte major, byte minor) in new (byte, byte)[] { (1, 2), (3, 0), (3, 1) })
        {
            foreach (bool nonresident in new[] { false, true })
            {
                foreach (string feature in new[] { "moved-data", "split-data", "moved-index" })
                { yield return [major, minor, nonresident, feature]; }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ExtensionRows))]
    public void AttributeListCandidateAccountsForExtensionsAndPreservesAllocationThroughContentTransitions(
        byte major, byte minor, bool nonresident, string feature)
    {
        using var work = new WorkDirectory();
        string path = NtfsCompatibilityFixtures.Create(work, major, minor, forWriterCandidate: true).Path;
        _ = new NtfsAttributeListTests.ExtensionFixture(work, nonresident,
            feature == "moved-index", feature == "split-data", existingPath: path);
        using (var device = new FileBlockDevice(path))
        { _ = NtfsWriteValidation.CaptureLegacyCandidate(device); }
        byte[] payload = Enumerable.Range(0, 17000).Select(i => (byte)(i % 251)).ToArray();
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        using (var fs = new NtfsFileSystem(file))
        {
            fs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;
            fs.NtfsOptions.CreateObjectIds = false;
            fs.CreateDirectory("New directory");
            using (Stream added = fs.OpenFile(@"New directory\Added.txt", FileMode.Create, FileAccess.Write))
            { added.Write(payload); }
            using (Stream shrunk = fs.OpenFile("LARGE.BIN", FileMode.Create, FileAccess.Write))
            { shrunk.Write("resident transition\n"u8); }
        }
        using (var device = new FileBlockDevice(path))
        { _ = NtfsWriteValidation.CaptureLegacyCandidate(device); }
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        using (var fs = new NtfsFileSystem(file))
        {
            fs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;
            fs.NtfsOptions.CreateObjectIds = false;
            using (Stream grown = fs.OpenFile("LARGE.BIN", FileMode.Create, FileAccess.Write)) { grown.Write(payload); }
            fs.MoveFile("LARGE.BIN", "Renamed.bin");
            fs.DeleteFile(@"New directory\Added.txt");
            fs.DeleteDirectory("New directory");
        }
        using (var device = new FileBlockDevice(path))
        {
            _ = NtfsWriteValidation.CaptureLegacyCandidate(device);
            if (major != 3 || minor != 1)
            { Assert.Throws<ImageFormatException>(() => NtfsWriteValidation.Capture(device)); }
        }
        using IImageSession reopened = new ImageOpener().OpenReadOnly(path);
        using Stream content = Assert.Single(reopened.Volumes).FileSystem.OpenRead("/Renamed.bin");
        using var bytes = new MemoryStream();
        content.CopyTo(bytes);
        Assert.Equal(payload, bytes.ToArray());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 0)]
    public void StructuralLegacyCandidatePreservesStrictAllocationAndSecurityThroughFiveOperations(byte major, byte minor)
    {
        using var work = new WorkDirectory();
        string path = NtfsCompatibilityFixtures.Create(work, major, minor, forWriterCandidate: true).Path;
        using (var device = new FileBlockDevice(path))
        {
            Assert.NotEmpty(NtfsWriteValidation.CaptureLegacyCandidate(device).SecurityDescriptors);
            Assert.Throws<ImageFormatException>(() => NtfsWriteValidation.Capture(device));
        }
        byte[] payload = Enumerable.Range(0, 17000).Select(i => (byte)(i % 251)).ToArray();
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        using (var fs = new NtfsFileSystem(file))
        {
            fs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;
            fs.NtfsOptions.CreateObjectIds = false;
            fs.CreateDirectory(@"Legacy\New λ directory");
            using (Stream added = fs.OpenFile(@"Legacy\New λ directory\Added.txt", FileMode.Create, FileAccess.Write))
            { added.Write("new file\n"u8); }
            using (Stream replaced = fs.OpenFile(@"Legacy\NOTE.TXT", FileMode.Create, FileAccess.Write))
            { replaced.Write(payload); }
            fs.MoveFile(@"Legacy\PAYLOAD.BIN", @"Legacy\Renamed.bin");
            fs.DeleteFile(@"Legacy\Unicode-λ.txt");
        }
        using (var device = new FileBlockDevice(path))
        {
            Assert.NotEmpty(NtfsWriteValidation.CaptureLegacyCandidate(device).SecurityDescriptors);
            Assert.Throws<ImageFormatException>(() => NtfsWriteValidation.Capture(device));
        }
        using IImageSession reopened = new ImageOpener().OpenReadOnly(path);
        IReadOnlyFileSystem reader = Assert.Single(reopened.Volumes).FileSystem;
        Assert.Equal($"{major}.{minor}", reader.Properties["NTFS version"]);
        using Stream content = reader.OpenRead(NtfsCompatibilityFixtures.ResidentPath);
        using var bytes = new MemoryStream();
        content.CopyTo(bytes);
        Assert.Equal(payload, bytes.ToArray());
        Assert.Contains(reader.ReadDirectory("/Legacy"), entry => entry.Name == "Renamed.bin");
        Assert.DoesNotContain(reader.ReadDirectory("/Legacy"), entry => entry.Name == "Unicode-λ.txt");
        Assert.Single(reader.ReadDirectory("/Legacy/New λ directory"));
    }
}
