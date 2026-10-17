using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SectorWorks.Core;
using SectorWorks.NtfsSecurity;
using static SectorWorks.Storage.NtfsValidation;
using static SectorWorks.Storage.PartitionReader;
using Attribute = SectorWorks.Storage.NtfsValidation.Attribute;

namespace SectorWorks.Storage;

internal sealed record NtfsWriteSnapshot(int ClusterSize, long ClusterCount, long FreeClusters,
    long FreeMftRecords, IReadOnlyDictionary<long, byte[]> SecurityDescriptors);

/// <summary>Bounded, dependency-independent qualification of the writable NTFS subset.</summary>
internal static class NtfsWriteValidation
{
    private const string UnsupportedRestartMessage =
        "NTFS transaction logs require two ordinary LFS 1.1 RSTR pages; "
        + "CHKD and LFS 2.0/cached-metadata layouts remain unsupported. "
        + "An LFS 2.0 clean flag alone cannot establish that Windows has released cached metadata; "
        + "replay, reset and normalization are not performed.";

    private sealed record Record(long Id, byte[] Bytes, List<Attribute> Attributes)
    {
        internal ushort Sequence => U16(Bytes, 16);
        internal bool Directory => (U16(Bytes, 22) & 2) != 0;
        internal Attribute? Find(uint type, string name = "") =>
            Attributes.SingleOrDefault(a => a.Type == type && AttributeName(a.Bytes) == name);
    }

    private sealed record IndexEntry(ulong Reference, byte[] Key, byte[] Data);
    private sealed record FileName(ulong Parent, string Name, byte Namespace);
    private sealed record SecurityEntry(uint Hash, uint Id, ulong Offset, uint Length, byte[] Descriptor);
    private sealed record RestartGeometry(int SystemPageSize, int LogPageSize, ulong FileSize,
        uint SequenceBits, ushort RecordHeaderLength, ushort PageDataOffset);

    internal static NtfsWriteSnapshot Capture(IReadOnlyBlockDevice device, CancellationToken token = default)
    {
        try
        {
            return CaptureCore(device, token);
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException
            or InvalidOperationException or DecoderFallbackException)
        {
            throw new ImageFormatException($"Invalid or unsupported NTFS write metadata: {ex.Message}");
        }
    }

