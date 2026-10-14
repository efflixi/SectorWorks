using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class LegacyFatTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyFloppyWithoutExtendedBpbUsesRootLabelAndRemainsReadOnly(bool signed)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("legacy.img");
        byte[] image = CreateLegacyFloppy();
        if (signed)
        {
            image[510] = 0x55;
            image[511] = 0xaa;
        }
        File.WriteAllBytes(path, image);
        byte[] before = SHA256.HashData(image);
        using (IImageSession session = new ImageOpener().OpenReadOnly(path))
        {
            Assert.Empty(session.Partitions);
            IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
            Assert.Equal("FAT12", fs.Name);
            Assert.Equal("LEGACY", fs.VolumeLabel);
            Assert.Equal("HELLO.TXT", Assert.Single(fs.ReadDirectory("/")).Name);
            using var text = new StreamReader(fs.OpenRead("/HELLO.TXT"));
            Assert.Equal("legacy FAT test\n", text.ReadToEnd());
            Assert.Equal(!signed, session.Warnings.Any(warning => warning.Contains("without a 0x55AA", StringComparison.Ordinal)));
            Assert.False(fs.Capabilities.Modify.Available);
            Assert.False(new RawFatImageWriter().Qualify(session).Available);
            Assert.Throws<NotSupportedException>(() => session.Device.Write(0, new byte[1]));
        }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData("modern-jump")]
    [InlineData("jump-into-bpb")]
    [InlineData("invalid-geometry")]
    [InlineData("different-fat")]
    [InlineData("invalid-reserved-fat")]
    [InlineData("truncated")]
    public void UnsignedFloppyDoesNotBypassStructuralValidation(string damage)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("invalid-legacy.img");
        byte[] image = CreateLegacyFloppy();
        switch (damage)
        {
            case "modern-jump": image[1] = 0x3c; break;
            case "jump-into-bpb": image[1] = 0x16; break;
            case "invalid-geometry": Write16(image, 24, 10); break;
            case "different-fat": image[2048 + 10] = 1; break;
            case "invalid-reserved-fat": image[512] = image[2048] = 0; break;
            case "truncated": image = image[..^512]; break;
        }
        File.WriteAllBytes(path, image);
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
    }

    private static byte[] CreateLegacyFloppy()
    {
        var image = new byte[720 * 1024];
        image[0] = 0xeb;
        image[1] = 0x1c;
        image[2] = 0x90;
        Encoding.ASCII.GetBytes("TESTDOS ").CopyTo(image, 3);
        Write16(image, 11, 512);
        image[13] = 2;
        Write16(image, 14, 1);
        image[16] = 2;
        Write16(image, 17, 112);
        Write16(image, 19, 1440);
        image[21] = 0xf9;
        Write16(image, 22, 3);
        Write16(image, 24, 9);
        Write16(image, 26, 2);
        image.AsSpan(30, 60).Fill(0x7f);
        image[512] = 0xf9;
        image[513] = image[514] = image[515] = 0xff;
        image[516] = 0x0f;
        image.AsSpan(512, 1536).CopyTo(image.AsSpan(2048, 1536));
        const int root = 7 * 512;
        Encoding.ASCII.GetBytes("HELLO   TXT").CopyTo(image, root);
        image[root + 11] = 0x20;
        Write16(image, root + 26, 2);
        byte[] contents = Encoding.UTF8.GetBytes("legacy FAT test\n");
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(root + 28), (uint)contents.Length);
        contents.CopyTo(image, 14 * 512);
        Encoding.ASCII.GetBytes("LEGACY     ").CopyTo(image, root + 32);
        image[root + 32 + 11] = 0x28;
        return image;
    }

    private static void Write16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
}
