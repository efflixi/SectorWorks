using System.Buffers.Binary;
using System.Security.Cryptography;
using DiscUtils;
using SectorWorks.Core;
using SectorWorks.NtfsSecurity;
using NewFileOptions = SectorWorks.ManagedNtfs.NewFileOptions;
using NtfsFileSystem = SectorWorks.ManagedNtfs.NtfsFileSystem;

namespace SectorWorks.Storage;

internal sealed class NtfsImageWriter(bool includeCandidates = false, bool includeCommitCandidates = false)
{
    internal Capability QualifyCommit(IImageSession image)
    {
        if (includeCommitCandidates) { return new(true, "Qualified bounded NTFS commit pipeline with independent per-volume validation."); }
        try
        {
            _ = NtfsWriteValidation.Capture(image.Device);
            return new(true, "Independently qualified ordinary NTFS commit pipeline.");
        }
        catch (ImageFormatException error)
        { return new(false, "Direct commit is unavailable for this NTFS volume: " + error.Message); }
    }
    private NtfsWriteSnapshot CaptureAllocation(IReadOnlyBlockDevice device, CancellationToken token = default)
        => includeCandidates ? NtfsWriteValidation.CaptureLegacyCandidate(device, token) : NtfsWriteValidation.Capture(device, token);
    private sealed record Entry(string Path, bool Directory, long Length, long Id,
        DateTime Created, DateTime Accessed, DateTime Written, DateTime Changed,
        FileAttributes Attributes, string? ShortName, byte[] Security, byte[] FileNameTimes, byte[]? ShortNameTimes);

    internal Capability Qualify(IImageSession image, uint hiddenSectors)
    {
        try
        {
            RequireLayout(image.Device, hiddenSectors);
            _ = CaptureAllocation(image.Device);
            _ = Capture(image.Device, default);
            return new(true, "Qualified bounded NTFS staged editing and validated Save As within the declared allocation/security subset; source commit has separate qualification. Formatting is unsupported.");
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException
            or InvalidOperationException or OverflowException or UnauthorizedAccessException)
        {
            return new(false, "NTFS write qualification failed; inspection remains read-only: " + error.Message);
        }
    }

