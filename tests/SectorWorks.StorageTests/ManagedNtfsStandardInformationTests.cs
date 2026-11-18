using System.Buffers.Binary;
using SectorWorks.ManagedNtfs;

namespace SectorWorks.StorageTests;

public sealed class ManagedNtfsStandardInformationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlyAllocatedRecordKeepsItsPhysicalMftIdentityWithoutAModernIdentityField(bool legacy)
    {
        var record = new FileRecord(512, 1024, 300, useLegacyHeader: legacy);
        Assert.Equal(300u, record.MasterFileTableIndex);
        record.ReInitialize(512, 1024, 301, useLegacyHeader: legacy);
        Assert.Equal(301u, record.MasterFileTableIndex);
        byte[] bytes = new byte[1024];
        record.ToBytes(bytes, 0);
        Assert.Equal(legacy ? 42 : 48, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));
        var read = new FileRecord(512);
        read.FromBytes(bytes, 0);
        read.LoadedIndex = 301;
        Assert.Equal(301u, read.MasterFileTableIndex);
    }

    [Theory]
    [InlineData(0, 48)]
    [InlineData(7, 48)]
    [InlineData(0, 72)]
    [InlineData(7, 72)]
    public void StandardInformationRoundTripsAllFieldsAtTheSuppliedOffset(int offset, int length)
    {
        byte[] original = new byte[offset + length];
        for (int field = 0; field < 4; field++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(original.AsSpan(offset + 8 * field),
                new DateTime(2000 + field, 2, 3, 4, 5, 6, DateTimeKind.Utc).ToFileTimeUtc());
        }
        for (int field = 32; field < length; field++) { original[offset + field] = checked((byte)(field + 1)); }
        var info = new StandardInformation();
        Assert.Equal(length, info.ReadFrom(original, offset));
        Assert.Equal(length, info.Size);
        if (length == 72)
        {
            Assert.NotEqual(info.QuotaCharged, info.UpdateSequenceNumber);
            Assert.Equal(BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(offset + 56)), info.QuotaCharged);
            Assert.Equal(BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(offset + 64)), info.UpdateSequenceNumber);
        }
        byte[] output = Enumerable.Repeat((byte)0xcd, offset + length + 9).ToArray();
        info.WriteTo(output, offset);
        Assert.Equal(original.AsSpan(offset, length).ToArray(), output.AsSpan(offset, length).ToArray());
        Assert.All(output.AsSpan(0, offset).ToArray(), value => Assert.Equal(0xcd, value));
        Assert.All(output.AsSpan(offset + length).ToArray(), value => Assert.Equal(0xcd, value));
    }
}
