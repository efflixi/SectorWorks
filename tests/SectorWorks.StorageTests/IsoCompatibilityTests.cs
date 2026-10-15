using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DiscUtils.Iso9660;
using DiscUtils.Streams;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class IsoCompatibilityTests
{
    private static readonly byte[] Payload = "Synthetic ISO reader regression\n"u8.ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnusedEmptyFileExtentsNeverSeekAndDoNotDependOnTheFilename(bool joliet)
    {
        using var work = new WorkDirectory();
        string path = Create(work, joliet);
        foreach (string name in new[] { "DISK1.ID", "EMPTY.BIN" })
        {
            Mutate(path, "/DOCS/" + name, record => Both(record, 2, uint.MaxValue));
        }
        string before = Hash(path);
        using (IImageSession session = new ImageOpener().OpenReadOnly(path))
        {
            Assert.Contains(session.Warnings, warning => warning.Contains("unused out-of-range", StringComparison.Ordinal));
            IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
            Assert.False(fs.Capabilities.Modify.Available);
            IReadOnlyList<FileEntry> entries = fs.ReadDirectory("/DOCS");
            foreach (string name in new[] { "DISK1.ID", "EMPTY.BIN" })
            {
                Assert.Equal(0, Assert.Single(entries, entry => entry.Name == name).Length);
                using Stream empty = fs.OpenRead("/DOCS/" + name);
                Assert.False(empty.CanWrite);
                Assert.Equal(0, empty.Length);
                Assert.Equal(-1, empty.ReadByte());
                Assert.Equal(0, empty.Read(new byte[8]));
                Assert.Equal(0, await empty.ReadAsync(new byte[8]));
                Assert.Equal(0, empty.Seek(0, SeekOrigin.End));
                Assert.Equal(0, empty.Position);
                Assert.Throws<ImageFormatException>(() => empty.Position = 1);
                Assert.Throws<NotSupportedException>(() => empty.WriteByte(1));
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    Assert.Equal(0, await empty.ReadAsync(new byte[1], canceled.Token)));
            }
            using Stream content = fs.OpenRead("/DOCS/NOTE.TXT");
            Assert.Equal(Payload, ReadAll(content));
        }
        Assert.Equal(before, Hash(path));
    }

    [Theory]
    [InlineData("nonempty")]
    [InlineData("directory")]
    [InlineData("empty-directory")]
    [InlineData("identifier")]
    [InlineData("extent-endian")]
    [InlineData("length-endian")]
    [InlineData("multi-extent")]
    [InlineData("interleaved")]
    [InlineData("extended-attributes")]
    public void EmptyFileToleranceDoesNotExemptMalformedOrUnsupportedRecords(string damage)
    {
        using var work = new WorkDirectory();
        string path = Create(work, true);
        Mutate(path, "/DOCS/DISK1.ID", record =>
        {
            Both(record, 2, uint.MaxValue);
            switch (damage)
            {
                case "nonempty": Both(record, 10, 1); break;
                case "directory": record[25] |= 2; break;
                case "empty-directory": record[25] |= 2; Both(record, 2, 0); break;
                case "identifier": record[32] = 255; break;
                case "extent-endian": BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(6), 1); break;
                case "length-endian": BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(14), 1); break;
                case "multi-extent": record[25] |= 0x80; break;
                case "interleaved": record[26] = 1; break;
                case "extended-attributes": record[1] = 1; break;
                default: throw new ArgumentException("Unknown test damage.", nameof(damage));
            }
        });
        string before = Hash(path);
        Assert.Throws<ImageFormatException>(() => new ImageOpener().OpenReadOnly(path));
        Assert.Equal(before, Hash(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeNestedDirectoriesUseBoundedBatchMetadataAndMatchTheSignedReader(bool joliet)
    {
        using var work = new WorkDirectory();
        string path = work.PathFor("large.iso");
        var builder = new CDBuilder { UseJoliet = joliet, VolumeIdentifier = "SYNTHETIC" };
        const int count = 1600;
        foreach (string folder in new[] { "NEST/CLIPART/OFFICE", "NEST/CLIPART/SLIDES" })
        {
            for (int i = 0; i < count; i++)
            {
                builder.AddFile($"{folder}/FILE{i:D4}.BIN".Replace('/', '\\'), [(byte)(i % 251)]);
            }
        }
        Save(builder, path);
        string before = Hash(path);
        using var device = new CountingDevice(path);
        var context = new ParserContext();
        using IReadOnlyFileSystem fs = context.Run(() =>
            ImageOpener.OpenOpticalFileSystem(device, context, [], default))!;
        using var nativeSource = File.OpenRead(path);
        using var native = new CDReader(nativeSource, joliet, true);
        foreach (string folder in new[] { "/NEST/CLIPART/OFFICE", "/NEST/CLIPART/SLIDES" })
        {
            device.Reset();
            IReadOnlyList<FileEntry> entries = fs.ReadDirectory(folder);
            Assert.Equal(count, entries.Count);
            Assert.InRange(device.BytesRead, 1, 4 * 1024 * 1024);
            Assert.InRange(device.ReadCalls, 1, 4096);
            Assert.InRange(device.MaximumRequest, 1, 1024 * 1024);
            Assert.All(entries, entry =>
            {
                Assert.False(entry.IsDirectory);
                Assert.Equal(1, entry.Length);
                Assert.Equal("ReadOnly", entry.Attributes);
            });
            foreach (int i in new[] { 0, count / 2, count - 1 })
            {
                string file = $"{folder}/FILE{i:D4}.BIN";
                FileEntry entry = Assert.Single(entries, item => item.Path == file);
                string parserPath = file.Replace('/', '\\');
                Assert.Equal(native.GetAttributes(parserPath).ToString(), entry.Attributes);
                Assert.Equal(native.GetLastWriteTimeUtc(parserPath), entry.ModifiedUtc);
                Assert.Equal(native.GetFileLength(parserPath), entry.Length);
                using Stream content = fs.OpenRead(file);
                Assert.Equal(i % 251, content.ReadByte());
                Assert.Equal(-1, content.ReadByte());
            }
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => fs.ReadDirectory("/NEST/CLIPART/OFFICE", canceled.Token));
        Assert.Equal(before, Hash(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchMetadataPreservesNamesAttributesTimezoneAndRejectsAmbiguity(bool joliet)
    {
        using var work = new WorkDirectory();
        string path = Create(work, joliet);
        Mutate(path, "/DOCS/NOTE.TXT", record =>
        {
            record[25] |= 1;
            new byte[] { 120, 2, 29, 23, 30, 15, 4 }.CopyTo(record, 18);
        });
        using (IImageSession session = new ImageOpener().OpenReadOnly(path))
        {
            IReadOnlyFileSystem fs = session.Volumes[0].FileSystem;
            FileEntry entry = Assert.Single(fs.ReadDirectory("/DOCS"), item => item.Name == "NOTE.TXT");
            Assert.Equal(new DateTime(2020, 2, 29, 22, 30, 15, DateTimeKind.Utc), entry.ModifiedUtc);
            Assert.Equal((FileAttributes.ReadOnly | FileAttributes.Hidden).ToString(), entry.Attributes);
            Assert.Throws<ImageFormatException>(() => fs.ReadDirectory("/DOCS/NOTE.TXT"));
            if (joliet)
            {
                Assert.Contains(fs.ReadDirectory("/DOCS"), item => item.Name == "MixedCase-λ.txt");
                using Stream unicode = fs.OpenRead("/DOCS/MixedCase-λ.txt");
                Assert.Equal(Payload, ReadAll(unicode));
            }
        }
        Mutate(path, "/DOCS/DISK2.ID", record =>
        {
            record[33 + (record[33] == 0 ? 9 : 4)] = (byte)'1';
        });
        using IImageSession duplicate = new ImageOpener().OpenReadOnly(path);
        Assert.Contains("duplicate", Assert.Throws<ImageFormatException>(() =>
            duplicate.Volumes[0].FileSystem.ReadDirectory("/DOCS")).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unsafe-name")]
    [InlineData("invalid-time")]
    [InlineData("invalid-joliet")]
    public void BatchMetadataDoesNotInventNamesOrDatesForInvalidRecords(string damage)
    {
        using var work = new WorkDirectory();
        string path = Create(work, true);
        Mutate(path, "/DOCS/DISK1.ID", record =>
        {
            switch (damage)
            {
                case "unsafe-name": record[record[33] == 0 ? 34 : 33] = (byte)'\\'; break;
                case "invalid-time": record[19] = 13; break;
                case "invalid-joliet": record[32]--; break;
                default: throw new ArgumentException("Unknown test damage.", nameof(damage));
            }
        });
        using IImageSession session = new ImageOpener().OpenReadOnly(path);
        Assert.Throws<ImageFormatException>(() => session.Volumes[0].FileSystem.ReadDirectory("/DOCS"));
    }

    [Fact]
    public void BatchMetadataKeepsDirectorySizeAndEntryCaps()
    {
        using var oversized = new MemoryStream();
        oversized.SetLength(16 * 1024 * 1024 + 1);
        Assert.Contains("16 MiB", Assert.Throws<ImageFormatException>(() =>
            IsoDirectoryReader.Read(oversized, [new(0, oversized.Length)], "/", false, default)).Message,
            StringComparison.Ordinal);
        const int count = 131073;
        var data = new byte[((count + 50) / 51) * 2048];
        for (int i = 0; i < count; i++)
        {
            int offset = i / 51 * 2048 + i % 51 * 40;
            data[offset] = 40;
            new byte[] { 120, 1, 1, 0, 0, 0, 0 }.CopyTo(data, offset + 18);
            data[offset + 32] = 7;
            Encoding.ASCII.GetBytes($"F{i:D6}").CopyTo(data, offset + 33);
        }
        using var directory = new MemoryStream(data, false);
        Assert.Contains("131072", Assert.Throws<ImageFormatException>(() =>
            IsoDirectoryReader.Read(directory, [new(0, data.Length)], "/", false, default)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyMarkersUseExistingPreviewHexSearchAndExtractionWithoutEnablingWrites()
    {
        using var work = new WorkDirectory();
        string path = Create(work, true);
        Mutate(path, "/DOCS/DISK1.ID", record => Both(record, 2, 0x69696969));
        string before = Hash(path);
        using var service = new ExplorerService(stagingDirectory: Path.GetDirectoryName(path));
        WorkspaceInfo info = await service.OpenAsync(path);
        VolumeInfo volume = Assert.Single(info.Volumes);
        Assert.Equal(WorkspaceState.ReadyReadOnly, info.State);
        Assert.Equal(Encoding.UTF8.GetString(Payload),
            (await service.PreviewAsync(info.Id, volume.Id, "/DOCS/NOTE.TXT")).Text);
        await service.ReadHexAsync(info.Id, volume.Id, "/DOCS/DISK1.ID", 0);
        SearchHit hit = Assert.Single(await service.SearchAsync(info.Id, "DISK1.ID"));
        string output = work.PathFor("extracted");
        Directory.CreateDirectory(output);
        ExtractionResult extracted = await service.ExtractAsync(info.Id, volume.Id, [hit.Entry], output);
        Assert.Empty(extracted.Failures);
        Assert.Equal(0, new FileInfo(Path.Combine(output, "DISK1.ID")).Length);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.Delete, volume.Id, "/DOCS/DISK1.ID")));
        Assert.Equal(before, Hash(path));
    }

    private static string Create(WorkDirectory work, bool joliet)
    {
        string path = work.PathFor("compatibility.iso");
        var builder = new CDBuilder { UseJoliet = joliet, VolumeIdentifier = "SYNTHETIC" };
        builder.AddFile(@"DOCS\DISK1.ID", []);
        builder.AddFile(@"DOCS\DISK2.ID", []);
        builder.AddFile(@"DOCS\EMPTY.BIN", []);
        builder.AddFile(@"DOCS\NOTE.TXT", Payload);
        if (joliet)
        {
            builder.AddFile(@"DOCS\MixedCase-λ.txt", Payload);
        }
        builder.AddDirectory(@"DOCS\INNER");
        Save(builder, path);
        return path;
    }

    private static void Save(CDBuilder builder, string path)
    {
        using SparseStream iso = builder.Build();
        using FileStream output = File.Create(path);
        iso.CopyTo(output);
    }

    private static void Mutate(string path, string target, Action<byte[]> change)
    {
        using FileStream file = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        var roots = new HashSet<(uint, uint, bool)>();
        for (int block = 16; block < 32; block++)
        {
            var descriptor = new byte[2048];
            file.Position = block * 2048L;
            file.ReadExactly(descriptor);
            if (descriptor[0] == 255)
            {
                break;
            }

            if (descriptor[0] is 1 or 2)
            {
                roots.Add((U32(descriptor, 158), U32(descriptor, 166), descriptor[0] == 2));
            }
        }
        var matches = new List<(long Offset, byte[] Record)>();
        foreach ((uint block, uint length, bool joliet) in roots)
        {
            var queue = new Queue<(uint Block, uint Length, string Path)>();
            queue.Enqueue((block, length, ""));
            while (queue.TryDequeue(out var directory))
            {
                var data = new byte[checked((int)directory.Length)];
                file.Position = directory.Block * 2048L;
                file.ReadExactly(data);
                for (int offset = 0; offset < data.Length;)
                {
                    int lengthOfRecord = data[offset];
                    if (lengthOfRecord == 0)
                    {
                        offset = (offset / 2048 + 1) * 2048;
                        continue;
                    }
                    byte[] record = data.AsSpan(offset, lengthOfRecord).ToArray();
                    long location = directory.Block * 2048L + offset;
                    offset += lengthOfRecord;
                    if (record[32] == 1 && record[33] is 0 or 1)
                    {
                        continue;
                    }

                    string name = (joliet ? Encoding.BigEndianUnicode : Encoding.ASCII)
                        .GetString(record, 33, record[32]).Split(';')[0];
                    string child = directory.Path + "/" + name;
                    if (child.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add((location, record));
                    }

                    if ((record[25] & 2) != 0)
                    {
                        queue.Enqueue((U32(record, 2), U32(record, 10), child));
                    }
                }
            }
        }
        Assert.NotEmpty(matches);
        foreach ((long offset, byte[] record) in matches)
        {
            change(record);
            file.Position = offset;
            file.Write(record);
        }
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Both(byte[] bytes, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 4), value);
    }
    private static string Hash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    private static byte[] ReadAll(Stream stream)
    {
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private sealed class CountingDevice(string path) : IReadOnlyBlockDevice
    {
        private readonly FileBlockDevice _source = new(path);
        internal long BytesRead { get; private set; }
        internal int ReadCalls { get; private set; }
        internal int MaximumRequest { get; private set; }
        public long Length => _source.Length;
        public int LogicalSectorSize => _source.LogicalSectorSize;
        public int? PhysicalSectorSize => _source.PhysicalSectorSize;
        public bool IsReadOnly => true;
        public int Read(long offset, Span<byte> buffer)
        {
            MaximumRequest = Math.Max(MaximumRequest, buffer.Length);
            int count = _source.Read(offset, buffer);
            BytesRead += count;
            ReadCalls++;
            return count;
        }
        public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(offset, buffer.Span));
        }
        internal void Reset() { BytesRead = 0; ReadCalls = 0; MaximumRequest = 0; }
        public void Dispose() => _source.Dispose();
    }
}
