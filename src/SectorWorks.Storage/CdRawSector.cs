using System.Buffers.Binary;
using SectorWorks.Core;

namespace SectorWorks.Storage;

// ECMA-130 Mode 1 (Annexes A/B) and the ordinary XA Form 1 sector layout.
// Payloads are stored descrambled, as in ordinary 2352-byte BIN dumps.
internal static class CdRawSector
{
    internal const int RawBytes = 2352;
    internal const int UserBytes = 2048;
    private const int ParityOffset = 2076;
    private static readonly uint[] EdcTable = BuildEdcTable();
    private static readonly (byte[] Double, byte[] Inverse) Field = BuildField();

    internal static int PayloadOffset(CdTrackMode mode) => mode == CdTrackMode.Mode2Raw ? 24 : 16;

    internal static bool HasDataHeader(ReadOnlySpan<byte> sector, CdTrackMode mode) =>
        sector[0] == 0 && sector[11] == 0 && !sector[1..11].ContainsAnyExcept((byte)255)
        && (mode == CdTrackMode.Mode2Raw
            ? sector[15] == 2 && sector[16..20].SequenceEqual(sector[20..24]) && (sector[18] & 32) == 0
            : sector[15] == 1);

    internal static int Address(ReadOnlySpan<byte> sector)
    {
        int minutes = Bcd(sector[12]);
        int seconds = Bcd(sector[13]);
        int frames = Bcd(sector[14]);
        return minutes < 0 || seconds is < 0 or >= 60 || frames is < 0 or >= 75
            ? -1 : (minutes * 60 + seconds) * 75 + frames;

        static int Bcd(byte value) => (value >> 4) <= 9 && (value & 15) <= 9
            ? (value >> 4) * 10 + (value & 15) : -1;
    }

    internal static void Validate(ReadOnlySpan<byte> sector, Span<byte> calculated, CdTrackMode mode, int address)
    {
        if (!HasDataHeader(sector, mode) || address < 0 || Address(sector) != address)
        { throw new ImageFormatException("Raw CD data sector framing, address or form is not qualified for rebuild."); }
        if (mode == CdTrackMode.Mode2Raw)
        {
            // Repacking may move file contents. File/channel, streaming,
            // subheader events and coding flags must not be bound to old LBAs.
            if (sector[16] != 0 || sector[17] != 0 || sector[18] != 8 || sector[19] != 0)
            { throw new NotSupportedException("XA file/channel or nonordinary subheader flags cannot be preserved across a rebuild."); }
        }
        else if (sector[2068..ParityOffset].ContainsAnyExcept((byte)0))
        { throw new NotSupportedException("Nonzero raw Mode 1 reserved bytes are not qualified for rebuild."); }
        sector.CopyTo(calculated);
        Protect(calculated, mode);
        int checkOffset = PayloadOffset(mode) + UserBytes;
        if (!calculated[checkOffset..].SequenceEqual(sector[checkOffset..]))
        { throw new ImageFormatException("Raw CD data sector EDC/ECC does not match its stored bytes; rebuild would change error behavior."); }
    }

    internal static void Protect(Span<byte> sector, CdTrackMode mode)
    {
        bool xa = mode == CdTrackMode.Mode2Raw;
        int edcEnd = PayloadOffset(mode) + UserBytes;
        uint edc = 0;
        foreach (byte value in sector[(xa ? 16 : 0)..edcEnd])
        { edc = (edc >> 8) ^ EdcTable[(edc ^ value) & 255]; }
        BinaryPrimitives.WriteUInt32LittleEndian(sector[edcEnd..], edc);

        uint header = BinaryPrimitives.ReadUInt32LittleEndian(sector[12..]);
        if (xa) { sector.Slice(12, 4).Clear(); }
        Parity(sector[12..ParityOffset], sector[ParityOffset..], 86, 24, 2, 86);
        Parity(sector[12..2248], sector[2248..], 52, 43, 86, 88);
        if (xa) { BinaryPrimitives.WriteUInt32LittleEndian(sector[12..], header); }
    }

    private static void Parity(ReadOnlySpan<byte> interleave, Span<byte> result,
        int words, int symbols, int stride, int increment)
    {
        for (int word = 0; word < words; word++)
        {
            int index = word / 2 * stride + (word & 1);
            byte weighted = 0, sum = 0;
            for (int symbol = 0; symbol < symbols; symbol++)
            {
                byte value = interleave[index];
                index += increment;
                if (index >= interleave.Length) { index -= interleave.Length; }
                sum ^= value;
                weighted = Field.Double[weighted ^ value];
            }
            byte first = Field.Inverse[Field.Double[weighted] ^ sum];
            result[word] = first;
            result[word + words] = (byte)(first ^ sum);
        }
    }

    private static uint[] BuildEdcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            { value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xd8018001); }
            table[i] = value;
        }
        return table;
    }

    private static (byte[], byte[]) BuildField()
    {
        var doubles = new byte[256];
        var inverse = new byte[256];
        for (int i = 0; i < doubles.Length; i++)
        {
            int value = (i << 1) ^ ((i & 128) == 0 ? 0 : 0x11d);
            doubles[i] = (byte)value;
            inverse[i ^ value] = (byte)i;
        }
        return (doubles, inverse);
    }
}
