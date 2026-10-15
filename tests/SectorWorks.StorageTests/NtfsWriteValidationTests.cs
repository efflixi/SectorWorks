using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsWriteValidationTests
{
    [Fact]
    public void IndependentlyAuthoredOrdinaryVolumeHasQualifiedAllocationAndSecurity()
    {
        using var work = new WorkDirectory();
        string path = CreateVolume(work);
        object snapshot = Capture(path);
        Assert.Equal(4096, Property<int>(snapshot, "ClusterSize"));
        Assert.Equal(8191, Property<long>(snapshot, "ClusterCount"));
        Assert.InRange(Property<long>(snapshot, "FreeClusters"), 1, 8190);
        Assert.InRange(Property<long>(snapshot, "FreeMftRecords"), 0, 100);
        Assert.Contains(5L, Property<IReadOnlyDictionary<long, byte[]>>(snapshot, "SecurityDescriptors").Keys);
    }

    [Fact]
    public void ExistingIndependentlyAuthoredGoldenIncludesOrdinaryResidentAndNonresidentFiles()
    {
        using var work = new WorkDirectory();
        object snapshot = Capture(work.Golden("ntfs.img"));
        Assert.True(Property<IReadOnlyDictionary<long, byte[]>>(snapshot, "SecurityDescriptors").Count > 19);
    }

    [Theory]
    [InlineData("lost", "lost cluster")]
    [InlineData("unmarked", "unmarked allocation")]
    [InlineData("tail", "tail bit")]
    [InlineData("mft", "MFT bitmap")]
    [InlineData("crosslink", "crosslinked")]
    [InlineData("backup", "backup boot")]
    [InlineData("mirror", "mirror differs")]
    [InlineData("dirty", "clean version")]
    [InlineData("log", "transaction logs")]
    [InlineData("sds-duplicate", "descriptor copies")]
    [InlineData("sds-hash", "security hash")]
    [InlineData("sii", "$SII/$SDH")]
    [InlineData("sdh", "$SII/$SDH")]
    [InlineData("security-id", "security ID")]
    [InlineData("legacy-security", "security descriptor")]
    [InlineData("dangling-index", "dangling/stale")]
    [InlineData("collation", "collation")]
    public void RawCorruptionsAreRejectedIndependentlyOfReadableFileContents(string corruption, string message)
    {
        using var work = new WorkDirectory();
        string path = CreateVolume(work);
        byte[] bytes = File.ReadAllBytes(path);
        int cluster = U16(bytes, 11) * bytes[13];
        int mft = checked((int)U64(bytes, 48) * cluster);
        int Data(int id, uint type = 0x80, string name = "")
        {
            int attr = FindAttribute(bytes, mft + id * 1024, type, name);
            return bytes[attr + 8] == 0 ? attr + U16(bytes, attr + 20) : RunOffset(bytes, attr, cluster);
        }
        switch (corruption)
        {
            case "lost":
            case "unmarked":
            case "tail":
                int bitmap = Data(6);
                int bit = corruption == "unmarked" ? 0 : corruption == "tail" ? 8191 : 0;
                if (corruption == "lost")
                {
                    while ((bytes[bitmap + bit / 8] & (1 << (bit % 8))) != 0) { bit++; }
                }
                bytes[bitmap + bit / 8] ^= (byte)(1 << (bit % 8));
                break;
            case "mft":
                bytes[Data(0, 0xb0) + 3] ^= 1;
                break;
            case "crosslink":
                int attr = FindAttribute(bytes, mft + 10 * 1024, 0x80, "");
                int pairs = attr + U16(bytes, attr + 32);
                int lengthBytes = bytes[pairs] & 15;
                int offsetBytes = bytes[pairs] >> 4;
                bytes.AsSpan(pairs + 1 + lengthBytes, offsetBytes).Clear();
                break;
            case "backup":
                bytes[bytes.Length - 512 + 72] ^= 1;
                break;
            case "mirror":
                bytes[checked((int)U64(bytes, 56) * cluster) + 16] ^= 1;
                break;
            case "dirty":
                int volumeFlags = Data(3, 0x70) + 10;
                bytes[volumeFlags] = 1;
                bytes[checked((int)U64(bytes, 56) * cluster) + volumeFlags - mft] = 1;
                break;
            case "log":
                bytes[Data(2)] = 0x52;
                break;
            case "sds-duplicate":
                bytes[Data(9, 0x80, "$SDS") + 0x40000 + 24] ^= 1;
                break;
            case "sds-hash":
                int sds = Data(9, 0x80, "$SDS");
                bytes[sds] ^= 1;
                bytes[sds + 0x40000] ^= 1;
                break;
            case "sii":
            case "sdh":
                int root = Data(9, 0x90, corruption == "sii" ? "$SII" : "$SDH");
                int entry = root + 16 + checked((int)U32(bytes, root + 16));
                int payload = entry + U16(bytes, entry);
                bytes[payload + 4] ^= 1;
                break;
            case "security-id":
                bytes[Data(24, 0x10) + 52] = 0;
                bytes[Data(24, 0x10) + 53] = 0;
                break;
            case "legacy-security":
                bytes[Data(5, 0x50)] = 9;
                break;
            case "dangling-index":
                int index = Data(5, 0xa0, "$I30");
                int indexEntry = index + 24 + checked((int)U32(bytes, index + 24));
                bytes[indexEntry + 6] ^= 1;
                break;
            case "collation":
                bytes[Data(5, 0x90, "$I30") + 4] = 3;
                break;
        }
        File.WriteAllBytes(path, bytes);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => Capture(path));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CancellationIsObservedBeforeReadingMetadata()
    {
        using var work = new WorkDirectory();
        string path = CreateVolume(work);
        Assert.Throws<OperationCanceledException>(() => Capture(path, new CancellationToken(true)));
    }

    [Theory]
    [InlineData(0x200u)]
    [InlineData(0x400u)]
    [InlineData(0x800u)]
    [InlineData(0x4000u)]
    public void UnsupportedUserFeatureFlagsAreRejected(uint flag)
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        byte[] bytes = File.ReadAllBytes(path);
        int mft = checked((int)U64(bytes, 48) * 4096);
        int user = FindUserRecord(bytes, mft);
        int si = FindAttribute(bytes, user, 0x10, "");
        int flags = si + U16(bytes, si + 20) + 32;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(flags), U32(bytes, flags) | flag);
        File.WriteAllBytes(path, bytes);
        Assert.Contains("file flags", Assert.Throws<ImageFormatException>(() => Capture(path)).Message);
    }

    [Fact]
    public void UserAlternateStreamsAreNotConfusedWithRequiredSystemNamedStreams()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        byte[] bytes = File.ReadAllBytes(path);
        int mft = checked((int)U64(bytes, 48) * 4096);
        int user = FindUserRecord(bytes, mft);
        int data = FindAttribute(bytes, user, 0x80, "");
        bytes[data + 9] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(data + 10), 24);
        File.WriteAllBytes(path, bytes);
        Assert.Contains("alternate data streams", Assert.Throws<ImageFormatException>(() => Capture(path)).Message);
    }

    [Fact]
    public void OrdinaryUserObjectIdIsNotSilentlyAcceptedWhenBackendGeneratesOptionalMetadata()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("ntfs.img");
        byte[] bytes = File.ReadAllBytes(path);
        int mft = checked((int)U64(bytes, 48) * 4096);
        int user = FindUserRecord(bytes, mft);
        byte[] record = bytes.AsSpan(user, 1024).ToArray();
        int usa = U16(record, 4);
        int sectors = U16(record, 6) - 1;
        for (int sector = 1; sector <= sectors; sector++)
        {
            record.AsSpan(usa + sector * 2, 2).CopyTo(record.AsSpan(sector * 512 - 2, 2));
        }
        int position = U16(record, 20);
        while (U32(record, position) < 0x40)
        {
            position += checked((int)U32(record, position + 4));
        }
        int used = checked((int)U32(record, 24));
        Assert.InRange(used + 40, 0, record.Length);
        record.AsSpan(position, used - position).CopyTo(record.AsSpan(position + 40));
        record.AsSpan(position, 40).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(position), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(position + 4), 40);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(position + 14), U16(record, 40));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(position + 16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(position + 20), 24);
        record.AsSpan(position + 24, 16).Fill(0x31);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(24), (uint)(used + 40));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(40), (ushort)(U16(record, 40) + 1));
        for (int sector = 1; sector <= sectors; sector++)
        {
            record.AsSpan(sector * 512 - 2, 2).CopyTo(record.AsSpan(usa + sector * 2, 2));
            record.AsSpan(usa, 2).CopyTo(record.AsSpan(sector * 512 - 2, 2));
        }
        record.CopyTo(bytes, user);
        File.WriteAllBytes(path, bytes);
        string message = Assert.Throws<ImageFormatException>(() => Capture(path)).Message;
        Assert.Contains($"MFT record {(user - mft) / 1024}", message);
        Assert.Contains("attribute 0x40 '' (resident)", message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentlySpecifiedCleanLfs11RestartPagesNeedNoReplayOrNormalization(bool closed)
    {
        using var work = new WorkDirectory();
        string path = CreateVolume(work);
        byte[] bytes = File.ReadAllBytes(path);
        int log = LogOffset(bytes, out int length);
        byte[] first = Lfs11RestartPage(length, closed);
        byte[] second = Lfs11RestartPage(length, closed);
        // LFS stores alternating checkpoints, not identical raw page copies.
        UnprotectRestartPage(second);
        Put64(second, 64, 0x10008);
        Put64(second, 64 + 64 + 8, 0x10008);
        ProtectRestartPage(second, 0x2233);
        first.CopyTo(bytes, log);
        second.CopyTo(bytes, log + 4096);
        "RCRD"u8.CopyTo(bytes.AsSpan(log + 8192));
        bytes[log + 9000] = 0x5a;
        File.WriteAllBytes(path, bytes);
        _ = Capture(path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("dirty", "active/dirty")]
    [InlineData("dirty-second", "active/dirty")]
    [InlineData("torn-first", "fixup mismatch")]
    [InlineData("torn-second", "fixup mismatch")]
    [InlineData("missing-second", "two ordinary LFS")]
    [InlineData("chkd", "two ordinary LFS")]
    [InlineData("version2", "LFS 2.0")]
    [InlineData("usa-bounds", "USA/area bounds")]
    [InlineData("area-bounds", "client array bounds")]
    [InlineData("client-count", "client state")]
    [InlineData("client-cycle", "client metadata")]
    [InlineData("client-name", "client metadata")]
    [InlineData("client-reserved", "client metadata")]
    [InlineData("file-size", "usable size")]
    [InlineData("sequence-bits", "sequence-number")]
    [InlineData("lsn-bounds", "LSN outside")]
    [InlineData("checkpoint-lsn", "checkpoint LSNs")]
    [InlineData("unknown-flags", "client state")]
    [InlineData("record-header", "record-page bounds")]
    [InlineData("geometry-mismatch", "ambiguous restart")]
    [InlineData("volume-dirty", "clean version")]
    public void DirtyAmbiguousOrMalformedLfsRestartPagesNeverQualify(string corruption, string reason)
    {
        using var work = new WorkDirectory();
        string path = CreateVolume(work);
        byte[] bytes = File.ReadAllBytes(path);
        int log = LogOffset(bytes, out int length);
        byte[] first = Lfs11RestartPage(length, false);
        byte[] second = Lfs11RestartPage(length, false);
        byte[] page = corruption.EndsWith("second", StringComparison.Ordinal) || corruption == "geometry-mismatch"
            ? second : first;
        UnprotectRestartPage(page);
        switch (corruption)
        {
            case "dirty":
            case "dirty-second": Put16(page, 64 + 14, 0); break;
            case "missing-second": second.AsSpan().Fill(0xff); break;
            case "chkd": "CHKD"u8.CopyTo(page); break;
            case "version2": Put16(page, 26, 0); Put16(page, 28, 2); break;
            case "usa-bounds": Put16(page, 4, 508); break;
            case "area-bounds": Put16(page, 64 + 22, 4088); break;
            case "client-count": Put16(page, 64 + 8, 2); break;
            case "client-cycle": Put16(page, 128 + 18, 0); break;
            case "client-name": page[128 + 32] = (byte)'X'; break;
            case "client-reserved": page[128 + 22] = 1; break;
            case "file-size": Put64(page, 64 + 24, (ulong)length - 4096); break;
            case "sequence-bits": Put32(page, 64 + 16, 1); break;
            case "lsn-bounds": Put64(page, 128, 1); break;
            case "checkpoint-lsn": Put64(page, 128 + 8, 0x20000); break;
            case "unknown-flags": Put16(page, 64 + 14, 0x8002); break;
            case "record-header": Put16(page, 64 + 36, 0); break;
            case "geometry-mismatch": Put16(page, 64 + 38, 72); break;
            case "volume-dirty":
                int mft = checked((int)U64(bytes, 48) * 4096);
                int volume = FindAttribute(bytes, mft + 3072, 0x70, "");
                int flagOffset = volume + U16(bytes, volume + 20) + 10;
                bytes[flagOffset] = 1;
                bytes[checked((int)U64(bytes, 56) * 4096) + flagOffset - mft] = 1;
                break;
        }
        if (corruption != "missing-second")
        {
            // Protect using the original fixed USA location; malformed header
            // offsets must not make the test fixture builder repair the mutation.
            ProtectRestartPage(page, 0x1234);
        }
        if (corruption is "torn-first" or "torn-second") { page[1022] ^= 1; }
        first.CopyTo(bytes, log);
        second.CopyTo(bytes, log + 4096);
        File.WriteAllBytes(path, bytes);
        Assert.Contains(reason, Assert.Throws<ImageFormatException>(() => Capture(path)).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static int LogOffset(byte[] bytes, out int length)
    {
        int cluster = U16(bytes, 11) * bytes[13];
        int mft = checked((int)U64(bytes, 48) * cluster);
        int attribute = FindAttribute(bytes, mft + 2048, 0x80, "");
        length = checked((int)U64(bytes, attribute + 48));
        return RunOffset(bytes, attribute, cluster);
    }

    // Field offsets come from the documented LFS 1.1 on-disk structures, not
    // production parser constants. This is synthetic evidence, not Windows output.
    private static byte[] Lfs11RestartPage(int logLength, bool closed)
    {
        byte[] page = new byte[4096];
        "RSTR"u8.CopyTo(page);
        Put16(page, 4, 30);
        Put16(page, 6, 9);
        Put32(page, 16, 4096);
        Put32(page, 20, 4096);
        Put16(page, 24, 64);
        Put16(page, 26, 1);
        Put16(page, 28, 1);
        Put64(page, 64, 0x10000);
        Put16(page, 72, 1);
        Put16(page, 74, closed ? (ushort)0 : (ushort)0xffff);
        Put16(page, 76, closed ? (ushort)0xffff : (ushort)0);
        Put16(page, 78, closed ? (ushort)0 : (ushort)2);
        int bits = 0;
        for (int size = logLength; size != 0; size >>= 1) { bits++; }
        Put32(page, 80, (uint)(67 - bits));
        Put16(page, 84, 224);
        Put16(page, 86, 64);
        Put64(page, 88, (ulong)logLength);
        Put16(page, 100, 48);
        Put16(page, 102, 64);
        Put32(page, 104, 5);
        Put64(page, 128, 0x8000);
        Put64(page, 136, 0x10000);
        Put16(page, 144, 0xffff);
        Put16(page, 146, 0xffff);
        Put16(page, 148, closed ? (ushort)1 : (ushort)0);
        Put32(page, 156, 8);
        "N\0T\0F\0S\0"u8.CopyTo(page.AsSpan(160));
        ProtectRestartPage(page, 0x1234);
        return page;
    }

    private static void UnprotectRestartPage(byte[] page)
    {
        for (int sector = 1; sector <= 8; sector++)
        {
            page.AsSpan(30 + sector * 2, 2).CopyTo(page.AsSpan(sector * 512 - 2, 2));
        }
    }

    private static void ProtectRestartPage(byte[] page, ushort sequence)
    {
        Put16(page, 30, sequence);
        for (int sector = 1; sector <= 8; sector++)
        {
            page.AsSpan(sector * 512 - 2, 2).CopyTo(page.AsSpan(30 + sector * 2, 2));
            Put16(page, sector * 512 - 2, sequence);
        }
    }

    private static void Put16(byte[] b, int p, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), value);
    private static void Put32(byte[] b, int p, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p), value);
    private static void Put64(byte[] b, int p, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p), value);

    private static int FindUserRecord(byte[] bytes, int mft)
    {
        for (int id = 16; id < 1024; id++)
        {
            int record = mft + id * 1024;
            if (!bytes.AsSpan(record, 4).SequenceEqual("FILE"u8) || (U16(bytes, record + 22) & 1) == 0) { continue; }
            int name = FindAttribute(bytes, record, 0x30, "");
            int value = name + U16(bytes, name + 20);
            if ((U64(bytes, value) & 0xffffffffffff) == 5) { return record; }
        }
        throw new InvalidDataException("No root-level user record in independent golden.");
    }

    private static string CreateVolume(WorkDirectory work)
    {
        string? mkntfs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(p => Path.Combine(p, "mkntfs")).FirstOrDefault(File.Exists);
        if (mkntfs == null)
        {
            return work.Golden("ntfs.img");
        }
        string path = work.PathFor("independent.img");
        using (FileStream file = File.Create(path)) { file.SetLength(32 * 1024 * 1024); }
        var start = new ProcessStartInfo(mkntfs) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-F", "-Q", "-s", "512", "-c", "4096", path, "65536" })
        {
            start.ArgumentList.Add(arg);
        }
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(true);
            throw new TimeoutException("mkntfs fixture authoring exceeded 15 seconds.");
        }
        Assert.True(process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        return path;
    }

    private static object Capture(string path, CancellationToken token = default)
    {
        using var device = new FileBlockDevice(path);
        Type type = typeof(FileBlockDevice).Assembly.GetType("SectorWorks.Storage.NtfsWriteValidation", true)!;
        try
        {
            return type.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [device, token])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    private static ulong U64(byte[] b, int p) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(p));

    private static int FindAttribute(byte[] bytes, int record, uint type, string name)
    {
        int position = record + U16(bytes, record + 20);
        while (U32(bytes, position) != uint.MaxValue)
        {
            int nameOffset = U16(bytes, position + 10);
            string actual = System.Text.Encoding.Unicode.GetString(bytes, position + nameOffset, bytes[position + 9] * 2);
            if (U32(bytes, position) == type && actual == name) { return position; }
            position += checked((int)U32(bytes, position + 4));
        }
        throw new InvalidDataException("Test fixture attribute not found.");
    }

    private static int RunOffset(byte[] bytes, int attribute, int cluster)
    {
        int position = attribute + U16(bytes, attribute + 32);
        int lengths = bytes[position] & 15;
        int offsets = bytes[position] >> 4;
        long lcn = 0;
        for (int i = 0; i < offsets; i++) { lcn |= (long)bytes[position + 1 + lengths + i] << (i * 8); }
        return checked((int)(lcn * cluster));
    }
}