    private static NtfsWriteSnapshot CaptureCore(IReadOnlyBlockDevice device, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (device.Length < 32 * 1024 || device.Length > 64L * 1024 * 1024 * 1024
            || device.Length % 512 != 0 || device.LogicalSectorSize != 512)
        {
            Fail("NTFS write qualification requires a bounded <=64 GiB 512-byte-sector volume.");
        }
        byte[] boot = Bounds.Read(device, 0, 512);
        int sector = U16(boot, 11);
        int cluster = checked(sector * boot[13]);
        if (!boot.AsSpan(3, 8).SequenceEqual("NTFS    "u8) || sector != 512
            || cluster < sector || cluster > 65536 || (cluster & (cluster - 1)) != 0
            || U16(boot, 510) != 0xaa55)
        {
            Fail("Invalid or unsupported NTFS boot geometry.");
        }
        long sectors = checked((long)U64(boot, 40));
        // NTFS normally excludes the last (backup-boot) sector from this field.
        if (sectors != device.Length / sector - 1 && sectors != device.Length / sector)
        {
            Fail("NTFS sector count does not fill the selected volume.");
        }
        if (!boot.AsSpan().SequenceEqual(Bounds.Read(device, device.Length - sector, sector)))
        {
            Fail("NTFS primary and final-sector backup boot records disagree.");
        }
        long clusters = sectors / boot[13];
        long volumeBytes = checked(clusters * cluster);
        int recordSize = DecodeSize(boot[64], cluster);
        int indexSize = DecodeSize(boot[68], cluster);
        if (recordSize is < 512 or > 65536 || recordSize % sector != 0
            || indexSize is < 512 or > 65536 || indexSize % sector != 0)
        {
            Fail("Unsupported NTFS file/index record size.");
        }
        Validate(device, boot, sector, cluster, token);
        byte[] first = Bounds.Read(device, checked((long)U64(boot, 48) * cluster), recordSize);
        List<Attribute> firstAttributes = ValidateRecord(first, sector, cluster, device.Length);
        Attribute mft = firstAttributes.Single(a => a.Type == 0x80 && AttributeName(a.Bytes) == "");
        List<Run> mftRuns = Runs(mft.Bytes, cluster, volumeBytes, false);
        long initialized = checked((long)U64(mft.Bytes, 56));
        if (initialized > 128L * 1024 * 1024 || initialized % recordSize != 0)
        {
            Fail("NTFS initialized MFT exceeds the aligned 128 MiB safety cap.");
        }
        var records = new Dictionary<long, Record>();
        var live = new HashSet<long>();
        for (long offset = 0; offset < initialized; offset += recordSize)
        {
            token.ThrowIfCancellationRequested();
            byte[] raw = new byte[recordSize];
            ReadRuns(device, mftRuns, offset, raw);
            if (raw.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            {
                continue;
            }
            List<Attribute> attributes = ValidateRecord(raw, sector, cluster, device.Length);
            ApplyFixups(raw, sector);
            long id = offset / recordSize;
            if ((U16(raw, 22) & 1) == 0)
            {
                continue;
            }
            if (U64(raw, 32) != 0 || U32(raw, 44) != id || U16(raw, 16) == 0)
            {
                Fail("NTFS live record has a continuation, wrong MFT identity or zero sequence.");
            }
            records.Add(id, new(id, raw, attributes));
            live.Add(id);
        }
        for (int id = 0; id < 16; id++)
        {
            if (!records.ContainsKey(id))
            {
                Fail("NTFS required system/reserved record is unallocated.");
            }
        }
        byte[] mftBitmap = ReadAttribute(device, records[0].Find(0xb0)!, cluster, volumeBytes, 1024 * 1024);
        long allocatedMft = checked((long)U64(mft.Bytes, 48)) / recordSize;
        if (allocatedMft < initialized / recordSize || allocatedMft > mftBitmap.LongLength * 8)
        {
            Fail("NTFS MFT bitmap does not cover the allocated MFT.");
        }
        for (long id = 0; id < mftBitmap.LongLength * 8; id++)
        {
            if (Bit(mftBitmap, id) != live.Contains(id))
            {
                Fail("NTFS allocated MFT bitmap disagrees with live file records.");
            }
        }
        byte[] bitmap = ReadAttribute(device, records[6].Find(0x80)!, cluster, volumeBytes, 16 * 1024 * 1024);
        if (bitmap.LongLength < (clusters + 7) / 8)
        {
            Fail("NTFS cluster bitmap is shorter than the volume.");
        }
        var referenced = new byte[checked((int)((clusters + 7) / 8))];
        long allocatedClusters = 0;
        var names = records.ToDictionary(p => p.Key, p => Names(p.Value));
        HashSet<long> system = IdentifySystemRecords(records, names);
        foreach (Record record in records.Values)
        {
            token.ThrowIfCancellationRequested();
            ValidateFeatures(record, system.Contains(record.Id));
            foreach (Attribute attribute in record.Attributes.Where(a => a.Bytes[8] == 1))
            {
                bool badPlaceholder = record.Id == 8 && attribute.Type == 0x80 && AttributeName(attribute.Bytes) == "$Bad";
                List<Run> runs = Runs(attribute.Bytes, cluster, volumeBytes, badPlaceholder);
                if (badPlaceholder && runs.Any(r => r.Offset >= 0))
                {
                    Fail("NTFS media with recorded bad clusters is not writable.");
                }
                long actual = 0;
                foreach (Run run in runs.Where(r => r.Offset >= 0))
                {
                    actual = checked(actual + run.Length);
                    for (long lcn = run.Offset / cluster; lcn < (run.Offset + run.Length) / cluster; lcn++)
                    {
                        if (Bit(referenced, lcn))
                        {
                            Fail("NTFS crosslinked cluster allocation.");
                        }
                        SetBit(referenced, lcn);
                        allocatedClusters++;
                    }
                }
                if (badPlaceholder)
                {
                    // mkntfs's all-sparse $Bad placeholder reports logical, not physical,
                    // allocation here. It reserves no clusters and must never be read.
                    if (actual != 0 || U64(attribute.Bytes, 48) != (ulong)volumeBytes
                        || U64(attribute.Bytes, 56) != 0
                        || (U64(attribute.Bytes, 40) != 0 && U64(attribute.Bytes, 40) != (ulong)volumeBytes))
                    {
                        Fail("NTFS $BadClus sparse placeholder has unsupported allocation metadata.");
                    }
                    continue;
                }
                if (checked((long)U64(attribute.Bytes, 40)) != actual
                    || U64(attribute.Bytes, 56) > U64(attribute.Bytes, 48)
                    || (!badPlaceholder && U64(attribute.Bytes, 48) > (ulong)actual))
                {
                    Fail("NTFS nonresident allocation/data/initialized sizes disagree.");
                }
            }
        }
        for (long lcn = 0; lcn < bitmap.LongLength * 8; lcn++)
        {
            if (Bit(bitmap, lcn) != (lcn >= clusters || Bit(referenced, lcn)))
            {
                Fail("NTFS bitmap has a lost cluster, unmarked allocation or invalid tail bit.");
            }
        }
        if (!Bit(referenced, 0) || records[7].Find(0x80) is not { } bootData
            || Runs(bootData.Bytes, cluster, volumeBytes, false)[0].Offset != 0)
        {
            Fail("NTFS boot clusters are not reserved by $Boot.");
        }
        ValidateMirror(device, records[1], first, mftRuns, boot, recordSize, cluster, volumeBytes, token);
        ValidateVolumeAndLog(device, records, cluster, volumeBytes, token);
        ValidateDirectories(device, records, names, system, cluster, volumeBytes, sector, indexSize, token);
        Dictionary<long, byte[]> descriptors = ValidateSecurity(device, records, system, cluster, volumeBytes, sector, indexSize, token);
        return new(cluster, clusters, clusters - allocatedClusters, allocatedMft - live.Count, descriptors);
    }

    private static HashSet<long> IdentifySystemRecords(Dictionary<long, Record> records, Dictionary<long, List<FileName>> names)
    {
        var result = Enumerable.Range(0, 16).Select(i => (long)i).ToHashSet();
        string[] expected = ["$MFT", "$MFTMirr", "$LogFile", "$Volume", "$AttrDef", ".",
            "$Bitmap", "$Boot", "$BadClus", "$Secure", "$UpCase", "$Extend"];
        for (int id = 0; id < expected.Length; id++)
        {
            if (names[id].Count != 1 || names[id][0].Name != expected[id]
                || (names[id][0].Parent & 0xffffffffffff) != 5)
            {
                Fail("NTFS fixed system-record identity/parent is inconsistent.");
            }
        }
        foreach (var pair in names.Where(p => p.Key >= 16))
        {
            if (pair.Value.Any(n => (n.Parent & 0xffffffffffff) == 11))
            {
                if (pair.Value.Count != 1 || pair.Value[0].Name is not ("$ObjId" or "$Quota" or "$Reparse"))
                {
                    Fail("Unsupported NTFS $Extend metadata (journal/transaction/unknown feature).");
                }
                result.Add(pair.Key);
            }
        }
        return result;
    }

    private static void ValidateFeatures(Record record, bool system)
    {
        Attribute si = record.Find(0x10) ?? throw new ImageFormatException("NTFS live record lacks standard information.");
        byte[] info = Resident(si);
        if (info.Length is not (48 or 72) || (U32(info, 32) & (0x200u | 0x400u | 0x800u | 0x4000u)) != 0)
        {
            Fail("NTFS sparse/compressed/encrypted/reparse file flags are unsupported.");
        }
        foreach (Attribute attribute in record.Attributes)
        {
            string name = AttributeName(attribute.Bytes);
            bool bad = record.Id == 8 && attribute.Type == 0x80 && name == "$Bad";
            if ((U16(attribute.Bytes, 12) & ~0x8000) != 0 || (!bad && U16(attribute.Bytes, 12) != 0)
                || (attribute.Bytes[8] == 1 && U16(attribute.Bytes, 34) != 0))
            {
                Fail($"NTFS sparse/compressed/encrypted attributes are unsupported: MFT record {record.Id}, "
                    + $"attribute 0x{attribute.Type:X} '{name}', flags 0x{U16(attribute.Bytes, 12):X}.");
            }
            if (attribute.Type is 0x20 or 0x100 or 0xc0 || attribute.Type > 0xb0)
            {
                Fail($"NTFS attribute-list/reparse/unsupported attribute feature: MFT record {record.Id}, "
                    + $"attribute 0x{attribute.Type:X} '{name}'.");
            }
            if (!system && attribute.Type == 0x80 && name != "")
            {
                Fail($"NTFS user alternate data streams are unsupported: MFT record {record.Id}, "
                    + $"attribute 0x{attribute.Type:X} '{name}'.");
            }
            if (system && name != "" && !(
                ((record.Id is 5 or 11) && name == "$I30" && attribute.Type is 0x90 or 0xa0 or 0xb0)
                || (record.Id == 8 && name == "$Bad" && attribute.Type == 0x80)
                || (record.Id == 9 && ((name == "$SDS" && attribute.Type == 0x80)
                    || (name is "$SDH" or "$SII" && attribute.Type is 0x90 or 0xa0 or 0xb0)))
                || (record.Id == 10 && name == "$Info" && attribute.Type == 0x80)
                || (record.Id >= 16 && name is "$O" or "$Q" or "$R" && attribute.Type is 0x90 or 0xa0 or 0xb0)))
            {
                Fail($"Unknown named NTFS system metadata stream: MFT record {record.Id}, "
                    + $"attribute 0x{attribute.Type:X} '{name}'.");
            }
            if (!system && attribute.Type is not (0x10 or 0x30 or 0x50 or 0x80 or 0x90 or 0xa0 or 0xb0))
            {
                Fail($"Unsupported NTFS user metadata: MFT record {record.Id}, "
                    + $"attribute 0x{attribute.Type:X} '{name}' "
                    + $"({(attribute.Bytes[8] == 0 ? "resident" : "nonresident")}).");
            }
        }
        if (record.Attributes.GroupBy(a => (a.Type, AttributeName(a.Bytes))).Any(g => g.Key.Type != 0x30 && g.Count() != 1))
        {
            Fail("Duplicate NTFS attribute identities.");
        }
        if (record.Directory && record.Find(0x90, "$I30") == null)
        {
            Fail("NTFS directory is missing its filename index.");
        }
        if (!system && ((!record.Directory && record.Find(0x80) == null)
            || (U16(record.Bytes, 22) & ~3) != 0
            || record.Attributes.Any(a => a.Type is 0x90 or 0xa0 or 0xb0
                && (!record.Directory || AttributeName(a.Bytes) != "$I30"))))
        {
            Fail("NTFS user record has missing data or unsupported index/view metadata.");
        }
    }

    private static void ValidateMirror(IReadOnlyBlockDevice device, Record mirror, byte[] first,
        List<Run> mftRuns, byte[] boot, int recordSize, int cluster, long volumeBytes, CancellationToken token)
    {
        Attribute data = mirror.Find(0x80) ?? throw new ImageFormatException("Missing NTFS MFT mirror data.");
        List<Run> runs = Runs(data.Bytes, cluster, volumeBytes, false);
        int expected = Math.Max(4 * recordSize, cluster);
        if (U64(data.Bytes, 48) != (ulong)expected || runs[0].Offset != checked((long)U64(boot, 56) * cluster)
            || mftRuns[0].Offset != checked((long)U64(boot, 48) * cluster))
        {
            Fail("NTFS MFT/MFTMirr boot reservations disagree.");
        }
        for (int offset = 0; offset < expected; offset += recordSize)
        {
            token.ThrowIfCancellationRequested();
            byte[] original = offset == 0 ? first : new byte[recordSize];
            if (offset != 0)
            {
                ReadRuns(device, mftRuns, offset, original);
            }
            byte[] copy = new byte[recordSize];
            ReadRuns(device, runs, offset, copy);
            ApplyFixups(original, 512);
            ApplyFixups(copy, 512);
            // USA sequence words may differ while restored record bytes agree.
            original.AsSpan(U16(original, 4), U16(original, 6) * 2).Clear();
            copy.AsSpan(U16(copy, 4), U16(copy, 6) * 2).Clear();
            if (!original.AsSpan().SequenceEqual(copy))
            {
                Fail("NTFS MFT mirror differs from the primary records.");
            }
        }
    }

    private static void ValidateVolumeAndLog(IReadOnlyBlockDevice device, Dictionary<long, Record> records,
        int cluster, long volumeBytes, CancellationToken token)
    {
        byte[] info = Resident(records[3].Find(0x70) ?? throw new ImageFormatException("Missing NTFS volume information."));
        if (info.Length != 12 || info[8] != 3 || info[9] != 1 || U16(info, 10) != 0)
        {
            Fail("NTFS must be clean version 3.1 without pending volume flags.");
        }
        Attribute log = records[2].Find(0x80) ?? throw new ImageFormatException("Missing NTFS transaction log.");
        List<Run> runs = Runs(log.Bytes, cluster, volumeBytes, false);
        long length = checked((long)U64(log.Bytes, 56));
        if (U64(log.Bytes, 48) != (ulong)length || length < 512)
        {
            Fail("NTFS transaction logs must be nonresident and fully initialized.");
        }
        byte[] header = new byte[512];
        ReadRuns(device, runs, 0, header);
        if (header.AsSpan(0, 4).SequenceEqual("CHKD"u8))
        {
            Fail(UnsupportedRestartMessage);
        }
        if (header.AsSpan(0, 4).SequenceEqual("RSTR"u8))
        {
            int pageSize = checked((int)U32(header, 16));
            if (!ValidLogPageSize(pageSize) || length < 2L * pageSize)
            {
                Fail("NTFS transaction logs have invalid restart-page geometry.");
            }
            byte[] first = new byte[pageSize];
            byte[] second = new byte[pageSize];
            ReadRuns(device, runs, 0, first);
            ReadRuns(device, runs, pageSize, second);
            RestartGeometry primary = ValidateRestartPage(first, length);
            token.ThrowIfCancellationRequested();
            RestartGeometry secondary = ValidateRestartPage(second, length);
            if (primary != secondary)
            {
                Fail("NTFS transaction logs have ambiguous restart-page geometry.");
            }
            return;
        }
        byte[] buffer = new byte[65536];
        for (long offset = 0; offset < length; offset += buffer.Length)
        {
            token.ThrowIfCancellationRequested();
            Span<byte> part = buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset));
            ReadRuns(device, runs, offset, part);
            if (part.IndexOfAnyExcept((byte)0xff) >= 0 && part.IndexOfAnyExcept((byte)0) >= 0)
            {
                Fail("NTFS populated transaction logs require unsupported restart/replay qualification.");
            }
        }
    }

    // Layout/clean-state definitions: ntfs-3g include/ntfs-3g/logfile.h and
    // libntfs-3g/logfile.c. This recognizes clean LFS 1.1; it never replays logs.
    private static RestartGeometry ValidateRestartPage(byte[] raw, long streamLength)
    {
        byte[] page = (byte[])raw.Clone();
        if (!page.AsSpan(0, 4).SequenceEqual("RSTR"u8) || U64(page, 8) != 0
            || U16(page, 26) != 1 || U16(page, 28) != 1)
        {
            Fail(UnsupportedRestartMessage);
        }
        int systemPage = checked((int)U32(page, 16));
        int logPage = checked((int)U32(page, 20));
        int usa = U16(page, 4);
        int usaCount = U16(page, 6);
        int area = U16(page, 24);
        if (systemPage != page.Length || !ValidLogPageSize(systemPage) || !ValidLogPageSize(logPage)
            || usa < 30 || usa % 2 != 0 || usaCount != systemPage / 512 + 1
            || usa + usaCount * 2 > 510 || U16(page, usa) == 0
            || area % 8 != 0 || area < usa + usaCount * 2 || area > page.Length - 48)
        {
            Fail("NTFS transaction logs have invalid restart header/USA/area bounds.");
        }
        ApplyFixups(page, 512);
        int clients = U16(page, area + 8);
        int free = U16(page, area + 10);
        int used = U16(page, area + 12);
        int flags = U16(page, area + 14);
        int areaLength = U16(page, area + 20);
        int clientOffset = U16(page, area + 22);
        ulong fileSize = U64(page, area + 24);
        ushort recordHeader = U16(page, area + 36);
        ushort dataOffset = U16(page, area + 38);
        if (clients != 1 || !((free == 0 && used == 0xffff) || (free == 0xffff && used == 0))
            || (flags & ~3) != 0 || (used != 0xffff && (flags & 2) == 0))
        {
            Fail("NTFS transaction logs have active/dirty, unknown or inconsistent client state.");
        }
        if (clientOffset is not (48 or 64) || area + clientOffset > 510
            || areaLength < clientOffset + 160 || areaLength > page.Length - area
            || U32(page, area + 44) != 0
            || page.AsSpan(area + 48, clientOffset - 48).IndexOfAnyExcept((byte)0) >= 0)
        {
            Fail("NTFS transaction logs have invalid restart/client array bounds or extension metadata.");
        }
        if (fileSize != (ulong)streamLength || fileSize > 0x100000000UL || fileSize % (ulong)logPage != 0
            || fileSize < (ulong)(2L * systemPage + 48L * logPage)
            || recordHeader != 48 || dataOffset % 8 != 0
            || dataOffset < ((40 + (logPage / 512 + 1) * 2 + 7) & ~7)
            || dataOffset > logPage - recordHeader
            || U32(page, area + 32) > fileSize - (ulong)(2 * systemPage))
        {
            Fail("NTFS transaction logs have inconsistent usable size or record-page bounds.");
        }
        int sizeBits = 0;
        for (ulong size = fileSize; size != 0; size >>= 1) { sizeBits++; }
        uint sequenceBits = U32(page, area + 16);
        if (sequenceBits != 67 - sizeBits)
        {
            Fail("NTFS transaction logs have invalid LSN sequence-number geometry.");
        }
        int client = area + clientOffset;
        ulong currentLsn = U64(page, area);
        ulong oldestLsn = U64(page, client);
        ulong restartLsn = U64(page, client + 8);
        if (U16(page, client + 16) != 0xffff || U16(page, client + 18) != 0xffff
            || U16(page, client + 20) > 1 || (used != 0xffff && U16(page, client + 20) != 0)
            || page.AsSpan(client + 22, 6).IndexOfAnyExcept((byte)0) >= 0
            || U32(page, client + 28) != 8
            || !page.AsSpan(client + 32, 8).SequenceEqual("N\0T\0F\0S\0"u8)
            || page.AsSpan(client + 40, 120).IndexOfAnyExcept((byte)0) >= 0
            || oldestLsn > currentLsn || restartLsn > currentLsn
            || (currentLsn != 0 && used != 0xffff && restartLsn == 0))
        {
            Fail("NTFS transaction logs have invalid NTFS client metadata or checkpoint LSNs.");
        }
        foreach (ulong lsn in new[] { currentLsn, oldestLsn, restartLsn })
        {
            if (lsn == 0) { continue; }
            ulong offset = (lsn & ((1UL << (64 - (int)sequenceBits)) - 1)) * 8;
            if (offset < (ulong)(2 * systemPage) || offset >= fileSize)
            {
                Fail("NTFS transaction logs have an LSN outside the usable record area.");
            }
        }
        return new(systemPage, logPage, fileSize, sequenceBits, recordHeader, dataOffset);
    }

    private static bool ValidLogPageSize(int size) =>
        size is >= 512 and <= 65536 && (size & (size - 1)) == 0;

    private static void ValidateDirectories(IReadOnlyBlockDevice device, Dictionary<long, Record> records,
        Dictionary<long, List<FileName>> names, HashSet<long> system, int cluster, long volumeBytes,
        int sector, int indexSize, CancellationToken token)
    {
        var links = new HashSet<(long Parent, long Child, string Name, byte Namespace)>();
        byte[] upcase = ReadAttribute(device, records[10].Find(0x80)!, cluster, volumeBytes, 131072);
        if (upcase.Length != 131072)
        {
            Fail("NTFS case-collation table is incomplete.");
        }
        foreach (Record directory in records.Values.Where(r => r.Directory))
        {
            var foldedNames = new HashSet<string>(StringComparer.Ordinal);
            string? previous = null;
            foreach (IndexEntry entry in ReadIndex(device, directory, "$I30", cluster, volumeBytes, sector, indexSize, token))
            {
                long id = (long)(entry.Reference & 0xffffffffffff);
                if (!records.TryGetValue(id, out Record? child) || child.Sequence != entry.Reference >> 48)
                {
                    Fail("NTFS directory index has a dangling/stale MFT reference.");
                }
                FileName key = ParseName(entry.Key);
                string folded = new(key.Name.Select(c => (char)U16(upcase, c * 2)).ToArray());
                if (key.Parent != ((ulong)directory.Sequence << 48 | (ulong)directory.Id)
                    || !names[id].Contains(key) || !foldedNames.Add(folded)
                    || (previous != null && string.CompareOrdinal(previous, folded) >= 0)
                    || !links.Add((directory.Id, id, key.Name, key.Namespace)))
                {
                    Fail("NTFS directory index and FILE_NAME identities disagree or names collide.");
                }
                previous = folded;
            }
        }
        foreach (long id in system.Where(id => id >= 16))
        {
            Record metadata = records[id];
            string name = names[id][0].Name;
            if (name is "$Reparse" or "$ObjId")
            {
                string index = name == "$Reparse" ? "$R" : "$O";
                if (ReadIndex(device, metadata, index, cluster, volumeBytes, sector, indexSize, token).Count != 0)
                {
                    Fail("NTFS populated reparse/object-ID metadata is unsupported.");
                }
            }
            else if (name == "$Quota")
            {
                List<IndexEntry> quota = ReadIndex(device, metadata, "$Q", cluster, volumeBytes, sector, indexSize, token);
                List<IndexEntry> owners = ReadIndex(device, metadata, "$O", cluster, volumeBytes, sector, indexSize, token);
                if (quota.Count != 2 || owners.Count != 1
                    || quota.Any(e => e.Key.Length != 4 || U32(e.Key, 0) is not (1 or 256)
                        || e.Data.Length < 48 || U32(e.Data, 0) != 2 || U32(e.Data, 4) != 1
                        || U64(e.Data, 8) != 0 || U64(e.Data, 24) != ulong.MaxValue
                        || U64(e.Data, 32) != ulong.MaxValue || U64(e.Data, 40) != 0)
                    || owners[0].Data.Length != 4 || U32(owners[0].Data, 0) != 256)
                {
                    Fail("NTFS active or nondefault quota accounting is unsupported.");
                }
            }
        }
        foreach (Record record in records.Values)
        {
            if (record.Id is >= 12 and <= 15)
            {
                if (names[record.Id].Count != 0 || U16(record.Bytes, 18) != 0)
                {
                    Fail("NTFS reserved MFT record is unexpectedly linked.");
                }
                continue;
            }
            if (names[record.Id].Count != U16(record.Bytes, 18))
            {
                Fail("NTFS file-record link count disagrees with filename attributes.");
            }
            List<FileName> ordinary = names[record.Id].Where(n => n.Namespace != 2).ToList();
            if (ordinary.Count != 1 || names[record.Id].Count > 2
                || names[record.Id].Any(n => n.Parent != ordinary[0].Parent)
                || (names[record.Id].Count == 2 && ordinary[0].Namespace != 1))
            {
                Fail("NTFS hardlinks or missing filenames are unsupported.");
            }
            foreach (FileName name in names[record.Id])
            {
                long parent = (long)(name.Parent & 0xffffffffffff);
                if (!records.TryGetValue(parent, out Record? directory) || !directory.Directory
                    || directory.Sequence != name.Parent >> 48
                    || (record.Id != 5 && !links.Contains((parent, record.Id, name.Name, name.Namespace))))
                {
                    Fail("NTFS live file has a dangling parent or missing directory index entry.");
                }
            }
            var visited = new HashSet<long>();
            long current = record.Id;
            while (current != 5)
            {
                if (!visited.Add(current) || visited.Count > 256 || !names.TryGetValue(current, out List<FileName>? parents)
                    || parents.Count == 0)
                {
                    Fail("NTFS directory ancestry is cyclic, detached or too deep.");
                }
                current = (long)(names[current][0].Parent & 0xffffffffffff);
            }
            if (!system.Contains(record.Id) && names[record.Id].Any(n => n.Name.StartsWith('$')))
            {
                Fail("Unknown NTFS metadata-like user name is unsupported for writing.");
            }
        }
    }

    private static Dictionary<long, byte[]> ValidateSecurity(IReadOnlyBlockDevice device, Dictionary<long, Record> records,
        HashSet<long> system, int cluster, long volumeBytes, int sector, int indexSize, CancellationToken token)
    {
        Record secure = records[9];
        Attribute sds = secure.Find(0x80, "$SDS") ?? throw new ImageFormatException("Missing NTFS $Secure:$SDS.");
        byte[] bytes = ReadAttribute(device, sds, cluster, volumeBytes, 16 * 1024 * 1024);
        var entries = new Dictionary<uint, SecurityEntry>();
        for (int block = 0; block < bytes.Length; block += 0x80000)
        {
            int primaryEnd = Math.Min(bytes.Length, checked(block + 0x40000));
            int offset = block;
            while (offset <= primaryEnd - 20 && U32(bytes, offset + 16) != 0)
            {
                token.ThrowIfCancellationRequested();
                uint length = U32(bytes, offset + 16);
                if (length < 40 || length > primaryEnd - offset || U64(bytes, offset + 8) != (ulong)offset)
                {
                    Fail("Invalid NTFS security descriptor stream entry bounds/offset.");
                }
                int duplicate = checked(offset + 0x40000);
                if (duplicate > bytes.Length || length > bytes.Length - duplicate
                    || !bytes.AsSpan(offset, (int)length).SequenceEqual(bytes.AsSpan(duplicate, (int)length)))
                {
                    Fail("NTFS $SDS primary/duplicate descriptor copies disagree.");
                }
                byte[] descriptor = bytes.AsSpan(offset + 20, (int)length - 20).ToArray();
                ValidateDescriptor(descriptor, false);
                uint hash = SecurityHash(descriptor);
                uint id = U32(bytes, offset + 4);
                if (id < 256 || hash != U32(bytes, offset)
                    || !entries.TryAdd(id, new(hash, id, (ulong)offset, length, descriptor)))
                {
                    Fail("NTFS $SDS security hash/ID is invalid or duplicated.");
                }
                offset = checked((offset + (int)length + 15) & ~15);
            }
            if (bytes.AsSpan(Math.Min(offset, primaryEnd), primaryEnd - Math.Min(offset, primaryEnd)).IndexOfAnyExcept((byte)0) >= 0)
            {
                Fail("NTFS $SDS primary padding contains unindexed security data.");
            }
            int duplicateStart = checked(block + 0x40000);
            if (duplicateStart < bytes.Length)
            {
                int duplicateLength = Math.Min(0x40000, bytes.Length - duplicateStart);
                if (!bytes.AsSpan(block, duplicateLength).SequenceEqual(bytes.AsSpan(duplicateStart, duplicateLength)))
                {
                    Fail("NTFS $SDS duplicate block/padding disagrees.");
                }
            }
        }
        ValidateSecurityIndex(ReadIndex(device, secure, "$SII", cluster, volumeBytes, sector, indexSize, token), entries, false);
        ValidateSecurityIndex(ReadIndex(device, secure, "$SDH", cluster, volumeBytes, sector, indexSize, token), entries, true);
        var result = new Dictionary<long, byte[]>();
        foreach (Record record in records.Values)
        {
            byte[] si = Resident(record.Find(0x10)!);
            uint id = si.Length >= 56 ? U32(si, 52) : 0;
            Attribute? legacy = record.Find(0x50);
            byte[] descriptor;
            if (legacy != null)
            {
                descriptor = ReadAttribute(device, legacy, cluster, volumeBytes, 1024 * 1024);
                if (id != 0)
                {
                    Fail("NTFS record mixes legacy security and a shared security ID.");
                }
            }
            else if (entries.TryGetValue(id, out SecurityEntry? entry))
            {
                descriptor = entry.Descriptor;
            }
            else if (record.Id == 0 && id == 0)
            {
                // ntfs-3g's ordinary ntfscp updates leave $MFT itself without a
                // descriptor. This fixed metadata exception never applies to users.
                result.Add(0, []);
                continue;
            }
            else
            {
                throw new ImageFormatException("NTFS live record has a missing/zero security ID and no legacy descriptor.");
            }
            ValidateDescriptor(descriptor, record.Directory && (record.Id == 5 || !system.Contains(record.Id)));
            result.Add(record.Id, descriptor);
        }
        return result;
    }

    private static void ValidateSecurityIndex(List<IndexEntry> index, Dictionary<uint, SecurityEntry> entries, bool hashIndex)
    {
        var seen = new HashSet<uint>();
        ulong previous = 0;
        foreach (IndexEntry item in index)
        {
            if (item.Key.Length != (hashIndex ? 8 : 4) || item.Data.Length != 20)
            {
                Fail("Invalid NTFS security index key/data lengths.");
            }
            uint id = U32(item.Key, hashIndex ? 4 : 0);
            if (!entries.TryGetValue(id, out SecurityEntry? entry) || !seen.Add(id)
                || U32(item.Data, 0) != entry.Hash || U32(item.Data, 4) != id
                || U64(item.Data, 8) != entry.Offset || U32(item.Data, 16) != entry.Length
                || (hashIndex && U32(item.Key, 0) != entry.Hash))
            {
                Fail("NTFS $SII/$SDH entry disagrees with $SDS.");
            }
            ulong key = hashIndex ? (ulong)entry.Hash << 32 | id : id;
            if (seen.Count > 1 && key <= previous)
            {
                Fail("NTFS security index keys are not strictly ordered.");
            }
            previous = key;
        }
        if (seen.Count != entries.Count)
        {
            Fail("NTFS security index is missing descriptors.");
        }
    }

    private static void ValidateDescriptor(byte[] bytes, bool inheritance)
    {
        try
        {
            PortableSecurityDescriptor descriptor = PortableSecurityDescriptor.Parse(bytes);
            _ = PortableSecurityDescriptor.Parse(descriptor.ToBinary());
            if (inheritance)
            {
                descriptor.ValidateSupportedInheritance();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidDataException)
        {
            throw new ImageFormatException($"Unsupported/corrupt NTFS security descriptor: {ex.Message}");
        }
    }

    private static uint SecurityHash(byte[] bytes)
    {
        uint hash = 0;
        Span<byte> word = stackalloc byte[4];
        for (int offset = 0; offset < bytes.Length; offset += 4)
        {
            word.Clear();
            bytes.AsSpan(offset, Math.Min(4, bytes.Length - offset)).CopyTo(word);
            hash = unchecked((hash << 3 | hash >> 29) + BinaryPrimitives.ReadUInt32LittleEndian(word));
        }
        return hash;
    }

    private static List<IndexEntry> ReadIndex(IReadOnlyBlockDevice device, Record record, string name,
        int cluster, long volumeBytes, int sector, int indexSize, CancellationToken token)
    {
        Attribute root = record.Find(0x90, name) ?? throw new ImageFormatException($"Missing NTFS index {name}.");
        byte[] rootBytes = Resident(root);
        if (rootBytes.Length < 32 || U32(rootBytes, 8) != indexSize)
        {
            Fail("Invalid NTFS index root geometry.");
        }
        uint collation = name switch
        {
            "$I30" => 1,
            "$SII" or "$Q" => 0x10,
            "$SDH" => 0x12,
            "$O" when Names(record).Single().Name == "$Quota" => 0x11,
            "$O" or "$R" => 0x13,
            _ => throw new ImageFormatException("Unsupported NTFS index identity.")
        };
        if (U32(rootBytes, 0) != (name == "$I30" ? 0x30u : 0u) || U32(rootBytes, 4) != collation)
        {
            Fail("NTFS index type/collation does not match its qualified metadata identity.");
        }
        Attribute? allocation = record.Find(0xa0, name);
        Attribute? bitmap = record.Find(0xb0, name);
        byte[] bits = bitmap == null ? [] : ReadAttribute(device, bitmap, cluster, volumeBytes, 1024 * 1024);
        long length = allocation == null ? 0 : checked((long)U64(allocation.Bytes, 56));
        if ((allocation == null) != (bitmap == null) || length > 16 * 1024 * 1024 || length % indexSize != 0
            || length / indexSize > bits.LongLength * 8)
        {
            Fail("NTFS index allocation/bitmap bounds disagree.");
        }
        List<Run>? runs = allocation == null ? null : Runs(allocation.Bytes, cluster, volumeBytes, false);
        var visited = new HashSet<long>();
        var entries = new List<IndexEntry>();
        Walk(rootBytes, 16, 0);
        for (long bit = 0; bit < bits.LongLength * 8; bit++)
        {
            if (Bit(bits, bit) != visited.Contains(bit))
            {
                Fail("NTFS index bitmap contains a lost or unmarked index node.");
            }
        }
        return entries;

        void Walk(byte[] node, int header, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 256 || node.Length < header + 16)
            {
                Fail("NTFS index depth/header safety bound exceeded.");
            }
            int position = checked(header + (int)U32(node, header));
            int end = checked(header + (int)U32(node, header + 4));
            if (position < header + 16 || end < position || end > node.Length)
            {
                Fail("NTFS index entry bounds are invalid.");
            }
            while (position <= end - 16)
            {
                int size = U16(node, position + 8);
                int keyLength = U16(node, position + 10);
                int flags = U16(node, position + 12);
                if (size < 16 || size % 8 != 0 || size > end - position || (flags & ~3) != 0
                    || keyLength > size - 16 - ((flags & 1) != 0 ? 8 : 0))
                {
                    Fail("Invalid NTFS index-entry length/flags.");
                }
                if ((flags & 1) != 0)
                {
                    long vcn = checked((long)U64(node, position + size - 8));
                    long byteOffset = checked(vcn * Math.Min(cluster, indexSize));
                    long bit = byteOffset / indexSize;
                    if (runs == null || byteOffset % indexSize != 0 || byteOffset < 0
                        || byteOffset > length - indexSize || !Bit(bits, bit) || !visited.Add(bit))
                    {
                        Fail("NTFS index child is missing, shared, cyclic or outside allocation.");
                    }
                    byte[] child = new byte[indexSize];
                    ReadRuns(device, runs!, byteOffset, child);
                    if (!child.AsSpan(0, 4).SequenceEqual("INDX"u8))
                    {
                        Fail("Invalid NTFS allocated-index signature.");
                    }
                    ApplyFixups(child, sector);
                    if (U64(child, 16) != (ulong)vcn)
                    {
                        Fail("NTFS allocated-index VCN mismatch.");
                    }
                    Walk(child, 24, depth + 1);
                }
                if ((flags & 2) != 0)
                {
                    if (keyLength != 0 || position + size != end)
                    {
                        Fail("NTFS index terminal entry is malformed.");
                    }
                    return;
                }
                int dataOffset = U16(node, position);
                int dataLength = U16(node, position + 2);
                bool view = (U16(record.Bytes, 22) & 8) != 0;
                if (view && (dataOffset < 16 + keyLength || dataOffset > size
                    || dataLength > size - dataOffset - ((flags & 1) != 0 ? 8 : 0)))
                {
                    Fail("NTFS view-index payload exceeds its entry.");
                }
                entries.Add(new(U64(node, position), node.AsSpan(position + 16, keyLength).ToArray(),
                    view ? node.AsSpan(position + dataOffset, dataLength).ToArray() : []));
                position += size;
                if (entries.Count > 1000000)
                {
                    Fail("NTFS index entry budget exceeded.");
                }
            }
            Fail("NTFS index has no terminal entry.");
        }
    }

    private static List<FileName> Names(Record record) =>
        record.Attributes.Where(a => a.Type == 0x30).Select(a => ParseName(Resident(a))).ToList();

    private static FileName ParseName(byte[] bytes)
    {
        if (bytes.Length < 66 || bytes.Length != 66 + bytes[64] * 2 || bytes[64] == 0 || bytes[65] > 3)
        {
            throw new ImageFormatException("Invalid NTFS filename metadata bounds.");
        }
        string name = new UnicodeEncoding(false, false, true).GetString(bytes, 66, bytes[64] * 2);
        if (name.Contains('\0') || name.Contains('/') || name.Contains('\\'))
        {
            Fail("Invalid NTFS filename characters.");
        }
        return new(U64(bytes, 0), name, bytes[65]);
    }

    private static byte[] ReadAttribute(IReadOnlyBlockDevice device, Attribute? attribute, int cluster,
        long volumeBytes, int maximum)
    {
        if (attribute == null)
        {
            throw new ImageFormatException("Required NTFS metadata stream is missing.");
        }
        if (attribute.Bytes[8] == 0)
        {
            return Resident(attribute);
        }
        long length = checked((long)U64(attribute.Bytes, 48));
        if (length > maximum || U64(attribute.Bytes, 56) != (ulong)length)
        {
            throw new ImageFormatException("NTFS metadata stream exceeds its safety cap or is not fully initialized.");
        }
        byte[] bytes = new byte[(int)length];
        ReadRuns(device, Runs(attribute.Bytes, cluster, volumeBytes, false), 0, bytes);
        return bytes;
    }

    private static byte[] Resident(Attribute? attribute)
    {
        if (attribute == null || attribute.Bytes[8] != 0)
        {
            throw new ImageFormatException("Required NTFS metadata must be resident.");
        }
        return attribute.Bytes.AsSpan(U16(attribute.Bytes, 20), checked((int)U32(attribute.Bytes, 16))).ToArray();
    }

    private static bool Bit(byte[] bytes, long bit) =>
        bit >= 0 && bit / 8 < bytes.LongLength && (bytes[bit / 8] & (1 << (int)(bit % 8))) != 0;
    private static void SetBit(byte[] bytes, long bit) => bytes[bit / 8] |= (byte)(1 << (int)(bit % 8));
    private static int DecodeSize(byte value, int cluster)
    {
        int signed = unchecked((sbyte)value);
        if (signed is 0 or < -16)
        {
            throw new ImageFormatException("Invalid NTFS encoded record size.");
        }
        return checked(signed < 0 ? 1 << -signed : signed * cluster);
    }
    [DoesNotReturn]
    private static void Fail(string message) => throw new ImageFormatException(message);
}
