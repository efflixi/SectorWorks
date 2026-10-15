using SectorWorks.ManagedNtfs;

namespace SectorWorks.StorageTests;

public sealed class ManagedNtfsRunTests
{
    [Theory]
    [InlineData("110101", 1, 1, false)]
    [InlineData("1101FF", 1, -1, false)]
    [InlineData("110180", 1, -128, false)]
    [InlineData("21010080", 1, -32768, false)]
    [InlineData("118001", 128, 1, false)]
    [InlineData("12800001", 128, 1, false)]
    [InlineData("21800001", 128, 256, false)]
    [InlineData("0101", 1, 0, true)]
    [InlineData("0180", 128, 0, true)]
    [InlineData("81010000000000000080", 1, long.MinValue, false)]
    public void MappingPairsDecodeUnsignedLengthsAndSignedLcnDeltas(string hex, long length, long offset, bool sparse)
    {
        byte[] bytes = Convert.FromHexString(hex);
        var run = new DataRun();
        Assert.Equal(bytes.Length, run.Read(bytes, 0));
        Assert.Equal(length, run.RunLength);
        Assert.Equal(offset, run.RunOffset);
        Assert.Equal(sparse, run.IsSparse);
        byte[] encoded = new byte[32];
        int count = run.Write(encoded, 3);
        var reopened = new DataRun();
        Assert.Equal(count, reopened.Read(encoded, 3));
        Assert.Equal(length, reopened.RunLength);
        Assert.Equal(offset, reopened.RunOffset);
        Assert.Equal(sparse, reopened.IsSparse);
    }

    [Fact]
    public void MappingPairsTerminatorConsumesExactlyOneByteEvenInAnEnclosingRecord()
    {
        var run = new DataRun();
        Assert.Equal(1, run.Read([0x91, 0x00, 0x11, 0x01, 0x01], 1));
        Assert.Equal(0, run.RunLength);
        Assert.Equal(0, run.RunOffset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("11")]
    [InlineData("1101")]
    [InlineData("110001")]
    [InlineData("FF")]
    [InlineData("810100000000000000")]
    public void MappingPairsRejectInvalidWidthsZeroLengthsAndTruncation(string hex)
    {
        var run = new DataRun();
        Assert.Throws<IOException>(() => run.Read(Convert.FromHexString(hex), 0));
    }

    [Fact]
    public void MappingPairsCannotNarrowAnOversizedUnsignedLengthIntoANegativeCount()
    {
        var run = new DataRun();
        Assert.Throws<OverflowException>(() => run.Read(Convert.FromHexString("18000000000000008001"), 0));
    }
}
