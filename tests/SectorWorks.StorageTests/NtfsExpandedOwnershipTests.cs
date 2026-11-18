using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;
using AttributeType = SectorWorks.ManagedNtfs.AttributeType;
using NtfsFileSystem = SectorWorks.ManagedNtfs.NtfsFileSystem;

namespace SectorWorks.StorageTests;

public sealed class NtfsExpandedOwnershipTests
{
    [Theory]
    [InlineData(1, 2, 0)]
    [InlineData(3, 0, 8)]
    [InlineData(3, 1, 16)]
    [InlineData(3, 1, 24)]
    public void UnsupportedRawStandardInformationTimesNeverBecomeWritable(byte major, byte minor, int timeOffset)
    {
        using var work = new WorkDirectory();
        string path = NtfsCompatibilityFixtures.Create(work, major, minor, forWriterCandidate: true).Path;
        long position;
        using (FileStream file = File.OpenRead(path))
        using (var fs = new NtfsFileSystem(file))
        {
            long id = fs.GetFileId(@"Legacy\NOTE.TXT") & 0x0000ffffffffffffL;
            position = Assert.Single(fs.GetFile(id).GetStream(AttributeType.StandardInformation, null).GetAbsoluteExtents()).Start;
        }
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.Write))
        {
            file.Position = position + timeOffset;
            file.Write(BitConverter.GetBytes(ulong.MaxValue));
        }
        string original = Hash(path);
        using (var device = new FileBlockDevice(path))
        {
            ImageFormatException error = Assert.Throws<ImageFormatException>(() => NtfsWriteValidation.CaptureLegacyCandidate(device));
            Assert.Contains("timestamp", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(original, Hash(path));
    }

    [Theory]
    [InlineData(1, 2, 36)]
    [InlineData(3, 0, 56)]
    [InlineData(3, 1, 64)]
    public void RawOpaqueStandardInformationChangesCannotPassOutputValidation(byte major, byte minor, int fieldOffset)
    {
        using var work = new WorkDirectory();
        string source = NtfsCompatibilityFixtures.Create(work, major, minor, forWriterCandidate: true).Path;
        string output = work.PathFor("modified.img");
        File.Copy(source, output);
        long position;
        using (FileStream file = File.OpenRead(output))
        using (var fs = new NtfsFileSystem(file))
        {
            long id = fs.GetFileId(@"Legacy\NOTE.TXT") & 0x0000ffffffffffffL;
            position = Assert.Single(fs.GetFile(id).GetStream(AttributeType.StandardInformation, null).GetAbsoluteExtents()).Start;
        }
        using (FileStream file = File.Open(output, FileMode.Open, FileAccess.Write))
        { file.Position = position + fieldOffset; file.WriteByte(0x7b); }
        using IImageSession original = new ImageOpener().OpenReadOnly(source);
        using IImageSession altered = new ImageOpener().OpenReadOnly(output);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() =>
            new NtfsImageWriter(includeCandidates: true).ValidatePreservedMetadata(original, altered, [], default));
        Assert.Contains("standard-information bytes", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 2, false)]
    [InlineData(3, 0, false)]
    [InlineData(3, 1, false)]
    [InlineData(1, 2, true)]
    [InlineData(3, 0, true)]
    [InlineData(3, 1, true)]
    public void ReadableContentDoesNotQualifyOrphanOrNonzeroLinkCountExtensions(byte major, byte minor, bool nonzeroLinks)
    {
        using var work = new WorkDirectory();
        string path = NtfsCompatibilityFixtures.Create(work, major, minor, forWriterCandidate: true).Path;
        var fixture = new NtfsAttributeListTests.ExtensionFixture(work, false, false, false, existingPath: path);
        if (nonzeroLinks) { fixture.Corrupt("nonzero-link-count"); }
        else { fixture.LeaveUnreferencedExtension(); }
        string original = Hash(path);
        using (IImageSession image = new ImageOpener().OpenReadOnly(path))
        {
            using Stream readable = Assert.Single(image.Volumes).FileSystem.OpenRead("/LARGE.BIN");
            Assert.Equal(8192, readable.Length);
            Assert.False(new DiskImageWriter(includeNtfsCandidates: true).Qualify(image).Available);
        }
        Assert.Equal(original, Hash(path));
    }

    [Theory]
    [InlineData("quota")]
    [InlineData("modern-si")]
    public void Ntfs12CandidateRefusesUnqualifiedQuotaAndStandardInformationLayouts(string feature)
    {
        using var work = new WorkDirectory();
        string path = NtfsCompatibilityFixtures.Create(work, 1, 2, forWriterCandidate: true).Path;
        using (FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        using (var fs = new NtfsFileSystem(file))
        {
            if (feature == "quota")
            {
                using Stream data = fs.OpenFile("$Quota", FileMode.Open, FileAccess.Write);
                data.WriteByte(1);
            }
            else
            {
                long id = fs.GetFileId(@"Legacy\NOTE.TXT") & 0x0000ffffffffffffL;
                SectorWorks.ManagedNtfs.File target = fs.GetFile(id);
                using (Stream data = target.GetStream(AttributeType.StandardInformation, null).Open(FileAccess.Write))
                { data.SetLength(72); data.Position = 48; data.Write(new byte[24]); }
                target.UpdateRecordInMft();
            }
        }
        string original = Hash(path);
        using (var device = new FileBlockDevice(path))
        {
            ImageFormatException error = Assert.Throws<ImageFormatException>(() => NtfsWriteValidation.CaptureLegacyCandidate(device));
            Assert.Contains(feature == "quota" ? "quota" : "48-byte", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(original, Hash(path));
    }
}
