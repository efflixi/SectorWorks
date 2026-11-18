using System.Buffers.Binary;
using System.Text;
using SectorWorks.ManagedNtfs;
using SectorWorks.NtfsSecurity;
using static SectorWorks.StorageTests.StorageTests;
using File = System.IO.File;

namespace SectorWorks.StorageTests;

// Synthetic layout probes, NOT images authored by NT4/Windows 2000/XP.
// The baseline is the repository's lawful mkntfs/ntfscp 32 MiB golden, expanded
// into WorkDirectory. Only this disposable expansion is modified. Native tools
// are not required, and the original signed DiscUtils reader remains unchanged.
internal static class NtfsCompatibilityFixtures
{
    internal const string ResidentPath = "/Legacy/NOTE.TXT";
    internal const string NonresidentPath = "/Legacy/PAYLOAD.BIN";
    internal const string UnicodePath = "/Legacy/Unicode-λ.txt";
    internal static readonly DateTime ModifiedUtc = new(2000, 2, 29, 12, 34, 56, DateTimeKind.Utc);
    internal static readonly byte[] UnicodeData = "SectorWorks generated Unicode λ metadata probe.\n"u8.ToArray();
    internal static readonly byte[] ResidentData = "Synthetic legacy NTFS layout probe.\n"u8.ToArray();
    internal static readonly byte[] NonresidentData = Enumerable.Range(0, 16384)
        .Select(index => checked((byte)(index % 251))).ToArray();

    internal sealed record Evidence(int ActiveRecords, int ShortStandardInformation,
        int PerFileSecurityDescriptors, int PreXpHeaders, int ModernStandardInformation,
        bool ResidentFile, bool NonresidentFile, int UnicodeStandardInformationLength,
        uint UnicodeSecurityId, string? UnicodeSecurityHash, DateTime UnicodeModifiedUtc);
    internal sealed record Fixture(string Path, Evidence Layout);