    internal void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations,
        uint hiddenSectors, CancellationToken token)
    {
        RequireLayout(image.Device, hiddenSectors);
        NtfsWriteSnapshot allocation = CaptureAllocation(image.Device, token);
        Dictionary<string, Entry> entries = Capture(image.Device, token);
        long available = checked(allocation.FreeClusters * allocation.ClusterSize);
        var addedDescriptors = new HashSet<string>(StringComparer.Ordinal);
        long cluster = allocation.ClusterSize;
        long Allocate(long length) => checked((length + cluster - 1) / cluster * cluster);
        foreach (StagedWriteOperation operation in operations)
        {
            token.ThrowIfCancellationRequested();
            ImageChangeRequest request = operation.Request;
            if (request.Kind is ImageChangeKind.Metadata || !Enum.IsDefined(request.Kind))
            {
                throw new NotSupportedException("NTFS metadata editing is not implemented.");
            }
            string name = request.Kind == ImageChangeKind.Rename ? request.NewName! : request.Path[(request.Path.LastIndexOf('/') + 1)..];
            if (!ImageEditNames.IsSupportedName(name))
            {
                throw new NotSupportedException("NTFS editing requires the declared NFC BMP filename subset.");
            }
            Entry? existing = entries.GetValueOrDefault(request.Path);
            byte[]? addedSecurity = request.Kind is ImageChangeKind.AddFile or ImageChangeKind.CreateDirectory
                ? PortableSecurityDescriptor.Parse(entries[Parent(request.Path).Length == 0 ? "/" : Parent(request.Path)].Security)
                    .Inherit(request.Kind == ImageChangeKind.CreateDirectory).ToBinary()
                : null;
            if (addedSecurity is not null && addedDescriptors.Add(Convert.ToHexString(SHA256.HashData(addedSecurity))))
            {
                available = checked(available - 512 * 1024);
            }
            if (request.Kind is ImageChangeKind.AddFile or ImageChangeKind.ReplaceFile)
            {
                using Stream content = operation.OpenContent?.Invoke()
                    ?? throw new ArgumentException("NTFS add/replace requires a captured content stream.");
                if (content.Length < 0 || content.Length > image.Device.Length)
                {
                    throw new IOException("NTFS imported file exceeds the bounded volume.");
                }
                available = checked(available + Allocate(existing?.Length ?? 0) - Allocate(content.Length) - cluster * 8);
                entries[request.Path] = existing is null
                    ? new(request.Path, false, content.Length, -1, default, default, default, default, 0, null, addedSecurity!, [], null)
                    : existing with { Length = content.Length };
            }
            else if (request.Kind == ImageChangeKind.CreateDirectory)
            {
                available = checked(available - cluster * 8);
                entries.Add(request.Path, new(request.Path, true, 0, -1, default, default, default, default, 0, null, addedSecurity!, [], null));
            }
            else if (request.Kind == ImageChangeKind.Rename)
            {
                available = checked(available - cluster * 8);
                Rename(entries, request.Path, Parent(request.Path) + "/" + request.NewName);
            }
            else if (request.Kind == ImageChangeKind.Delete)
            {
                foreach (string path in entries.Keys.Where(path => Within(path, request.Path)).ToArray())
                {
                    entries.Remove(path);
                }
            }
            if (available < 0)
            {
                throw new IOException("Insufficient NTFS free space for staged data and conservative MFT/index/security allocation margins.");
            }
        }
    }

    internal void ApplyToStream(Stream stream, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
    {
        Dictionary<string, Entry> expected;
        using (var fs = Open(stream)) { expected = Capture(fs, token); }
        for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            StagedWriteOperation operation = operations[operationIndex];
            token.ThrowIfCancellationRequested();
            ImageChangeRequest request = operation.Request;
            using var fs = Open(stream);
            string path = ParserPath(request.Path);
            byte[]? addedSecurity = request.Kind is ImageChangeKind.AddFile or ImageChangeKind.CreateDirectory
                ? PortableSecurityDescriptor.Parse(expected[Parent(request.Path).Length == 0 ? "/" : Parent(request.Path)].Security)
                    .Inherit(request.Kind == ImageChangeKind.CreateDirectory).ToBinary()
                : null;
            switch (request.Kind)
            {
                case ImageChangeKind.AddFile:
                case ImageChangeKind.ReplaceFile:
                    using (Stream content = operation.OpenContent?.Invoke()
                        ?? throw new ArgumentException("NTFS add/replace requires captured content."))
                    using (Stream target = fs.OpenFile(path,
                        request.Kind == ImageChangeKind.AddFile ? FileMode.CreateNew : FileMode.Create,
                        FileAccess.Write, new NewFileOptions { CreateShortNames = false, Compressed = false }))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = content.Read(buffer)) != 0)
                        {
                            token.ThrowIfCancellationRequested();
                            target.Write(buffer, 0, read);
                        }
                        target.Flush();
                    }
                    if (request.Kind == ImageChangeKind.AddFile)
                    {
                        expected.Add(request.Path, ReadEntry(fs, request.Path) with { Security = addedSecurity! });
                    }
                    else { expected[request.Path] = expected[request.Path] with { Length = fs.GetFileLength(path) }; }
                    break;
                case ImageChangeKind.CreateDirectory:
                    fs.CreateDirectory(path, new NewFileOptions { CreateShortNames = false, Compressed = false });
                    expected.Add(request.Path, ReadEntry(fs, request.Path) with { Security = addedSecurity! });
                    break;
                case ImageChangeKind.Delete:
                    if (fs.DirectoryExists(path)) { fs.DeleteDirectory(path, true); }
                    else { fs.DeleteFile(path); }
                    foreach (string deleted in expected.Keys.Where(candidate => Within(candidate, request.Path)).ToArray())
                    {
                        expected.Remove(deleted);
                    }
                    break;
                case ImageChangeKind.Rename:
                    string renamed = Parent(request.Path) + "/" + request.NewName;
                    bool directory = expected[request.Path].Directory;
                    if (renamed.Equals(request.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        string temporary = ParserPath(Parent(request.Path) + "/.sw-" + Guid.NewGuid().ToString("N"));
                        if (directory) { fs.MoveDirectory(path, temporary); fs.MoveDirectory(temporary, ParserPath(renamed)); }
                        else { fs.MoveFile(path, temporary); fs.MoveFile(temporary, ParserPath(renamed)); }
                    }
                    else if (directory) { fs.MoveDirectory(path, ParserPath(renamed)); }
                    else { fs.MoveFile(path, ParserPath(renamed)); }
                    Rename(expected, request.Path, renamed);
                    break;
                default:
                    throw new NotSupportedException("NTFS metadata editing is not implemented.");
            }
            foreach (Entry entry in expected.Values.OrderByDescending(entry =>
                entry.Path == "/" ? 0 : entry.Path.Count(character => character == '/')))
            {
                token.ThrowIfCancellationRequested();
                Entry current = ReadEntry(fs, entry.Path);
                if (entry.Path != "/" && current.ShortName != entry.ShortName)
                {
                    fs.SetShortName(ParserPath(entry.Path), entry.ShortName);
                    current = ReadEntry(fs, entry.Path);
                }
                if (current.Created != entry.Created || current.Accessed != entry.Accessed
                    || current.Written != entry.Written || current.Changed != entry.Changed
                    || current.Attributes != entry.Attributes)
                {
                    fs.SetFileStandardInformation(ParserPath(entry.Path), new WindowsFileInformation
                    {
                        CreationTime = entry.Created,
                        LastAccessTime = entry.Accessed,
                        LastWriteTime = entry.Written,
                        ChangeTime = entry.Changed,
                        FileAttributes = entry.Attributes,
                    });
                    current = ReadEntry(fs, entry.Path);
                }
                if (!current.FileNameTimes.AsSpan().SequenceEqual(entry.FileNameTimes))
                {
                    fs.SetFileNameTimestamps(ParserPath(entry.Path), entry.FileNameTimes);
                }
                if (entry.ShortNameTimes is not null
                    && !(current.ShortNameTimes ?? []).AsSpan().SequenceEqual(entry.ShortNameTimes))
                {
                    fs.SetFileNameTimestamps(ParserPath(Parent(entry.Path) + "/" + entry.ShortName), entry.ShortNameTimes);
                }
            }
        }
        stream.Flush();
        using var device = new StreamBlockDevice(stream, new ParserContext());
        _ = CaptureAllocation(device, token);
        Compare(expected, Capture(device, token));
    }

    internal void ValidatePreservedMetadata(IImageSession source, IImageSession output,
        IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
    {
        NtfsWriteSnapshot sourceAllocation = CaptureAllocation(source.Device, token);
        NtfsWriteSnapshot outputAllocation = CaptureAllocation(output.Device, token);
        if (sourceAllocation.PassiveMetadata.Count != outputAllocation.PassiveMetadata.Count
            || sourceAllocation.PassiveMetadata.Any(pair => !outputAllocation.PassiveMetadata.TryGetValue(pair.Key, out byte[]? value)
                || !pair.Value.AsSpan().SequenceEqual(value)
                || !sourceAllocation.SecurityDescriptors[pair.Key].AsSpan()
                    .SequenceEqual(outputAllocation.SecurityDescriptors[pair.Key])))
        {
            throw new ImageFormatException("NTFS output changed protected passive repair/deleted records or their allocation bytes.");
        }
        Dictionary<string, Entry> expected = Capture(source.Device, token);
        var originalIds = expected.Values.Select(entry => entry.Id).ToHashSet();
        Dictionary<string, Entry> actual = Capture(output.Device, token);
        for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            StagedWriteOperation operation = operations[operationIndex];
            token.ThrowIfCancellationRequested();
            ImageChangeRequest request = operation.Request;
            switch (request.Kind)
            {
                case ImageChangeKind.AddFile:
                case ImageChangeKind.CreateDirectory:
                    string parent = Parent(request.Path);
                    byte[] security = PortableSecurityDescriptor.Parse(expected[parent.Length == 0 ? "/" : parent].Security)
                        .Inherit(request.Kind == ImageChangeKind.CreateDirectory).ToBinary();
                    string final = FinalPath(request.Path, operations, operationIndex);
                    if (final.Length == 0)
                    {
                        expected[request.Path] = new(request.Path, request.Kind == ImageChangeKind.CreateDirectory,
                            0, -1, default, default, default, default, 0, null, security, [], null);
                        break;
                    }
                    if (!actual.TryGetValue(final, out Entry? added))
                    {
                        throw new ImageFormatException("NTFS output is missing an added entry.");
                    }
                    expected[request.Path] = added with { Path = request.Path, Security = security };
                    break;
                case ImageChangeKind.ReplaceFile:
                    if (expected.TryGetValue(request.Path, out Entry? replaced))
                    {
                        using Stream content = operation.OpenContent!();
                        expected[request.Path] = replaced with { Length = content.Length };
                    }
                    break;
                case ImageChangeKind.Delete:
                    foreach (string deleted in expected.Keys.Where(path => Within(path, request.Path)).ToArray()) { expected.Remove(deleted); }
                    break;
                case ImageChangeKind.Rename:
                    Rename(expected, request.Path, Parent(request.Path) + "/" + request.NewName);
                    break;
            }
        }
        Compare(expected, actual);
        if (HashSystemStream(source.Device, "$LogFile", token) != HashSystemStream(output.Device, "$LogFile", token))
        {
            throw new ImageFormatException("NTFS output changed the qualified clean log stream; replay, reset and normalization are not implemented.");
        }
        foreach (Entry entry in expected.Values.Where(entry => originalIds.Contains(entry.Id)))
        {
            long record = entry.Id & 0x0000ffffffffffffL;
            if (!sourceAllocation.SecurityDescriptors[record].AsSpan()
                .SequenceEqual(outputAllocation.SecurityDescriptors[record]))
            {
                throw new ImageFormatException("NTFS output changed original stored security-descriptor bytes: " + entry.Path);
            }
            if (!sourceAllocation.StandardInformation[record].AsSpan()
                .SequenceEqual(outputAllocation.StandardInformation[record]))
            {
                throw new ImageFormatException("NTFS output changed original standard-information bytes: " + entry.Path);
            }
        }
        if (!Bounds.Read(source.Device, 0, 512).AsSpan().SequenceEqual(Bounds.Read(output.Device, 0, 512))
            || !Bounds.Read(source.Device, source.Device.Length - 512, 512).AsSpan()
                .SequenceEqual(Bounds.Read(output.Device, output.Device.Length - 512, 512)))
        {
            throw new ImageFormatException("NTFS output changed primary or backup boot-sector bytes.");
        }
    }

    private static string FinalPath(string path, IReadOnlyList<StagedWriteOperation> operations, int current)
    {
        for (int index = current + 1; index < operations.Count; index++)
        {
            ImageChangeRequest request = operations[index].Request;
            if (request.Kind == ImageChangeKind.Delete && Within(path, request.Path)) { return ""; }
            if (request.Kind == ImageChangeKind.Rename && Within(path, request.Path))
            {
                path = Parent(request.Path) + "/" + request.NewName + path[request.Path.Length..];
            }
        }
        return path;
    }

    private static void RequireLayout(IReadOnlyBlockDevice device, uint hiddenSectors)
    {
        byte[] boot = Bounds.Read(device, 0, 512);
        if (!boot.AsSpan(3, 8).SequenceEqual("NTFS    "u8) || device.LogicalSectorSize != 512
            || device.Length > RawFatImageWriter.MaximumImageBytes
            || BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11)) != 512
            || BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(28)) != hiddenSectors)
        {
            throw new NotSupportedException("NTFS writing requires a bounded 512-byte-sector volume with its original matching absolute hidden-sector count, no larger than 64 GiB.");
        }
    }

    private static Dictionary<string, Entry> Capture(IReadOnlyBlockDevice device, CancellationToken token)
    {
        var context = new ParserContext();
        return context.Run(() =>
        {
            using var fs = Open(new DeviceStream(device, context));
            return Capture(fs, token);
        }, token);
    }

    private static string HashSystemStream(IReadOnlyBlockDevice device, string path, CancellationToken token)
    {
        var context = new ParserContext();
        return context.Run(() =>
        {
            using var fs = Open(new DeviceStream(device, context));
            using Stream stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536];
            int count;
            while ((count = stream.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, count);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }, token);
    }

    private static NtfsFileSystem Open(Stream stream)
    {
        var fs = new NtfsFileSystem(stream);
        fs.NtfsOptions.HideHiddenFiles = false;
        fs.NtfsOptions.HideSystemFiles = false;
        fs.NtfsOptions.HideMetafiles = true;
        fs.NtfsOptions.HideDosFileNames = true;
        fs.NtfsOptions.CreateObjectIds = false;
        return fs;
    }

    private static Dictionary<string, Entry> Capture(NtfsFileSystem fs, CancellationToken token)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase) { ["/"] = ReadEntry(fs, "/") };
        long metadataBytes = entries["/"].Security.Length + 256;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push(("/", 0));
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (directory.Depth > 128) { throw new ImageFormatException("NTFS editable tree exceeds the depth budget."); }
            foreach (DiscFileSystemInfo info in fs.GetDirectoryInfo(ParserPath(directory.Path)).GetFileSystemInfos())
            {
                if (!ImageEditNames.IsSupportedName(info.Name))
                {
                    throw new NotSupportedException("NTFS editing requires the declared NFC BMP filename subset.");
                }
                string path = directory.Path.TrimEnd('/') + "/" + info.Name;
                if (path.Length > 240 || entries.Count >= 100000)
                {
                    throw new NotSupportedException("NTFS editable tree exceeds the path/entry budget.");
                }
                Entry entry = ReadEntry(fs, path);
                metadataBytes = checked(metadataBytes + entry.Security.Length + path.Length * 2L + 256);
                if (metadataBytes > 64 * 1024 * 1024)
                {
                    throw new NotSupportedException("NTFS editable metadata exceeds the 64 MiB snapshot budget.");
                }
                if ((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Compressed
                    | FileAttributes.Encrypted | FileAttributes.SparseFile)) != 0)
                {
                    throw new NotSupportedException("NTFS sparse/compressed/encrypted/reparse entries are not qualified for modification.");
                }
                if (!entries.TryAdd(path, entry)) { throw new ImageFormatException("Ambiguous NTFS editable path."); }
                if (entry.Directory) { pending.Push((path, directory.Depth + 1)); }
            }
        }
        return entries;
    }

    private static Entry ReadEntry(NtfsFileSystem fs, string path)
    {
        string parser = ParserPath(path);
        WindowsFileInformation info = fs.GetFileStandardInformation(parser);
        bool directory = (info.FileAttributes & FileAttributes.Directory) != 0;
        PortableSecurityDescriptor security = PortableSecurityDescriptor.Parse(fs.GetSecurityBytes(parser));
        security.ValidateSupportedInheritance();
        string? shortName = path == "/" ? null : fs.GetShortName(parser);
        byte[]? shortNameTimes = shortName is not null && shortName != path[(path.LastIndexOf('/') + 1)..]
            ? fs.GetFileNameTimestamps(ParserPath(Parent(path) + "/" + shortName)) : null;
        return new(path, directory, directory ? 0 : fs.GetFileLength(parser), fs.GetFileId(parser),
            info.CreationTime, info.LastAccessTime, info.LastWriteTime, info.ChangeTime, info.FileAttributes,
            shortName, security.ToBinary(), fs.GetFileNameTimestamps(parser), shortNameTimes);
    }

    private static void Compare(Dictionary<string, Entry> expected, Dictionary<string, Entry> actual)
    {
        if (expected.Count != actual.Count) { throw new ImageFormatException("NTFS output metadata tree differs from the expected staged operations."); }
        foreach ((string path, Entry original) in expected)
        {
            if (!actual.TryGetValue(path, out Entry? saved) || original.Path != saved.Path
                || original.Directory != saved.Directory || original.Length != saved.Length || original.Id != saved.Id
                || original.Created != saved.Created || original.Accessed != saved.Accessed
                || original.Written != saved.Written || original.Changed != saved.Changed
                || original.Attributes != saved.Attributes || original.ShortName != saved.ShortName
                || !original.Security.AsSpan().SequenceEqual(saved.Security)
                || !original.FileNameTimes.AsSpan().SequenceEqual(saved.FileNameTimes)
                || !(original.ShortNameTimes ?? []).AsSpan().SequenceEqual(saved.ShortNameTimes ?? []))
            {
                throw new ImageFormatException("NTFS output changed supported identity, spelling, timestamp, attribute, alias or security metadata: " + path);
            }
        }
    }

    private static void Rename(Dictionary<string, Entry> entries, string path, string renamed)
    {
        foreach (string original in entries.Keys.Where(candidate => Within(candidate, path)).ToArray())
        {
            Entry entry = entries[original];
            entries.Remove(original);
            string updated = renamed + original[path.Length..];
            entries.Add(updated, entry with { Path = updated });
        }
    }

    private static bool Within(string candidate, string path) =>
        candidate.Equals(path, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase);
    private static string Parent(string path) => path[..path.LastIndexOf('/')];
    private static string ParserPath(string path) => path == "/" ? "\\" : path.Replace('/', '\\');
}
