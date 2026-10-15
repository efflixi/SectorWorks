using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.DependencyProbe;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class NtfsAttributeListTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void OrdinaryExtensionStreamsReadWithoutEnablingWrites(bool nonresidentList, bool directory, bool split)
    {
        using var work = new WorkDirectory();
        var fixture = new ExtensionFixture(work, nonresidentList, directory, split);
        string before = Hash(fixture.Path);
        using (IImageSession image = new ImageOpener().OpenReadOnly(fixture.Path))
        {
            IReadOnlyFileSystem fs = image.Volumes.Single().FileSystem;
            Assert.Equal("3.1", fs.Properties["NTFS version"]);
            Assert.StartsWith("Validated read-only", fs.Properties["Attribute lists"], StringComparison.Ordinal);
            Assert.Contains(fs.ReadDirectory("/"), e => e.Name == "LARGE.BIN");
            using Stream file = fs.OpenRead("/LARGE.BIN");
            var content = new byte[8192];
            file.ReadExactly(content);
            Assert.Equal(Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)), content);
            using var sentinel = new StreamReader(fs.OpenRead("/SENTINEL.TXT"));
            Assert.Equal(FixtureCatalog.Sentinel, sentinel.ReadToEnd());
            Assert.False(new DiskImageWriter().Qualify(image).Available);
            Assert.False(fs.Capabilities.Modify.Available);
            Assert.False(fs.Capabilities.CommitInPlace.Available);
        }
        Assert.Equal(before, Hash(fixture.Path));
    }

    [Theory]
    [InlineData("zero-length")]
    [InlineData("name-bounds")]
    [InlineData("stale-sequence")]
    [InlineData("outside-mft")]
    [InlineData("wrong-type")]
    [InlineData("wrong-instance")]
    [InlineData("wrong-vcn")]
    [InlineData("duplicate-entry")]
    [InlineData("omitted-entry")]
    [InlineData("unordered")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-record-id")]
    [InlineData("inactive-extension")]
    [InlineData("wrong-directory")]
    [InlineData("nested-list")]
    [InlineData("split-gap")]
    public void MalformedListsFailBeforeDependencyParserAndReleaseSource(string corruption)
    {
        using var work = new WorkDirectory();
        var fixture = new ExtensionFixture(work, false, false, corruption == "split-gap");
        fixture.Corrupt(corruption);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() =>
            new ImageOpener().OpenReadOnly(fixture.Path));
        Assert.Contains("NTFS", error.Message, StringComparison.Ordinal);
        using FileStream unlocked = File.Open(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResidentDirectoryCompressionDefaultDoesNotEnableCompressedFileContents(bool encrypted)
    {
        using var work = new WorkDirectory();
        var fixture = new ExtensionFixture(work, false, true, false);
        fixture.SetRootFlags(encrypted ? (ushort)0x4000 : (ushort)1);
        if (encrypted)
        {
            Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(fixture.Path));
            return;
        }
        using IImageSession image = new ImageOpener().OpenReadOnly(fixture.Path);
        Assert.Contains(image.Volumes[0].FileSystem.ReadDirectory("/"), e => e.Name == "SENTINEL.TXT");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtensionStreamsSupportExplorerWorkflowsButRejectStagingAndSaveAs(bool directory)
    {
        using var work = new WorkDirectory();
        var fixture = new ExtensionFixture(work, true, directory, !directory);
        string before = Hash(fixture.Path);
        using var service = new ExplorerService(stagingDirectory: System.IO.Path.GetDirectoryName(fixture.Path));
        WorkspaceInfo info = await service.OpenAsync(fixture.Path);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.Equal(WorkspaceState.ReadyReadOnly, info.State);
        Assert.False(volume.Capabilities!.Modify.Available);
        Assert.False(volume.Capabilities.SaveAs.Available);
        Assert.False(info.Capabilities.CommitInPlace.Available);
        Assert.Equal(FixtureCatalog.Sentinel, (await service.PreviewAsync(info.Id, volume.Id, "/SENTINEL.TXT")).Text);
        Assert.Contains("00 01 02 03", (await service.ReadHexAsync(info.Id, volume.Id, "/LARGE.BIN", 0)).Text, StringComparison.Ordinal);
        SearchHit hit = Assert.Single(await service.SearchAsync(info.Id, "LARGE.BIN"));
        string destination = work.PathFor("extracted");
        Directory.CreateDirectory(destination);
        ExtractionResult extracted = await service.ExtractAsync(info.Id, volume.Id, [hit.Entry], destination);
        Assert.Empty(extracted.Failures);
        Assert.Equal(8192, extracted.BytesCompleted);
        Assert.Equal(Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)),
            File.ReadAllBytes(System.IO.Path.Combine(destination, "LARGE.BIN")));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.Delete, volume.Id, "/LARGE.BIN")));
        string output = work.PathFor("must-not-save.img");
        await Assert.ThrowsAsync<NotSupportedException>(() => service.SaveAsAsync(info.Id, output));
        Assert.False(File.Exists(output));
        Assert.Equal(before, Hash(fixture.Path));
    }

    [Theory]
    [InlineData("list-size", "safety cap")]
    [InlineData("list-flags", "unsupported flags")]
    [InlineData("named-list", "must be unnamed")]
    [InlineData("bootstrap", "bootstrap")]
    [InlineData("allocation-flags", "directory index")]
    public void UnsupportedMetadataIsRejectedWithAnExplicitBoundedReason(string kind, string message)
    {
        using var work = new WorkDirectory();
        var fixture = new ExtensionFixture(work, true, true, false);
        fixture.Limit(kind);
        ImageFormatException error = Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(fixture.Path));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyNonresidentIndexAllocationHasNoRunsOrChildNodes(bool nonresidentBitmap)
    {
        using var work = new WorkDirectory();
        var fixture = new ExtensionFixture(work, false, true, false, withEmptyDirectory: true);
        fixture.EmptyIndex(nonresidentBitmap);
        using IImageSession image = new ImageOpener().OpenReadOnly(fixture.Path);
        Assert.Empty(image.Volumes[0].FileSystem.ReadDirectory("/EMPTY"));
        Assert.False(new DiskImageWriter().Qualify(image).Available);
    }

    [Fact]
    public void OpeningCancellationDoesNotLeaveASourceHandle()
    {
        using var work = new WorkDirectory();
        var fixture = new ExtensionFixture(work, false, true, false);
        Assert.Throws<OperationCanceledException>(() => new ImageOpener()
            .OpenReadOnly(fixture.Path, new CancellationToken(true)));
        using FileStream unlocked = File.Open(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task QualifiedFatEditingDoesNotGrantPermissionToAnAttributeListNtfsVolume()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "gpt", "fat12", readOnlyNtfs: true);
        PartitionInfo partition;
        using (IImageSession image = new ImageOpener().OpenReadOnly(source))
        {
            partition = image.Volumes.Single(v => v.FileSystem.Name == "NTFS").Partition!;
        }
        using var extensionWork = new WorkDirectory();
        var fixture = new ExtensionFixture(extensionWork, true, true, false);
        byte[] ntfs = File.ReadAllBytes(fixture.Path);
        Assert.Equal(partition.Length, ntfs.LongLength);
        W32(ntfs, 28, checked((uint)(partition.Offset / 512)));
        ntfs.AsSpan(0, 512).CopyTo(ntfs.AsSpan(ntfs.Length - 512));
        using (var disk = new FileStream(source, FileMode.Open, FileAccess.Write))
        {
            disk.Position = partition.Offset;
            disk.Write(ntfs);
        }
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: System.IO.Path.GetDirectoryName(source));
        WorkspaceInfo info = await service.OpenAsync(source);
        VolumeInfo legacy = info.Volumes.Single(v => v.FileSystem == "NTFS");
        VolumeInfo fat = info.Volumes.Single(v => v.FileSystem == "FAT12");
        Assert.True(info.Capabilities.Modify.Available);
        Assert.True(fat.Capabilities!.Modify.Available);
        Assert.False(legacy.Capabilities!.Modify.Available);
        Assert.False(legacy.Capabilities.SaveAs.Available);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.Delete, legacy.Id, "/LARGE.BIN")));
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, "qualified FAT only\n");
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, fat.Id, "/NEW.TXT", payload));
        string output = work.PathFor("mixed-saved.img");
        await service.SaveAsAsync(info.Id, output);
        using (IImageSession saved = new ImageOpener().OpenReadOnly(output))
        {
            using var content = new StreamReader(saved.Volumes.Single(v => v.Id == fat.Id).FileSystem.OpenRead("/NEW.TXT"));
            Assert.Equal("qualified FAT only\n", content.ReadToEnd());
            using var bounded = new PartitionBlockDevice(saved.Device, partition.Offset, partition.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(ntfs)), DiskWriterFixtures.VirtualHash(bounded));
            Assert.False(new DiskImageWriter().QualifyVolume(saved, legacy.Id).Available);
        }
        Assert.Equal(baseline, Hash(source));
    }

    private sealed class ExtensionFixture
    {
        private readonly byte[] _bytes;
        private readonly int _cluster;
        private readonly int _mft;
        private readonly int _ownerId;
        private readonly int _extensionId;
        private readonly int _listId;
        private readonly bool _directory;
        internal string Path { get; }

        internal ExtensionFixture(WorkDirectory work, bool nonresidentList, bool directory, bool split,
            bool withEmptyDirectory = false)
        {
            Path = work.Golden("ntfs.img");
            if (withEmptyDirectory)
            {
                using FileStream stream = File.Open(Path, FileMode.Open, FileAccess.ReadWrite);
                using var fs = new SectorWorks.ManagedNtfs.NtfsFileSystem(stream);
                fs.CreateDirectory("EMPTY");
            }
            _bytes = File.ReadAllBytes(Path);
            _cluster = U16(_bytes, 11) * _bytes[13];
            _mft = checked((int)U64(_bytes, 48) * _cluster);
            byte[] first = ReadRecord(0);
            byte[] mftData = Attributes(first).Single(a => U32(a, 0) == 0x80);
            int records = checked((int)U64(mftData, 56) / 1024);
            _extensionId = Enumerable.Range(16, records - 16).First(i => (U16(ReadRecord(i), 22) & 1) == 0);
            _directory = directory;
            _ownerId = directory ? 5 : Enumerable.Range(16, records - 16).First(i =>
                Attributes(ReadRecord(i)).Any(a => U32(a, 0) == 0x30
                    && Encoding.Unicode.GetString(a, U16(a, 20) + 66, a[U16(a, 20) + 64] * 2) == "LARGE.BIN"));
            byte[] owner = ReadRecord(_ownerId);
            List<byte[]> attributes = Attributes(owner);
            uint type = directory ? 0xa0u : 0x80u;
            byte[] moved = attributes.Single(a => U32(a, 0) == type);
            Assert.Equal(1, moved[8]);
            _listId = attributes.Max(a => U16(a, 14)) + 1;
            byte[] extensionAttribute = (byte[])moved.Clone();
            if (split)
            {
                int mapping = U16(moved, 32);
                int countBytes = moved[mapping] & 15;
                int offsetBytes = moved[mapping] >> 4;
                long lcn = 0;
                for (int i = 0; i < offsetBytes; i++)
                {
                    lcn |= (long)moved[mapping + 1 + countBytes + i] << (i * 8);
                }
                Assert.Equal(2UL, U64(moved, 24) + 1);
                SetRun(moved, 0, 0, lcn);
                SetRun(extensionAttribute, 1, 1, lcn + 1);
                extensionAttribute.AsSpan(40, 24).Clear();
            }
            else
            {
                attributes.Remove(moved);
            }
            ulong ownerReference = Reference(owner, _ownerId);
            byte[] extension = (byte[])owner.Clone();
            W16(extension, 16, 1);
            W64(extension, 32, ownerReference);
            W32(extension, 44, checked((uint)_extensionId));
            WriteRecord(_extensionId, extension, [extensionAttribute]);
            var entries = new List<(byte[] Attribute, ulong Reference)>();
            entries.AddRange(attributes.Select(a => (a, ownerReference)));
            entries.Add((extensionAttribute, (1UL << 48) | (uint)_extensionId));
            byte[] list = entries.OrderBy(e => U32(e.Attribute, 0))
                .ThenBy(e => Name(e.Attribute), StringComparer.Ordinal)
                .ThenBy(e => e.Attribute[8] == 1 ? U64(e.Attribute, 16) : 0)
                .SelectMany(e => ListEntry(e.Attribute, e.Reference)).ToArray();
            byte[] listAttribute;
            if (nonresidentList)
            {
                int bitmapRecord = 6;
                byte[] bitmap = Attributes(ReadRecord(bitmapRecord)).Single(a => U32(a, 0) == 0x80);
                int bitmapOffset = RunOffset(bitmap);
                int bit = 0;
                while ((_bytes[bitmapOffset + bit / 8] & (1 << (bit % 8))) != 0)
                {
                    bit++;
                }

                _bytes[bitmapOffset + bit / 8] |= (byte)(1 << (bit % 8));
                list.CopyTo(_bytes, bit * _cluster);
                listAttribute = new byte[80];
                W32(listAttribute, 0, 0x20);
                W32(listAttribute, 4, 80);
                listAttribute[8] = 1;
                W16(listAttribute, 14, checked((ushort)_listId));
                W16(listAttribute, 32, 64);
                W64(listAttribute, 40, (ulong)_cluster);
                W64(listAttribute, 48, (ulong)list.Length);
                W64(listAttribute, 56, (ulong)list.Length);
                SetRun(listAttribute, 0, 0, bit);
            }
            else
            {
                listAttribute = Resident(0x20, checked((ushort)_listId), list);
            }
            attributes.Add(listAttribute);
            WriteRecord(_ownerId, owner, attributes.OrderBy(a => U32(a, 0)).ToList());
            byte[] mftBitmap = Attributes(first).Single(a => U32(a, 0) == 0xb0);
            if (mftBitmap[8] == 1)
            {
                _bytes[RunOffset(mftBitmap) + _extensionId / 8] |= (byte)(1 << (_extensionId % 8));
            }
            else
            {
                int mftBitmapPosition = U16(first, 20);
                foreach (byte[] attribute in Attributes(first))
                {
                    if (U32(attribute, 0) == 0xb0)
                    {
                        break;
                    }

                    mftBitmapPosition += attribute.Length;
                }
                first[mftBitmapPosition + U16(mftBitmap, 20) + _extensionId / 8] |= (byte)(1 << (_extensionId % 8));
                WriteRecord(0, first, Attributes(first));
            }
            Save();
        }

        internal void Corrupt(string kind)
        {
            byte[] owner = ReadRecord(_ownerId);
            List<byte[]> attributes = Attributes(owner);
            byte[] list = attributes.Single(a => U32(a, 0) == 0x20);
            int value = U16(list, 20);
            int length = checked((int)U32(list, 16));
            int movedEntry = value;
            while ((U64(list, movedEntry + 16) & 0x0000ffffffffffff) != (ulong)_extensionId)
            {
                movedEntry += U16(list, movedEntry + 4);
            }
            switch (kind)
            {
                case "zero-length": W16(list, value + 4, 0); break;
                case "name-bounds": list[value + 6] = 255; list[value + 7] = 255; break;
                case "stale-sequence": list[movedEntry + 22] ^= 1; break;
                case "outside-mft": W64(list, movedEntry + 16, (1UL << 48) | 0xffffffff); break;
                case "wrong-type": W32(list, movedEntry, 0xa0); break;
                case "wrong-instance": W16(list, movedEntry + 24, 0xfffe); break;
                case "wrong-vcn": W64(list, movedEntry + 8, 5); break;
                case "duplicate-entry":
                    list.AsSpan(value, U16(list, value + 4)).CopyTo(list.AsSpan(movedEntry));
                    break;
                case "omitted-entry":
                    int firstLength = U16(list, value + 4);
                    list.AsSpan(value + firstLength, length - firstLength).CopyTo(list.AsSpan(value));
                    W32(list, 16, checked((uint)(length - firstLength)));
                    break;
                case "unordered": W32(list, value, 0xf0); break;
                default:
                    byte[] extension = ReadRecord(_extensionId);
                    List<byte[]> extAttributes = Attributes(extension);
                    switch (kind)
                    {
                        case "wrong-owner": W64(extension, 32, (1UL << 48) | 5); break;
                        case "wrong-record-id": W32(extension, 44, checked((uint)(_extensionId + 1))); break;
                        case "inactive-extension": W16(extension, 22, 0); break;
                        case "wrong-directory": W16(extension, 22, _directory ? (ushort)1 : (ushort)3); break;
                        case "nested-list": extAttributes.Add(Resident(0x20, 99, [])); break;
                        case "split-gap":
                            W64(extAttributes[0], 16, 2);
                            W64(extAttributes[0], 24, 2);
                            W64(list, movedEntry + 8, 2);
                            break;
                        default: throw new ArgumentException("Unknown corruption.", nameof(kind));
                    }
                    WriteRecord(_extensionId, extension, extAttributes);
                    break;
            }
            WriteRecord(_ownerId, owner, attributes);
            Save();
        }

        internal void SetRootFlags(ushort flags)
        {
            byte[] owner = ReadRecord(5);
            List<byte[]> attributes = Attributes(owner);
            W16(attributes.Single(a => U32(a, 0) == 0x90), 12, flags);
            WriteRecord(5, owner, attributes);
            Save();
        }

        internal void Limit(string kind)
        {
            int id = kind == "allocation-flags" ? _extensionId : _ownerId;
            byte[] record = ReadRecord(id);
            List<byte[]> attributes = Attributes(record);
            byte[] list = attributes.Single(a => U32(a, 0) == (kind == "allocation-flags" ? 0xa0u : 0x20u));
            switch (kind)
            {
                case "list-size":
                    W64(list, 48, 1024 * 1024 + 1);
                    W64(list, 56, 1024 * 1024 + 1);
                    break;
                case "list-flags": W16(list, 12, 0x4000); break;
                case "named-list":
                    var named = new byte[list.Length + 8];
                    list.AsSpan(0, 64).CopyTo(named);
                    list.AsSpan(64).CopyTo(named.AsSpan(72));
                    named[9] = 1;
                    W16(named, 10, 64);
                    W16(named, 64, 'X');
                    W16(named, 32, 72);
                    W32(named, 4, checked((uint)named.Length));
                    attributes[attributes.IndexOf(list)] = named;
                    break;
                case "allocation-flags": W16(list, 12, 0x4000); break;
                case "bootstrap":
                    record = ReadRecord(0);
                    attributes = Attributes(record);
                    attributes.Add(Resident(0x20, 99, []));
                    id = 0;
                    break;
                default: throw new ArgumentException("Unknown limit.", nameof(kind));
            }
            WriteRecord(id, record, attributes);
            Save();
        }

        internal void EmptyIndex(bool nonresidentBitmap)
        {
            byte[] mftData = Attributes(ReadRecord(0)).Single(a => U32(a, 0) == 0x80);
            int records = checked((int)U64(mftData, 56) / 1024);
            int id = Enumerable.Range(16, records - 16).First(i =>
                Attributes(ReadRecord(i)).Any(a => U32(a, 0) == 0x30
                    && Encoding.Unicode.GetString(a, U16(a, 20) + 66, a[U16(a, 20) + 64] * 2) == "EMPTY"));
            byte[] owner = ReadRecord(id);
            List<byte[]> attributes = Attributes(owner);
            byte[] root = attributes.Single(a => U32(a, 0) == 0x90);
            int value = U16(root, 20);
            root.AsSpan(value + 16).Clear();
            W32(root, value + 16, 16);
            W32(root, value + 20, 32);
            W32(root, value + 24, checked((uint)(root.Length - value - 16)));
            W16(root, value + 16 + 16 + 8, 16);
            W16(root, value + 16 + 16 + 12, 2);
            byte[] bitmap = (byte[])Attributes(ReadRecord(5)).Single(a => U32(a, 0) == 0xb0).Clone();
            bitmap.AsSpan(U16(bitmap, 20), checked((int)U32(bitmap, 16))).Clear();
            byte[] allocation = (byte[])Attributes(ReadRecord(_extensionId)).Single(a => U32(a, 0) == 0xa0).Clone();
            ushort next = checked((ushort)(attributes.Max(a => U16(a, 14)) + 1));
            W16(allocation, 14, next);
            W64(allocation, 24, ulong.MaxValue);
            allocation.AsSpan(40, 24).Clear();
            allocation.AsSpan(U16(allocation, 32)).Clear();
            if (nonresidentBitmap)
            {
                bitmap = (byte[])allocation.Clone();
                W32(bitmap, 0, 0xb0);
            }
            W16(bitmap, 14, checked((ushort)(next + 1)));
            attributes.Add(allocation);
            attributes.Add(bitmap);
            WriteRecord(id, owner, attributes.OrderBy(a => U32(a, 0)).ToList());
            Save();
        }

        private void Save() => File.WriteAllBytes(Path, _bytes);

        private byte[] ReadRecord(int id)
        {
            byte[] record = _bytes.AsSpan(_mft + id * 1024, 1024).ToArray();
            int usa = U16(record, 4);
            for (int i = 1; i < 3; i++)
            {
                record.AsSpan(usa + i * 2, 2).CopyTo(record.AsSpan(i * 512 - 2));
            }
            return record;
        }

        private void WriteRecord(int id, byte[] header, List<byte[]> attributes)
        {
            var record = new byte[1024];
            int position = U16(header, 20);
            header.AsSpan(0, position).CopyTo(record);
            foreach (byte[] attribute in attributes)
            {
                Assert.True(position + attribute.Length + 8 <= record.Length, "Synthetic attribute list exceeds record.");
                attribute.CopyTo(record, position);
                position += attribute.Length;
            }
            W32(record, position, uint.MaxValue);
            W32(record, 24, checked((uint)(position + 8)));
            W16(record, 40, checked((ushort)(attributes.Max(a => U16(a, 14)) + 1)));
            int usa = U16(record, 4);
            W16(record, usa, 0x55aa);
            for (int i = 1; i < 3; i++)
            {
                record.AsSpan(i * 512 - 2, 2).CopyTo(record.AsSpan(usa + i * 2));
                W16(record, i * 512 - 2, 0x55aa);
            }
            record.CopyTo(_bytes, _mft + id * 1024);
        }

        private static List<byte[]> Attributes(byte[] record)
        {
            var result = new List<byte[]>();
            if (!record.AsSpan(0, 4).SequenceEqual("FILE"u8))
            {
                return result;
            }
            for (int offset = U16(record, 20); U32(record, offset) != uint.MaxValue;)
            {
                int size = checked((int)U32(record, offset + 4));
                result.Add(record.AsSpan(offset, size).ToArray());
                offset += size;
            }
            return result;
        }

        private static byte[] Resident(uint type, ushort id, byte[] data)
        {
            var result = new byte[(24 + data.Length + 7) / 8 * 8];
            W32(result, 0, type);
            W32(result, 4, (uint)result.Length);
            W16(result, 14, id);
            W32(result, 16, (uint)data.Length);
            W16(result, 20, 24);
            data.CopyTo(result, 24);
            return result;
        }

        private static byte[] ListEntry(byte[] attribute, ulong reference)
        {
            byte[] name = Encoding.Unicode.GetBytes(Name(attribute));
            var result = new byte[(32 + name.Length + 7) / 8 * 8];
            W32(result, 0, U32(attribute, 0));
            W16(result, 4, (ushort)result.Length);
            result[6] = (byte)(name.Length / 2);
            result[7] = 32;
            W64(result, 8, attribute[8] == 1 ? U64(attribute, 16) : 0);
            W64(result, 16, reference);
            W16(result, 24, U16(attribute, 14));
            name.CopyTo(result, 32);
            return result;
        }

        private static void SetRun(byte[] attribute, ulong start, ulong end, long lcn)
        {
            W64(attribute, 16, start);
            W64(attribute, 24, end);
            int mapping = U16(attribute, 32);
            attribute.AsSpan(mapping).Clear();
            attribute[mapping] = 0x31;
            attribute[mapping + 1] = checked((byte)(end - start + 1));
            for (int i = 0; i < 3; i++)
            {
                attribute[mapping + 2 + i] = (byte)((lcn >> (i * 8)) & 255);
            }
        }

        private int RunOffset(byte[] attribute)
        {
            int mapping = U16(attribute, 32);
            int lengthBytes = attribute[mapping] & 15;
            int offsetBytes = attribute[mapping] >> 4;
            long lcn = 0;
            for (int i = 0; i < offsetBytes; i++)
            {
                lcn |= (long)attribute[mapping + 1 + lengthBytes + i] << (i * 8);
            }

            return checked((int)lcn * _cluster);
        }

        private static string Name(byte[] a) => Encoding.Unicode.GetString(a, U16(a, 10), a[9] * 2);
        private static ulong Reference(byte[] record, int id) => (uint)id | ((ulong)U16(record, 16) << 48);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    private static ulong U64(byte[] b, int p) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(p));
    private static void W16(byte[] b, int p, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), v);
    private static void W32(byte[] b, int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p), v);
    private static void W64(byte[] b, int p, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p), v);
}
