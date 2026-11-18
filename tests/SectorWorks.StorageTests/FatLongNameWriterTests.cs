using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.DependencyProbe;
using SectorWorks.Storage;

namespace SectorWorks.StorageTests;

public sealed class FatLongNameWriterTests
{
    [Theory]
    [InlineData("Mixed.txt", true)]
    [InlineData("long file name.txt", true)]
    [InlineData("café Ελληνικά 日本語.txt", true)]
    [InlineData("cafe\u0301.txt", false)]
    [InlineData("emoji😀.txt", false)]
    [InlineData("trailing.", false)]
    [InlineData(" leading", false)]
    [InlineData("trailing ", false)]
    [InlineData("CON.txt", false)]
    [InlineData("lpt1.txt", false)]
    [InlineData("bad\u202ename.txt", false)]
    [InlineData("bad:stream", false)]
    [InlineData("bad\uffff.txt", false)]
    public void SafeNameSubsetIsExplicitAndDoesNotNormalize(string name, bool supported)
    {
        Assert.Equal(supported, FatEditNames.IsSupportedName(name));
        Assert.Equal(supported, FatEditNames.IsSupported(name));
    }

    [Fact]
    public void UnpairedSurrogatesAndOverlongNamesAreRejectedBeforeNormalization()
    {
        Assert.False(FatEditNames.IsSupportedName("bad" + (char)0xd800));
        Assert.False(FatEditNames.IsSupportedName("bad" + (char)0xdc00));
        Assert.False(FatEditNames.IsSupportedName(new string('x', 256)));
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task ExistingLongTreesSupportAllOperationsWithExactNamesAliasesAndMetadata(string fixture)
    {
        using var work = new Fixture(fixture);
        var writer = new RawFatImageWriter();
        string originalHash = Hash(work.Path);
        string copy = work.Path + ".copy.img";
        File.Copy(work.Path, copy);
        string folder = "/Mixed café 日本語";
        string file = folder + "/Résumé Mixed.txt";
        writer.Apply(copy,
        [
            Op(ImageChangeKind.CreateDirectory, folder),
            Op(ImageChangeKind.AddFile, file, "first"),
            Op(ImageChangeKind.AddFile, "/collision long alpha.txt", "alpha"),
            Op(ImageChangeKind.AddFile, "/collision long beta.txt", "beta"),
            Op(ImageChangeKind.AddFile, "/" + new string('a', 13), "thirteen"),
            Op(ImageChangeKind.AddFile, "/" + new string('b', 26), "twenty-six"),
            Op(ImageChangeKind.AddFile, "/" + new string('c', 255), "max"),
        ], default);
        string alias;
        using (IImageSession image = new ImageOpener().OpenReadOnly(copy))
        {
            Assert.True(writer.Qualify(image).Available, writer.Qualify(image).Reason);
            IReadOnlyFileSystem fs = image.Volumes[0].FileSystem;
            alias = Assert.Single(fs.ReadDirectory(folder)).ShortName!;
            Assert.Equal("first", Read(fs, file));
            Assert.Contains(fs.ReadDirectory("/"), e => e.Name == new string('c', 255));
            string[] aliases = fs.ReadDirectory("/").Select(e => e.ShortName!).ToArray();
            Assert.Equal(aliases.Length, aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        await AssertTools(copy, folder, "Résumé Mixed.txt");
        byte[] boot = Boot(copy);
        writer.Apply(copy,
        [
            Op(ImageChangeKind.ReplaceFile, file, new string('r', 9000)),
            new(new(ImageChangeKind.Rename, "volume", folder, NewName: "MIXED CAFÉ 日本語"), null),
            new(new(ImageChangeKind.Rename, "volume", "/collision long alpha.txt", NewName: "Collision renamed.txt"), null),
            Op(ImageChangeKind.Delete, "/collision long beta.txt"),
            Op(ImageChangeKind.CreateDirectory, "/To delete Unicode"),
            Op(ImageChangeKind.AddFile, "/To delete Unicode/中文.txt", "deleted"),
            Op(ImageChangeKind.Delete, "/To delete Unicode"),
        ], default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(copy))
        {
            Assert.True(writer.Qualify(image).Available, writer.Qualify(image).Reason);
            IReadOnlyFileSystem fs = image.Volumes[0].FileSystem;
            FileEntry replaced = Assert.Single(fs.ReadDirectory("/MIXED CAFÉ 日本語"));
            Assert.Equal("Résumé Mixed.txt", replaced.Name);
            Assert.Equal(alias, replaced.ShortName);
            Assert.Equal(new string('r', 9000), Read(fs, replaced.Path));
            Assert.Equal("alpha", Read(fs, "/Collision renamed.txt"));
            Assert.DoesNotContain(fs.ReadDirectory("/"), e => e.Name == "To delete Unicode" || e.Name == "collision long beta.txt");
        }
        Assert.Equal(boot, Boot(copy));
        Assert.Equal(originalHash, Hash(work.Path));
        await AssertTools(copy, "/MIXED CAFÉ 日本語", "Résumé Mixed.txt");
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task LongDirectorySequencesGrowAcrossClustersAndDeleteWithoutOrphans(string fixture)
    {
        using var work = new Fixture(fixture);
        var writer = new RawFatImageWriter();
        var operations = new List<StagedWriteOperation> { Op(ImageChangeKind.CreateDirectory, "/Growing Mixed Directory") };
        for (int i = 0; i < 75; i++)
        {
            operations.Add(Op(ImageChangeKind.AddFile, $"/Growing Mixed Directory/Long filename collision {i:000}.txt", i.ToString()));
        }
        writer.Apply(work.Path, operations, default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            Assert.True(writer.Qualify(image).Available, writer.Qualify(image).Reason);
            Assert.Equal(75, image.Volumes[0].FileSystem.ReadDirectory("/Growing Mixed Directory").Count);
            Assert.Equal("74", Read(image.Volumes[0].FileSystem, "/Growing Mixed Directory/Long filename collision 074.txt"));
        }
        await AssertTools(work.Path, "/Growing Mixed Directory", "Long filename collision 074.txt");
        writer.Apply(work.Path, [Op(ImageChangeKind.Delete, "/Growing Mixed Directory")], default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            Assert.True(writer.Qualify(image).Available, writer.Qualify(image).Reason);
            Assert.DoesNotContain(image.Volumes[0].FileSystem.ReadDirectory("/"), e => e.Name == "Growing Mixed Directory");
        }
        await AssertTools(work.Path, "/", "SENTINEL");
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task IndependentlyAuthoredLongNamesKeepAliasesAttributesAndDates(string fixture)
    {
        const string mcopy = "/usr/bin/mcopy";
        if (!File.Exists(mcopy)) { return; }
        using var work = new Fixture(fixture);
        string payload = work.Path + ".txt";
        File.WriteAllText(payload, "independent");
        const string existing = "/Independent existing café.txt";
        var start = new ProcessStartInfo(mcopy) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-i", work.Path, payload, "::" + existing }) { start.ArgumentList.Add(argument); }
        start.Environment["LC_ALL"] = "C.UTF-8";
        using (Process process = Process.Start(start)!)
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await output + await errors);
        }
        // mtools updates primary FSInfo only; author the matching backup required by the writer's existing envelope.
        if (fixture == "fat32.img")
        {
            byte[] boot = Boot(work.Path);
            using var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite);
            byte[] info = new byte[512];
            file.Position = Read16(boot, 48) * 512L;
            file.ReadExactly(info);
            file.Position = (Read16(boot, 50) + Read16(boot, 48)) * 512L;
            file.Write(info);
        }
        string alias;
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            Assert.True(new RawFatImageWriter().Qualify(image).Available, new RawFatImageWriter().Qualify(image).Reason);
            alias = image.Volumes[0].FileSystem.ReadDirectory("/").Single(e => e.Name == existing[1..]).ShortName!;
        }
        byte[] original = RootEntry(work.Path, alias);
        byte[] untouched = RootEntry(work.Path, "SENTINEL.TXT");
        new RawFatImageWriter().Apply(work.Path,
            [new(new(ImageChangeKind.Rename, "volume", existing, NewName: "Independent Renamed 日本語.txt"), null)], default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            alias = image.Volumes[0].FileSystem.ReadDirectory("/").Single(e => e.Name == "Independent Renamed 日本語.txt").ShortName!;
        }
        byte[] moved = RootEntry(work.Path, alias);
        Assert.Equal(original.AsSpan(13, 13).ToArray(), moved.AsSpan(13, 13).ToArray());
        new RawFatImageWriter().Apply(work.Path,
            [Op(ImageChangeKind.ReplaceFile, "/Independent Renamed 日本語.txt", "replacement")], default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            Assert.True(new RawFatImageWriter().Qualify(image).Available, new RawFatImageWriter().Qualify(image).Reason);
            FileEntry entry = image.Volumes[0].FileSystem.ReadDirectory("/").Single(e => e.Name == "Independent Renamed 日本語.txt");
            byte[] renamed = RootEntry(work.Path, entry.ShortName!);
            Assert.Equal(original[11], renamed[11]);
            Assert.Equal(original.AsSpan(13, 9).ToArray(), renamed.AsSpan(13, 9).ToArray());
            Assert.Equal("replacement", Read(image.Volumes[0].FileSystem, entry.Path));
        }
        Assert.Equal(untouched, RootEntry(work.Path, "SENTINEL.TXT"));
        await AssertTools(work.Path, "/", "Independent Renamed 日本語.txt");
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task CaseOnlyRenameAndReplacementKeepIndependentlySelectedNoncanonicalAlias(string fixture)
    {
        const string mcopy = "/usr/bin/mcopy";
        if (!File.Exists(mcopy)) { return; }
        using var work = new Fixture(fixture);
        const string originalName = "My Fancy Long Name.txt";
        const string desiredName = "mY fANCY lONG nAME.TXT";
        string payload = work.Path + ".txt";
        File.WriteAllText(payload, "independent");
        var start = new ProcessStartInfo(mcopy) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-i", work.Path, payload, "::/" + originalName }) { start.ArgumentList.Add(argument); }
        start.Environment["LC_ALL"] = "C.UTF-8";
        using (Process process = Process.Start(start)!)
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await output + await errors);
        }
        string initialAlias;
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            initialAlias = image.Volumes[0].FileSystem.ReadDirectory("/").Single(e => e.Name == originalName).ShortName!;
            Assert.DoesNotContain(image.Volumes[0].FileSystem.ReadDirectory("/"),
                entry => entry.ShortName!.Equals("MYFANC~4.TXT", StringComparison.OrdinalIgnoreCase));
        }
        long offset = RootEntryOffset(work.Path, initialAlias);
        byte[] original = RootEntry(work.Path, initialAlias);
        "MYFANC~4TXT"u8.CopyTo(original);
        original[12] = 0x18;
        byte checksum = 0;
        foreach (byte value in original.Take(11))
        {
            checksum = unchecked((byte)(((checksum & 1) << 7) + (checksum >> 1) + value));
        }
        byte[] sourceBoot = Boot(work.Path);
        using (var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = offset;
            file.Write(original);
            for (long previous = offset - 32; previous >= 0; previous -= 32)
            {
                file.Position = previous + 11;
                if (file.ReadByte() != 0x0f) { break; }
                file.Position = previous + 13;
                file.WriteByte(checksum);
            }
            if (fixture == "fat32.img")
            {
                byte[] info = new byte[512];
                file.Position = Read16(sourceBoot, 48) * 512L;
                file.ReadExactly(info);
                file.Position = (Read16(sourceBoot, 50) + Read16(sourceBoot, 48)) * 512L;
                file.Write(info);
            }
        }
        string originalHash = Hash(work.Path);
        string savedPath = work.Path + ".output.img";
        File.Copy(work.Path, savedPath);
        StagedWriteOperation[] operations =
        [
            new(new(ImageChangeKind.Rename, "volume", "/" + originalName, NewName: desiredName), null),
            Op(ImageChangeKind.ReplaceFile, "/" + desiredName, "replacement"),
        ];
        var writer = new RawFatImageWriter();
        writer.Apply(savedPath, operations, default);
        byte[] savedEntry = RootEntry(savedPath, "MYFANC~4.TXT");
        Assert.Equal(original.AsSpan(0, 20).ToArray(), savedEntry.AsSpan(0, 20).ToArray());
        Assert.Equal(original.AsSpan(22, 4).ToArray(), savedEntry.AsSpan(22, 4).ToArray());
        using IImageSession source = new ImageOpener().OpenReadOnly(work.Path);
        using IImageSession saved = new ImageOpener().OpenReadOnly(savedPath);
        Assert.DoesNotContain(source.Volumes[0].FileSystem.ReadDirectory("/"),
            entry => entry.ShortName!.Equals("MYFANC~1.TXT", StringComparison.OrdinalIgnoreCase));
        FileEntry entry = saved.Volumes[0].FileSystem.ReadDirectory("/").Single(e => e.Name == desiredName);
        Assert.Equal("myfanc~4.txt", entry.ShortName);
        Assert.Equal("replacement", Read(saved.Volumes[0].FileSystem, "/MYFANC~4.TXT"));
        writer.ValidatePreservedMetadata(source.Volumes[0].FileSystem, saved.Volumes[0].FileSystem, operations, default);
        writer.ValidateOutput(source, saved, operations, default);
        Assert.Equal(originalHash, Hash(work.Path));
        await AssertTools(savedPath, "/", desiredName);
    }

    [Fact]
    public async Task Fat32RootLongSequencesGrowWithMirroredAllocationAndUpdatedFsInfo()
    {
        using var work = new Fixture("fat32.img");
        var writer = new RawFatImageWriter();
        writer.Apply(work.Path, Enumerable.Range(0, 100)
            .Select(i => Op(ImageChangeKind.AddFile, $"/Long root filename 日本語 {i:000}.txt", "")).ToArray(), default);
        using IImageSession image = new ImageOpener().OpenReadOnly(work.Path);
        Assert.True(writer.Qualify(image).Available, writer.Qualify(image).Reason);
        Assert.Equal(102, image.Volumes[0].FileSystem.ReadDirectory("/").Count);
        await AssertTools(work.Path, "/", "Long root filename 日本語 099.txt");
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public void BoundedVolumeQualificationRequiresExactHiddenSectorsAndNeverNormalizesBootMetadata(string fixture)
    {
        using var work = new Fixture(fixture);
        const uint hidden = 2048;
        byte[] boot = Boot(work.Path);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(28), hidden);
        using (var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Write(boot);
            if (fixture == "fat32.img")
            {
                file.Position = Read16(boot, 50) * 512L;
                file.Write(boot);
            }
        }
        var writer = new RawFatImageWriter();
        StagedWriteOperation operation = Op(ImageChangeKind.AddFile, "/Bounded Mixed café.txt", "bounded");
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            Assert.False(writer.Qualify(image).Available);
            Assert.False(writer.Qualify(image, hidden + 1).Available);
            Assert.True(writer.Qualify(image, hidden).Available, writer.Qualify(image, hidden).Reason);
            writer.ValidateCapacity(image, [operation], hidden, default);
        }
        using (var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite))
        {
            writer.ApplyToStream(file, [operation], default);
            file.Flush(true);
            if (fixture == "fat32.img")
            {
                byte[] backup = new byte[512];
                file.Position = Read16(boot, 50) * 512L;
                file.ReadExactly(backup);
                Assert.Equal(boot, backup);
            }
        }
        Assert.Equal(boot, Boot(work.Path));
        using IImageSession output = new ImageOpener().OpenReadOnly(work.Path);
        Assert.True(writer.Qualify(output, hidden).Available, writer.Qualify(output, hidden).Reason);
        Assert.Equal("bounded", Read(output.Volumes[0].FileSystem, "/Bounded Mixed café.txt"));
        Assert.False(writer.Qualify(output).Available);
    }

    [Theory]
    [InlineData(0, 0x43)]
    [InlineData(13, 0)]
    [InlineData(12, 1)]
    [InlineData(26, 1)]
    [InlineData(20, 255)]
    [InlineData(2, 0xd8)]
    public void InvalidLongSequencesAreNotWritable(int field, byte value)
    {
        using var work = new Fixture("fat12.img");
        new RawFatImageWriter().Apply(work.Path, [Op(ImageChangeKind.AddFile, "/Long Unicode café.txt", "data")], default);
        using (var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] boot = new byte[512];
            file.ReadExactly(boot);
            long root = (Read16(boot, 14) + boot[16] * Read16(boot, 22)) * 512L;
            byte[] entries = new byte[Read16(boot, 17) * 32];
            file.Position = root;
            file.ReadExactly(entries);
            int entry = Enumerable.Range(0, entries.Length / 32).First(i => entries[i * 32] != 0xe5 && entries[i * 32 + 11] == 0x0f) * 32;
            file.Position = root + entry + field;
            file.WriteByte(value);
        }
        using IImageSession image = new ImageOpener().OpenReadOnly(work.Path);
        Assert.False(new RawFatImageWriter().Qualify(image).Available);
    }

    [Fact]
    public void AliasConflictsAndFixedRootSlotExhaustionFailWithoutFalseSuccess()
    {
        using var work = new Fixture("fat12.img");
        var writer = new RawFatImageWriter();
        writer.Apply(work.Path, [Op(ImageChangeKind.AddFile, "/Long filename collision.txt", "data")], default);
        string alias;
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            alias = image.Volumes[0].FileSystem.ReadDirectory("/").Single(e => e.Name == "Long filename collision.txt").ShortName!;
        }
        Assert.Throws<IOException>(() => writer.Apply(work.Path, [Op(ImageChangeKind.AddFile, "/" + alias, "conflict")], default));
        using IImageSession source = new ImageOpener().OpenReadOnly(work.Path);
        var operations = Enumerable.Range(0, 20).Select(i => Op(ImageChangeKind.AddFile, "/" + i.ToString("D3") + new string('x', 240), "")).ToArray();
        Assert.Throws<IOException>(() => writer.ValidateCapacity(source, operations, default));
    }

    [Fact]
    public void FragmentedFixedRootRefusesLongNamesWithoutCompactingUntouchedEntries()
    {
        using var work = new Fixture("fat12.img");
        var writer = new RawFatImageWriter();
        writer.Apply(work.Path, Enumerable.Range(0, 221).Select(i => Op(ImageChangeKind.AddFile, $"/F{i:000}.TXT", "")).ToArray(), default);
        writer.Apply(work.Path, Enumerable.Range(0, 111).Select(i => Op(ImageChangeKind.Delete, $"/F{i * 2:000}.TXT")).ToArray(), default);
        using (IImageSession image = new ImageOpener().OpenReadOnly(work.Path))
        {
            Assert.True(writer.Qualify(image).Available, writer.Qualify(image).Reason);
        }
        byte[] sentinel = RootEntry(work.Path, "SENTINEL.TXT");
        Assert.Throws<IOException>(() => writer.Apply(work.Path,
            [Op(ImageChangeKind.AddFile, "/" + new string('n', 255), "")], default));
        Assert.Equal(sentinel, RootEntry(work.Path, "SENTINEL.TXT"));
    }

    [Fact]
    public async Task DeletedLongSlotsCanAllBeReusedByShortNamesOnFullFixedRoot()
    {
        using var work = new Fixture("fat12.img");
        var writer = new RawFatImageWriter();
        var operations = Enumerable.Range(0, 73)
            .Select(i => Op(ImageChangeKind.AddFile, $"/Long filename {i:000}.txt", "")).ToList();
        operations.Add(Op(ImageChangeKind.AddFile, "/LAST1.TXT", ""));
        operations.Add(Op(ImageChangeKind.AddFile, "/LAST2.TXT", ""));
        writer.Apply(work.Path, operations, default);
        writer.Apply(work.Path,
        [
            Op(ImageChangeKind.Delete, "/Long filename 000.txt"),
            Op(ImageChangeKind.AddFile, "/REUSED1.TXT", ""),
            Op(ImageChangeKind.AddFile, "/REUSED2.TXT", ""),
            Op(ImageChangeKind.AddFile, "/REUSED3.TXT", ""),
        ], default);
        using IImageSession image = new ImageOpener().OpenReadOnly(work.Path);
        Assert.True(writer.Qualify(image).Available, writer.Qualify(image).Reason);
        Assert.Equal(79, image.Volumes[0].FileSystem.ReadDirectory("/").Count);
        await AssertTools(work.Path, "/", "REUSED3");
    }

    [Fact]
    public void StaleButMirroredFat32FreeSpaceSummariesAreRejectedWithoutRepair()
    {
        using var work = new Fixture("fat32.img");
        byte[] boot = Boot(work.Path);
        using (var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite))
        {
            foreach (long sector in new[] { (long)Read16(boot, 48), Read16(boot, 50) + (long)Read16(boot, 48) })
            {
                file.Position = sector * 512 + 488;
                file.Write(new byte[4]);
            }
        }
        string original = Hash(work.Path);
        using IImageSession image = new ImageOpener().OpenReadOnly(work.Path);
        Capability capability = new RawFatImageWriter().Qualify(image);
        Assert.False(capability.Available);
        Assert.Contains("FSInfo", capability.Reason, StringComparison.Ordinal);
        Assert.Equal(original, Hash(work.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fat32AcceptsOnlyAdvisoryDifferencesInBackupFsInfo(bool changeNonHintByte)
    {
        using var work = new Fixture("fat32.img");
        byte[] boot = Boot(work.Path);
        long primary = Read16(boot, 48) * 512L;
        long backup = Read16(boot, 50) * 512L + primary;
        using (var f = File.OpenWrite(work.Path))
        {
            f.Position = backup + (changeNonHintByte ? 100 : 488);
            f.Write(changeNonHintByte ? [0x9b] : [0xff, 0xff, 0xff, 0xff]);
        }
        string before = Hash(work.Path);
        var writer = new RawFatImageWriter();
        using IImageSession source = new ImageOpener().OpenReadOnly(work.Path);
        Capability qualification = writer.Qualify(source);
        Assert.Equal(!changeNonHintByte, qualification.Available);
        if (!qualification.Available)
        {
            Assert.Contains("FSInfo", qualification.Reason, StringComparison.Ordinal);
            return;
        }
        string output = Path.Combine(Path.GetDirectoryName(work.Path)!, "save-fsinfo.img");
        File.Copy(work.Path, output);
        StagedWriteOperation[] operations = [Op(ImageChangeKind.AddFile, "/NEW.TXT", "ordinary file")];
        writer.Apply(output, operations, default);
        using IImageSession saved = new ImageOpener().OpenReadOnly(output);
        writer.ValidateOutput(source, saved, operations, default);
        byte[] data = File.ReadAllBytes(output);
        Assert.True(data.AsSpan((int)primary, 512).SequenceEqual(data.AsSpan((int)backup, 512)));
        Assert.Equal("ordinary file", Read(saved.Volumes[0].FileSystem, "/NEW.TXT"));
        Assert.Equal(before, Hash(work.Path));
        await AssertTools(output, "/", "NEW");
    }

    [Fact]
    public async Task Fat32ReplacementPreservesAllDatesWithoutRestoringTheOldHighClusterWord()
    {
        using var work = new Fixture("fat32.img");
        byte[] boot = Boot(work.Path);
        byte[] original = RootEntry(work.Path, "SENTINEL.TXT");
        int clusterBytes = boot[13] * 512;
        uint oldCluster = Read16(original, 26) | (uint)Read16(original, 20) << 16;
        const uint highCluster = 65540;
        long fatBytes = Read32(boot, 36) * 512L;
        long fatStart = Read16(boot, 14) * 512L;
        long dataStart = fatStart + boot[16] * fatBytes;
        long entryOffset = RootEntryOffset(work.Path, "SENTINEL.TXT");
        using (var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] data = new byte[clusterBytes];
            file.Position = dataStart + (oldCluster - 2L) * clusterBytes;
            file.ReadExactly(data);
            file.Position = dataStart + (highCluster - 2L) * clusterBytes;
            file.Write(data);
            foreach (int copy in new[] { 0, 1 })
            {
                file.Position = fatStart + copy * fatBytes + highCluster * 4L;
                byte[] free = new byte[4];
                file.ReadExactly(free);
                Assert.Equal(0U, Read32(free, 0));
                file.Position -= 4;
                file.Write(new byte[] { 0xff, 0xff, 0xff, 0x0f });
                file.Position = fatStart + copy * fatBytes + oldCluster * 4L;
                file.Write(new byte[4]);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(20), (ushort)(highCluster >> 16));
            BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(26), (ushort)(highCluster & 0xffff));
            file.Position = entryOffset;
            file.Write(original);
        }
        string output = work.Path + ".output.img";
        File.Copy(work.Path, output);
        StagedWriteOperation operation = Op(ImageChangeKind.ReplaceFile, "/SENTINEL.TXT", new string('r', 9000));
        var writer = new RawFatImageWriter();
        writer.Apply(output, [operation], default);
        byte[] replaced = RootEntry(output, "SENTINEL.TXT");
        Assert.Equal(0, Read16(replaced, 20));
        Assert.Equal(original.AsSpan(0, 20).ToArray(), replaced.AsSpan(0, 20).ToArray());
        Assert.Equal(original.AsSpan(22, 4).ToArray(), replaced.AsSpan(22, 4).ToArray());
        using IImageSession source = new ImageOpener().OpenReadOnly(work.Path);
        using IImageSession saved = new ImageOpener().OpenReadOnly(output);
        writer.ValidatePreservedMetadata(source.Volumes[0].FileSystem, saved.Volumes[0].FileSystem, [operation], default);
        Assert.Equal(new string('r', 9000), Read(saved.Volumes[0].FileSystem, "/SENTINEL.TXT"));
        await AssertTools(output, "/", "SENTINEL");
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(18)]
    [InlineData(24)]
    public void IndependentReopenRejectsValidButChangedAttributesAndDates(int field)
    {
        using var work = new Fixture("fat12.img");
        string output = work.Path + ".output.img";
        File.Copy(work.Path, output);
        var writer = new RawFatImageWriter();
        StagedWriteOperation operation = Op(ImageChangeKind.AddFile, "/New Mixed.txt", "new");
        writer.Apply(output, [operation], default);
        long entryOffset = RootEntryOffset(output, "SENTINEL.TXT");
        using (var file = new FileStream(output, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = entryOffset + field;
            int value = file.ReadByte();
            file.Position--;
            file.WriteByte((byte)(value ^ (field == 11 ? 2 : 1)));
        }
        using IImageSession source = new ImageOpener().OpenReadOnly(work.Path);
        using IImageSession saved = new ImageOpener().OpenReadOnly(output);
        Assert.True(writer.Qualify(saved).Available, writer.Qualify(saved).Reason);
        Assert.Equal(Read(source.Volumes[0].FileSystem, "/SENTINEL.TXT"), Read(saved.Volumes[0].FileSystem, "/SENTINEL.TXT"));
        Assert.Throws<ImageFormatException>(() => writer.ValidatePreservedMetadata(source.Volumes[0].FileSystem,
            saved.Volumes[0].FileSystem, [operation], default));
    }

    [Fact]
    public void IndependentReopenRejectsChangedShortAliasEvenWhenLongNameAndContentMatch()
    {
        using var work = new Fixture("fat12.img");
        var writer = new RawFatImageWriter();
        const string path = "/Original long café.txt";
        writer.Apply(work.Path, [Op(ImageChangeKind.AddFile, path, "original")], default);
        string alias;
        using (IImageSession source = new ImageOpener().OpenReadOnly(work.Path))
        {
            alias = source.Volumes[0].FileSystem.ReadDirectory("/").Single(e => e.Name == path[1..]).ShortName!;
        }
        string output = work.Path + ".output.img";
        File.Copy(work.Path, output);
        StagedWriteOperation operation = Op(ImageChangeKind.AddFile, "/New Mixed.txt", "new");
        writer.Apply(output, [operation], default);
        long offset = RootEntryOffset(output, alias);
        byte[] shortEntry = RootEntry(output, alias);
        shortEntry[0] = shortEntry[0] == (byte)'Z' ? (byte)'Y' : (byte)'Z';
        byte checksum = 0;
        foreach (byte value in shortEntry.AsSpan(0, 11))
        {
            checksum = unchecked((byte)(((checksum & 1) << 7) + (checksum >> 1) + value));
        }
        using (var file = new FileStream(output, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = offset;
            file.Write(shortEntry);
            for (long previous = offset - 32; previous >= 0; previous -= 32)
            {
                file.Position = previous + 11;
                if (file.ReadByte() != 0x0f) { break; }
                file.Position = previous + 13;
                file.WriteByte(checksum);
            }
        }
        using IImageSession originalImage = new ImageOpener().OpenReadOnly(work.Path);
        using IImageSession saved = new ImageOpener().OpenReadOnly(output);
        Assert.True(writer.Qualify(saved).Available, writer.Qualify(saved).Reason);
        Assert.Equal("original", Read(saved.Volumes[0].FileSystem, path));
        Assert.Throws<ImageFormatException>(() => writer.ValidatePreservedMetadata(originalImage.Volumes[0].FileSystem,
            saved.Volumes[0].FileSystem, [operation], default));
    }

    [Theory]
    [InlineData(12, 1)]
    [InlineData(13, 200)]
    [InlineData(20, 1)]
    [InlineData(24, 0)]
    public void UnpreservableExistingMetadataRemainsReadOnlyWithoutNormalization(int field, byte value)
    {
        using var work = new Fixture("fat12.img");
        long offset = RootEntryOffset(work.Path, "SENTINEL.TXT");
        using (var file = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = offset + field;
            file.WriteByte(value);
        }
        string originalHash = Hash(work.Path);
        using IImageSession image = new ImageOpener().OpenReadOnly(work.Path);
        Assert.NotEmpty(Read(image.Volumes[0].FileSystem, "/SENTINEL.TXT"));
        Assert.False(new RawFatImageWriter().Qualify(image).Available);
        Assert.Equal(originalHash, Hash(work.Path));
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public void ChildEditsPreserveExactAncestorShortAndSpecialEntryMetadata(string fixture)
    {
        using var work = new Fixture(fixture);
        byte[] boot = Boot(work.Path);
        byte[] original = RootEntry(work.Path, "DOCS");
        uint cluster = Read16(original, 26);
        bool fat32 = Read16(boot, 22) == 0;
        if (fat32) { cluster |= (uint)Read16(original, 20) << 16; }
        long rootSectors = (Read16(boot, 17) * 32 + 511) / 512;
        long dataSector = Read16(boot, 14) + boot[16] * (long)(fat32 ? Read32(boot, 36) : Read16(boot, 22)) + rootSectors;
        long offset = dataSector * 512 + (cluster - 2L) * boot[13] * 512;
        byte[] special = new byte[64];
        using (var file = File.OpenRead(work.Path))
        {
            file.Position = offset;
            file.ReadExactly(special);
        }
        new RawFatImageWriter().Apply(work.Path, [Op(ImageChangeKind.AddFile, "/dOcS/Child Mixed café.txt", "child")], default);
        Assert.Equal(original, RootEntry(work.Path, "DOCS"));
        byte[] saved = new byte[64];
        using (var file = File.OpenRead(work.Path))
        {
            file.Position = offset;
            file.ReadExactly(saved);
        }
        Assert.Equal(special, saved);
    }

    private static StagedWriteOperation Op(ImageChangeKind kind, string path, string? content = null) =>
        new(new(kind, "volume", path), content is null ? null : () => new MemoryStream(Encoding.UTF8.GetBytes(content), false));

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static byte[] Boot(string path)
    {
        using var file = File.OpenRead(path);
        byte[] boot = new byte[512];
        file.ReadExactly(boot);
        return boot;
    }

    private static byte[] RootEntry(string path, string alias)
    {
        using var file = File.OpenRead(path);
        file.Position = RootEntryOffset(path, alias);
        byte[] entry = new byte[32];
        file.ReadExactly(entry);
        return entry;
    }

    private static long RootEntryOffset(string path, string alias)
    {
        byte[] boot = Boot(path);
        bool fat32 = Read16(boot, 22) == 0;
        long rootSector = Read16(boot, 14) + boot[16] * (long)(fat32 ? Read32(boot, 36) : Read16(boot, 22));
        if (fat32) { rootSector += (Read32(boot, 44) - 2L) * boot[13]; }
        byte[] root = new byte[fat32 ? boot[13] * 512 : Read16(boot, 17) * 32];
        using var file = File.OpenRead(path);
        file.Position = rootSector * 512;
        file.ReadExactly(root);
        string[] parts = alias.ToUpperInvariant().Split('.');
        byte[] expected = Encoding.ASCII.GetBytes(parts[0].PadRight(8) + (parts.Length == 2 ? parts[1] : "").PadRight(3));
        int offset = Enumerable.Range(0, root.Length / 32).Select(i => i * 32)
            .Single(i => root.AsSpan(i, 11).SequenceEqual(expected));
        return rootSector * 512 + offset;
    }

    private static string Read(IReadOnlyFileSystem fs, string path)
    {
        using var reader = new StreamReader(fs.OpenRead(path));
        return reader.ReadToEnd();
    }

    private static async Task AssertTools(string path, string directory, string expected)
    {
        foreach (string tool in new[] { "/usr/sbin/fsck.fat", "/usr/bin/mdir" })
        {
            if (!File.Exists(tool)) { continue; }
            var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in tool.EndsWith("mdir", StringComparison.Ordinal) ? new[] { "-i", path, "::" + directory } : new[] { "-n", path })
            {
                start.ArgumentList.Add(argument);
            }
            start.Environment["LC_ALL"] = "C.UTF-8";
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = await stdout;
            Assert.True(process.ExitCode == 0, output + await stderr);
            if (tool.EndsWith("mdir", StringComparison.Ordinal)) { Assert.Contains(expected, output, StringComparison.Ordinal); }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly StorageTests.WorkDirectory _work = new();
        public string Path { get; }

        public Fixture(string name)
        {
            string goldens = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "golden");
            Path = FixtureCatalog.Materialize(goldens, _work.PathFor(""), FixtureCatalog.ReadManifest(goldens).Single(e => e.Name == name));
            using var file = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite);
            byte[] boot = new byte[512];
            file.ReadExactly(boot);
            bool fat32 = Read16(boot, 22) == 0;
            long fatSectors = fat32 ? Read32(boot, 36) : Read16(boot, 22);
            long rootSector = Read16(boot, 14) + boot[16] * fatSectors;
            if (fat32) { rootSector += (Read32(boot, 44) - 2L) * boot[13]; }
            file.Position = rootSector * 512;
            byte[] root = new byte[fat32 ? boot[13] * 512 : Read16(boot, 17) * 32];
            file.ReadExactly(root);
            int slot = Enumerable.Range(0, root.Length / 32).First(i => root[i * 32] == 0) * 32;
            boot.AsSpan(fat32 ? 71 : 43, 11).CopyTo(root.AsSpan(slot));
            root[slot + 11] = 8;
            file.Position = rootSector * 512;
            file.Write(root);
            if (fat32)
            {
                file.Position = Read16(boot, 50) * 512L;
                file.Write(boot);
                byte[] info = new byte[512];
                BinaryPrimitives.WriteUInt32LittleEndian(info, 0x41615252);
                BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(484), 0x61417272);
                BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(488), uint.MaxValue);
                BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(492), uint.MaxValue);
                BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(508), 0xaa550000);
                foreach (long sector in new[] { (long)Read16(boot, 48), Read16(boot, 50) + (long)Read16(boot, 48) })
                {
                    file.Position = sector * 512;
                    file.Write(info);
                }
            }
        }

        public void Dispose() => _work.Dispose();
    }

    private static ushort Read16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