    internal static Fixture Create(WorkDirectory work, byte major, byte minor, bool forWriterCandidate = false)
    {
        string path = work.Golden("ntfs.img");
        // The isolated managed backend can author ordinary children portably;
        // its formatter is deliberately excluded and still throws. These calls
        // author test media only; they do not qualify any production writer.
        using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        using (var fs = new NtfsFileSystem(stream))
        {
            fs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;
            fs.NtfsOptions.CreateObjectIds = false;
            fs.CreateDirectory("Legacy");
            using (Stream small = fs.OpenFile(@"Legacy\NOTE.TXT", FileMode.Create, FileAccess.Write))
            {
                small.Write(ResidentData);
            }
            using (Stream large = fs.OpenFile(@"Legacy\PAYLOAD.BIN", FileMode.Create, FileAccess.Write))
            {
                large.Write(NonresidentData);
            }
            using (Stream unicode = fs.OpenFile(@"Legacy\Unicode-λ.txt", FileMode.Create, FileAccess.Write))
            {
                unicode.Write(UnicodeData);
            }
            fs.SetLastWriteTimeUtc(@"Legacy\Unicode-λ.txt", ModifiedUtc);
        }

        byte[] bytes = File.ReadAllBytes(path);
        int cluster = U16(bytes, 11) * bytes[13];
        int recordSize = bytes[64] >= 128 ? 1 << -unchecked((sbyte)bytes[64]) : bytes[64] * cluster;
        int mft = checked((int)U64(bytes, 48) * cluster);
        byte[] first = Decode(bytes.AsSpan(mft, recordSize).ToArray());
        byte[] data = Attributes(first).Single(attribute => U32(attribute, 0) == 0x80 && attribute[9] == 0);
        long initialized = checked((long)U64(data, 56));
        List<(long Offset, long Length)> runs = DecodeRuns(data, cluster);
        var releasedSecurity = new List<(long Offset, long Length)>();
        for (int logical = 0; logical < initialized; logical += recordSize)
        {
            int physical = PhysicalOffset(runs, logical, recordSize);
            byte[] record = Decode(bytes.AsSpan(physical, recordSize).ToArray());
            if ((U16(record, 22) & 1) == 0) { continue; }
            List<byte[]> attributes = Attributes(record);
            bool nt4 = major == 1 && minor == 2;
            bool preXp = nt4 || (major == 3 && minor == 0);
            for (int index = 0; index < attributes.Count; index++)
            {
                byte[] attribute = attributes[index];
                uint type = U32(attribute, 0);
                if (type == 0x10)
                {
                    int value = U16(attribute, 20);
                    if (nt4)
                    {
                        byte[] shortened = attribute.AsSpan(0, value + 48).ToArray();
                        W32(shortened, 4, checked((uint)shortened.Length));
                        W32(shortened, 16, 48);
                        attributes[index] = shortened;
                    }
                }
                if (logical == 3 * recordSize && type == 0x70)
                {
                    int value = U16(attribute, 20);
                    attribute[value + 8] = major;
                    attribute[value + 9] = minor;
                }
            }
            if (nt4)
            {
                // NTFS 1.2 predates shared security IDs. Use a bounded valid
                // self-relative per-file descriptor, including for metadata.
                // Reader fixtures historically retain allocation from a replaced
                // nonresident root descriptor. A separate writer candidate must
                // author a consistent bitmap, never relax the product validator
                // to accept that intentionally unqualified reader fixture.
                if (forWriterCandidate)
                {
                    foreach (byte[] old in attributes.Where(a => U32(a, 0) == 0x50 && a[8] == 1))
                    { releasedSecurity.AddRange(DecodeRuns(old, cluster)); }
                }
                attributes.RemoveAll(attribute => U32(attribute, 0) is 0x40 or 0x50);
                ushort id = U16(record, 40);
                attributes.Add(ResidentAttribute(0x50, id, SecurityDescriptor()));
                W16(record, 40, checked((ushort)(id + 1)));
            }
            attributes = attributes.OrderBy(attribute => U32(attribute, 0)).ToList();
            int firstAttribute = preXp ? 48 : 56;
            record.AsSpan(42).Clear();
            W16(record, 4, checked((ushort)(preXp ? 42 : 48)));
            W16(record, 6, checked((ushort)(recordSize / 512 + 1)));
            W16(record, 20, checked((ushort)firstAttribute));
            if (!preXp) { W32(record, 44, checked((uint)(logical / recordSize))); }
            int position = firstAttribute;
            foreach (byte[] attribute in attributes)
            {
                if (position + attribute.Length + 8 > record.Length)
                {
                    throw new InvalidDataException("Synthetic legacy fixture record exceeds its bounded MFT slot.");
                }
                attribute.CopyTo(record, position);
                position += attribute.Length;
            }
            W32(record, position, uint.MaxValue);
            W32(record, 24, checked((uint)(position + 8)));
            Encode(record);
            record.CopyTo(bytes, physical);
        }
        if (releasedSecurity.Count != 0)
        {
            byte[] bitmapRecord = Decode(bytes.AsSpan(PhysicalOffset(runs, 6 * recordSize, recordSize), recordSize).ToArray());
            byte[] bitmap = Attributes(bitmapRecord).Single(a => U32(a, 0) == 0x80 && a[9] == 0);
            List<(long Offset, long Length)> bitmapRuns = DecodeRuns(bitmap, cluster);
            foreach ((long start, long length) in releasedSecurity)
            {
                for (long lcn = start / cluster; lcn < (start + length) / cluster; lcn++)
                {
                    int bitByte = PhysicalOffset(bitmapRuns, lcn / 8, 1);
                    bytes[bitByte] &= unchecked((byte)~(1 << (int)(lcn % 8)));
                }
            }
        }
        int mirror = checked((int)U64(bytes, 56) * cluster);
        int mirrorLength = Math.Max(4 * recordSize, cluster);
        for (int offset = 0; offset < mirrorLength; offset += recordSize)
        {
            bytes.AsSpan(PhysicalOffset(runs, offset, recordSize), recordSize).CopyTo(bytes.AsSpan(mirror + offset));
        }
        File.WriteAllBytes(path, bytes);
        if (forWriterCandidate && major == 1 && minor == 2)
        {
            // A writer fixture must also model the pre-3.0 system namespace,
            // rather than relabeling modern $Secure/$Extend as NTFS 1.2.
            // This bounded synthetic subset has an empty $Quota (no quota
            // tracking), per-file security and no modern extended metadata.
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
            using (var fs = new NtfsFileSystem(stream))
            {
                fs.NtfsOptions.CreateObjectIds = false;
                fs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;
                fs.MoveFile("$Secure", "$Quota");
                SectorWorks.ManagedNtfs.File quota = fs.GetFile(9);
                foreach (NtfsStream old in quota.AllStreams.Where(s => s.AttributeType is AttributeType.Data
                    or AttributeType.IndexRoot or AttributeType.IndexAllocation or AttributeType.Bitmap).ToArray())
                {
                    using (Stream dataStream = old.Open(FileAccess.Write)) { dataStream.SetLength(0); }
                    quota.RemoveStream(old);
                }
                quota.CreateStream(AttributeType.Data, null);
                quota.UpdateRecordInMft();
                foreach (string name in new[] { "$ObjId", "$Quota", "$Reparse" }) { fs.DeleteFile("$Extend\\" + name); }
                fs.DeleteDirectory("$Extend");
            }
            byte[] authored = File.ReadAllBytes(path);
            for (int id = 12; id <= 15; id++)
            {
                int physical = PhysicalOffset(runs, id * recordSize, recordSize);
                byte[] reserved = Decode(authored.AsSpan(physical, recordSize).ToArray());
                int start = U16(reserved, 20);
                reserved.AsSpan(start).Clear();
                W16(reserved, 18, 0);
                W16(reserved, 40, 0);
                W32(reserved, start, uint.MaxValue);
                W32(reserved, 24, checked((uint)(start + 8)));
                Encode(reserved);
                reserved.CopyTo(authored, physical);
            }
            File.WriteAllBytes(path, authored);
        }
        return new(path, Inspect(path, recordSize, runs, initialized));
    }

    private static Evidence Inspect(string path, int recordSize, List<(long Offset, long Length)> runs, long initialized)
    {
        // Observe the serialized bytes afresh, independently of authoring counters.
        byte[] bytes = File.ReadAllBytes(path);
        int active = 0, shortSi = 0, descriptors = 0, oldHeaders = 0, modernSi = 0;
        bool resident = false, nonresident = false;
        int unicodeSiLength = 0;
        uint unicodeSecurityId = 0;
        string? unicodeSecurityHash = null;
        DateTime unicodeModified = default;
        for (int offset = 0; offset < initialized; offset += recordSize)
        {
            byte[] record = Decode(bytes.AsSpan(PhysicalOffset(runs, offset, recordSize), recordSize).ToArray());
            if ((U16(record, 22) & 1) == 0) { continue; }
            active++;
            if (U16(record, 4) == 42 && U16(record, 20) == 48) { oldHeaders++; }
            List<byte[]> attributes = Attributes(record);
            foreach (byte[] attribute in attributes)
            {
                if (U32(attribute, 0) == 0x10)
                {
                    if (U32(attribute, 16) == 48) { shortSi++; }
                    if (U32(attribute, 16) == 72) { modernSi++; }
                }
                if (U32(attribute, 0) == 0x50)
                {
                    if (attribute[8] == 0)
                    {
                        int value = U16(attribute, 20);
                        _ = PortableSecurityDescriptor.Parse(attribute.AsSpan(value, checked((int)U32(attribute, 16))));
                    }
                    descriptors++;
                }
            }
            foreach (byte[] name in attributes.Where(attribute => U32(attribute, 0) == 0x30))
            {
                int value = U16(name, 20);
                string filename = Encoding.Unicode.GetString(name, value + 66, name[value + 64] * 2);
                byte[]? content = attributes.SingleOrDefault(attribute => U32(attribute, 0) == 0x80 && attribute[9] == 0);
                if (filename == "NOTE.TXT") { resident = content is not null && content[8] == 0; }
                if (filename == "PAYLOAD.BIN") { nonresident = content is not null && content[8] == 1; }
                if (filename == "Unicode-λ.txt")
                {
                    byte[] si = attributes.Single(attribute => U32(attribute, 0) == 0x10);
                    int valueOffset = U16(si, 20);
                    unicodeSiLength = checked((int)U32(si, 16));
                    unicodeModified = DateTime.FromFileTimeUtc(checked((long)U64(si, valueOffset + 8)));
                    unicodeSecurityId = unicodeSiLength >= 72 ? U32(si, valueOffset + 52) : 0;
                    byte[]? security = attributes.SingleOrDefault(attribute => U32(attribute, 0) == 0x50);
                    if (security is not null)
                    {
                        byte[] valueBytes = security.AsSpan(U16(security, 20), checked((int)U32(security, 16))).ToArray();
                        PortableSecurityDescriptor descriptor = PortableSecurityDescriptor.Parse(valueBytes);
                        if (descriptor.Control != 0x8004 || !descriptor.HasDiscretionaryAcl ||
                            descriptor.IsDiscretionaryAclNull || descriptor.HasSystemAcl ||
                            !descriptor.OwnerSid!.SequenceEqual(SecurityDescriptor().AsSpan(20, 12).ToArray()) ||
                            !descriptor.GroupSid!.SequenceEqual(descriptor.OwnerSid))
                        {
                            throw new InvalidDataException("Synthetic NTFS 1.2 security semantics differ from the declared fixture.");
                        }
                        unicodeSecurityHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(valueBytes));
                    }
                }
            }
        }
        return new(active, shortSi, descriptors, oldHeaders, modernSi, resident, nonresident,
            unicodeSiLength, unicodeSecurityId, unicodeSecurityHash, unicodeModified);
    }

    private static byte[] ResidentAttribute(uint type, ushort id, byte[] value)
    {
        var attribute = new byte[checked((24 + value.Length + 7) / 8 * 8)];
        W32(attribute, 0, type);
        W32(attribute, 4, checked((uint)attribute.Length));
        W16(attribute, 14, id);
        W32(attribute, 16, checked((uint)value.Length));
        W16(attribute, 20, 24);
        value.CopyTo(attribute, 24);
        return attribute;
    }

    private static byte[] SecurityDescriptor()
    {
        // Self-relative descriptor: owner/group S-1-5-18; present empty DACL.
        var bytes = new byte[52];
        bytes[0] = 1;
        W16(bytes, 2, 0x8004);
        W32(bytes, 4, 20);
        W32(bytes, 8, 32);
        W32(bytes, 16, 44);
        bytes[20] = 1;
        bytes[21] = 1;
        bytes[27] = 5;
        W32(bytes, 28, 18);
        bytes.AsSpan(20, 12).CopyTo(bytes.AsSpan(32, 12));
        bytes[44] = 2;
        W16(bytes, 46, 8);
        return bytes;
    }

    private static List<byte[]> Attributes(byte[] record)
    {
        var result = new List<byte[]>();
        int position = U16(record, 20);
        while (U32(record, position) != uint.MaxValue)
        {
            int length = checked((int)U32(record, position + 4));
            if (length < 24 || length > record.Length - position)
            {
                throw new InvalidDataException("Invalid synthetic fixture attribute bounds.");
            }
            result.Add(record.AsSpan(position, length).ToArray());
            position = checked(position + length);
        }
        return result;
    }

    private static byte[] Decode(byte[] record)
    {
        int usa = U16(record, 4);
        int count = U16(record, 6);
        for (int index = 1; index < count; index++)
        {
            if (U16(record, index * 512 - 2) != U16(record, usa))
            {
                throw new InvalidDataException("Synthetic fixture contains a torn MFT record.");
            }
            W16(record, index * 512 - 2, U16(record, usa + index * 2));
        }
        return record;
    }

    private static void Encode(byte[] record)
    {
        int usa = U16(record, 4);
        W16(record, usa, 0x5a71);
        for (int index = 1; index < U16(record, 6); index++)
        {
            W16(record, usa + index * 2, U16(record, index * 512 - 2));
            W16(record, index * 512 - 2, 0x5a71);
        }
    }

    private static List<(long Offset, long Length)> DecodeRuns(byte[] attribute, int cluster)
    {
        var result = new List<(long, long)>();
        int position = U16(attribute, 32);
        long lcn = 0;
        while (attribute[position] != 0)
        {
            byte header = attribute[position++];
            int lengthBytes = header & 15, offsetBytes = header >> 4;
            long length = 0, delta = 0;
            for (int index = 0; index < lengthBytes; index++) { length |= (long)attribute[position++] << (8 * index); }
            for (int index = 0; index < offsetBytes; index++) { delta |= (long)attribute[position++] << (8 * index); }
            if (offsetBytes is > 0 and < 8 && (delta & (1L << (offsetBytes * 8 - 1))) != 0)
            {
                delta |= -1L << (offsetBytes * 8);
            }
            lcn = checked(lcn + delta);
            result.Add((checked(lcn * cluster), checked(length * cluster)));
        }
        return result;
    }

    private static int PhysicalOffset(List<(long Offset, long Length)> runs, long position, int count)
    {
        foreach ((long offset, long length) in runs)
        {
            if (position < length)
            {
                if (count > length - position) { throw new InvalidDataException("Fixture record crosses an MFT run."); }
                return checked((int)(offset + position));
            }
            position -= length;
        }
        throw new InvalidDataException("Fixture record exceeds the MFT runs.");
    }

    private static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    private static ulong U64(byte[] b, int p) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(p));
    private static void W16(byte[] b, int p, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), value);
    private static void W32(byte[] b, int p, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p), value);
}
