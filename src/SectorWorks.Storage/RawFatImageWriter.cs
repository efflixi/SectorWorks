using System.Buffers.Binary;
using SectorWorks.Core;

namespace SectorWorks.Storage;

public sealed class RawFatImageWriter : IStagedImageWriter
{
    public const long MaximumImageBytes = 64L * 1024 * 1024 * 1024;
    public const long MaximumFatTableBytes = 8L * 1024 * 1024;

    public Capability Qualify(IImageSession image) => Qualify(image, 0);

    public Capability Qualify(IImageSession image, uint expectedHiddenSectors)
    {
        try { return QualifyCore(image, expectedHiddenSectors); }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(false, "FAT write qualification failed; read-only inspection remains available: " + error.Message);
        }
    }

    private static Capability QualifyCore(IImageSession image, uint expectedHiddenSectors)
    {
        if (image.Warnings.Count != 0)
        {
            return new(false, "Images with detection or structural warnings remain read-only; no implicit repair or normalization is performed.");
        }
        if (image.Metadata.Format != "Raw" || image.Partitions.Count != 0 || image.Volumes.Count != 1)
        {
            return new(false, "Phase 2 Save As supports only unpartitioned raw FAT images; other stacks remain read-only.");
        }
        IReadOnlyFileSystem fs = image.Volumes[0].FileSystem;
        if (fs.Name is not ("FAT12" or "FAT16" or "FAT32"))
        {
            return new(false, "This filesystem has no qualified Phase 2 writer. NTFS writes and ISO rebuild are not enabled.");
        }
        byte[] boot = new byte[512];
        int read = image.Device.Read(0, boot);
        int labelOffset = fs.Name == "FAT32" ? 66 : 38;
        if (read != boot.Length || boot[510] != 0x55 || boot[511] != 0xaa
            || boot[labelOffset] != 0x29 || BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11)) != 512
            || boot[16] != 2 || (fs.Name == "FAT32" && BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(40)) != 0))
        {
            return new(false, "Only conventional 512-byte-sector mirrored FAT with an extended BPB is qualified. Legacy floppy reads remain read-only.");
        }
        long declaredFatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22));
        if (declaredFatSectors == 0) { declaredFatSectors = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36)); }
        if (image.Device.Length > MaximumImageBytes || checked(declaredFatSectors * 512) > MaximumFatTableBytes)
        {
            return new(false, "Raw FAT writes are limited to 64 GiB images and 8 MiB per declared FAT table to bound dependency allocations.");
        }
        uint sectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(19));
        if (sectors == 0) { sectors = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(32)); }
        if (checked((long)sectors * 512) != image.Device.Length || BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(28)) != expectedHiddenSectors
            || (fs.Name == "FAT12" && (image.Device.Length != 1_474_560
                || BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(24)) != 18
                || BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(26)) != 2)))
        {
            return new(false, "Only exact-sized bounded FAT volumes with the expected hidden-sector count are qualified (zero for filesystem-only raw images); FAT12 writes are limited to conventional 1,474,560-byte PC floppy layouts. 360K/720K and legacy variants remain read-only.");
        }
        if (!FatWriteMetadata.HasConventionalMetadata(image, boot, fs.Name == "FAT32"))
        {
            return new(false, "FAT writing requires consistent root/boot volume labels and valid FAT32 FSInfo/backup boot metadata. No source repairs are performed.");
        }
        if (fs is not FatFileSystem fat) { return new(false, "This filesystem reader is not qualified with the raw FAT writer."); }
        fat.ValidateWritableAllocation();
        var pending = new Stack<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(("/", 0));
        int count = 0;
        while (pending.TryPop(out var item))
        {
            if (item.Depth > 128 || !visited.Add(item.Path))
            {
                return new(false, "FAT tree exceeds the qualified traversal limits.");
            }
            foreach (FileEntry entry in fs.ReadDirectory(item.Path))
            {
                if (++count > 100000 || entry.IsLink || !FatEditNames.IsSupportedName(entry.Name)
                    || entry.ShortName is null || !FatFileSystem.IsSafeAlias(entry.ShortName))
                {
                    return new(false, "FAT writing requires the qualified NFC BMP filename subset and unambiguous ASCII short aliases.");
                }
                if (entry.IsDirectory) { pending.Push((entry.Path, item.Depth + 1)); }
            }
        }
        return new(true, "Staged FAT editing with checked long names, mixed case and NFC BMP Unicode, validated Save As to a new unpartitioned raw image; no source writes or bootability guarantee.");
    }

    public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
        => ValidateCapacity(image, operations, 0, cancellationToken);

    public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, uint expectedHiddenSectors,
        CancellationToken cancellationToken)
    {
        RequireQualified(image, expectedHiddenSectors);
        var fs = (FatFileSystem)image.Volumes[0].FileSystem;
        long available = fs.ValidateWritableAllocation(cancellationToken);
        long cluster = long.Parse(fs.Properties["Cluster size"], System.Globalization.CultureInfo.InvariantCulture);
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push(("/", 0));
        int entries = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directory.Depth > 128) { throw new ImageFormatException("Capacity traversal exceeds depth limits."); }
            foreach (FileEntry entry in fs.ReadDirectory(directory.Path, cancellationToken))
            {
                if (++entries > 100000) { throw new ImageFormatException("Capacity traversal exceeds entry limits."); }
                if (entry.IsDirectory) { pending.Push((entry.Path, directory.Depth + 1)); }
                else { sizes.Add(entry.Path, entry.Length); }
            }
        }
        byte[] boot = new byte[512];
        image.Device.Read(0, boot);
        int fixedRootEntries = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
        var rootNames = fs.RootNameSlots(cancellationToken);
        foreach (StagedWriteOperation operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageChangeRequest r = operation.Request;
            string path = r.Path;
            if (r.Path.LastIndexOf('/') == 0)
            {
                string name = r.Path[1..];
                if (r.Kind is ImageChangeKind.AddFile or ImageChangeKind.CreateDirectory) { rootNames.Add(name, FatEditNames.RequiredDirectorySlots(name)); }
                if (r.Kind == ImageChangeKind.Delete) { rootNames.Remove(name); }
                if (r.Kind == ImageChangeKind.Rename) { rootNames.Remove(name); rootNames.Add(r.NewName!, FatEditNames.RequiredDirectorySlots(r.NewName!)); }
                if (fixedRootEntries != 0 && rootNames.Values.Sum() >= fixedRootEntries)
                {
                    throw new IOException("The fixed FAT root directory has insufficient long-name entry slots (one slot reserved for the volume label).");
                }
            }
            long OldSize() => sizes.GetValueOrDefault(path);
            long Allocated(long bytes) => checked((bytes + cluster - 1) / cluster * cluster);
            string desiredName = r.Kind == ImageChangeKind.Rename ? r.NewName! : path[(path.LastIndexOf('/') + 1)..];
            long directoryMargin = r.Kind == ImageChangeKind.Delete ? 0 : Allocated(FatEditNames.RequiredDirectorySlots(desiredName) * 32L);
            switch (r.Kind)
            {
                case ImageChangeKind.AddFile:
                case ImageChangeKind.ReplaceFile:
                    using (Stream content = operation.OpenContent!())
                    {
                        available = checked(available + Allocated(OldSize()) - Allocated(content.Length) - directoryMargin);
                        sizes[path] = content.Length;
                    }
                    break;
                case ImageChangeKind.CreateDirectory:
                    available = checked(available - cluster - directoryMargin);
                    break;
                case ImageChangeKind.Delete:
                    // Directory deletion credits no space: avoiding optimistic allocation estimates.
                    available = checked(available + Allocated(OldSize()));
                    foreach (string deleted in sizes.Keys.Where(key => key.Equals(path, StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToArray()) { sizes.Remove(deleted); }
                    break;
                case ImageChangeKind.Rename:
                    available = checked(available - directoryMargin);
                    string renamed = r.Path[..(r.Path.LastIndexOf('/') + 1)] + r.NewName;
                    foreach (string oldPath in sizes.Keys.Where(key => key.Equals(path, StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
                    {
                        long size = sizes[oldPath];
                        sizes.Remove(oldPath);
                        sizes[renamed + oldPath[path.Length..]] = size;
                    }
                    break;
            }
            if (available < 0)
            {
                throw new IOException("Insufficient FAT free space for the staged operations and directory allocation margin.");
            }
        }
    }

    public void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        using (IImageSession copy = new ImageOpener().OpenReadOnly(copyPath, cancellationToken))
        {
            RequireQualified(copy);
        }
        using var stream = new FileStream(copyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        ApplyToStream(stream, operations, cancellationToken);
        stream.Flush(true);
    }

    public void ApplyToStream(Stream stream, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        Dictionary<string, FatFileSystem.PreservedEntry> expected;
        stream.Flush();
        using (var device = new StreamBlockDevice(stream, new ParserContext()))
        using (var reader = new FatFileSystem(device))
        {
            expected = reader.CapturePreservation(cancellationToken);
        }
        foreach (StagedWriteOperation operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageChangeRequest request = operation.Request;
            string parent;
            string name;
            string alias;
            string path;
            byte[]? original = null;
            bool hadLongName = false;
            List<(long Offset, byte[] Bytes)> ancestors;
            stream.Flush();
            using (var device = new StreamBlockDevice(stream, new ParserContext()))
            using (var reader = new FatFileSystem(device))
            {
                if (request.Kind is ImageChangeKind.AddFile or ImageChangeKind.CreateDirectory)
                {
                    string requestedParent = request.Path[..request.Path.LastIndexOf('/')];
                    string canonicalParent = reader.CanonicalPath(requestedParent.Length == 0 ? "/" : requestedParent, cancellationToken);
                    request = request with { Path = canonicalParent.TrimEnd('/') + request.Path[request.Path.LastIndexOf('/')..] };
                }
                else
                {
                    request = request with { Path = reader.CanonicalPath(request.Path, cancellationToken) };
                }
                parent = request.Path[..request.Path.LastIndexOf('/')];
                name = request.Kind == ImageChangeKind.Rename ? request.NewName! : request.Path[(request.Path.LastIndexOf('/') + 1)..];
                ancestors = reader.AncestorMetadata(request.Path, cancellationToken);
                if (request.Kind is ImageChangeKind.AddFile or ImageChangeKind.CreateDirectory)
                {
                    alias = reader.ChooseAlias(parent, name, null, cancellationToken);
                    path = reader.AliasPath(parent.Length == 0 ? "/" : parent, cancellationToken).TrimEnd('\\') + "\\" + alias;
                }
                else
                {
                    path = reader.AliasPath(request.Path, cancellationToken);
                    original = reader.EntryBytes(request.Path, cancellationToken);
                    hadLongName = reader.NameSlotCount(request.Path, cancellationToken) > 1;
                    alias = path[(path.LastIndexOf('\\') + 1)..];
                    if (request.Kind == ImageChangeKind.Rename)
                    {
                        bool caseOnly = name.Equals(reader.DisplayName(request.Path, cancellationToken), StringComparison.OrdinalIgnoreCase);
                        if (!caseOnly) { alias = reader.ChooseAlias(parent, name, request.Path, cancellationToken); }
                        reader.SetDisplayName(stream, request.Path, name, alias, cancellationToken, original,
                            forceLongName: caseOnly && hadLongName, preserveAlias: caseOnly);
                        stream.Flush();
                        reader.RecordExpectedChange(expected, request, cancellationToken);
                        continue;
                    }
                    if (request.Kind == ImageChangeKind.ReplaceFile) { name = reader.DisplayName(request.Path, cancellationToken); }
                    reader.RemoveLongName(stream, request.Path, cancellationToken);
                }
            }
            using (var fs = new DiscUtils.Fat.FatFileSystem(stream))
            {
                switch (request.Kind)
                {
                    case ImageChangeKind.CreateDirectory:
                        fs.CreateDirectory(path);
                        break;
                    case ImageChangeKind.AddFile:
                    case ImageChangeKind.ReplaceFile:
                        FileAttributes? attributes = request.Kind == ImageChangeKind.ReplaceFile ? fs.GetAttributes(path) : null;
                        // DiscUtils truncation to zero leaks the original first cluster; release the chain explicitly.
                        if (request.Kind == ImageChangeKind.ReplaceFile) { fs.DeleteFile(path); }
                        using (Stream input = operation.OpenContent!())
                        using (Stream output = fs.OpenFile(path, FileMode.CreateNew, FileAccess.Write))
                        {
                            byte[] buffer = new byte[65536];
                            int count;
                            while ((count = input.Read(buffer)) != 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, count);
                            }
                            output.Flush();
                        }
                        if (attributes.HasValue) { fs.SetAttributes(path, attributes.Value); }
                        break;
                    case ImageChangeKind.Delete:
                        if (fs.DirectoryExists(path))
                        {
                            fs.DeleteDirectory(path, true);
                        }
                        else
                        {
                            fs.DeleteFile(path);
                        }
                        break;
                    default:
                        throw new NotSupportedException("Metadata changes are reserved, not implemented.");
                }
            }
            FatFileSystem.RestoreAncestorMetadata(stream, ancestors);
            if (request.Kind != ImageChangeKind.Delete)
            {
                stream.Flush();
                using var device = new StreamBlockDevice(stream, new ParserContext());
                using var reader = new FatFileSystem(device);
                reader.SetDisplayName(stream, path.Replace('\\', '/'), name, alias, cancellationToken, original,
                    replacement: request.Kind == ImageChangeKind.ReplaceFile, forceLongName: hadLongName);
                if (request.Kind == ImageChangeKind.CreateDirectory)
                {
                    reader.CompleteChangedDirectoryDates(stream, [request.Path], cancellationToken);
                }
            }
            stream.Flush();
            using (var device = new StreamBlockDevice(stream, new ParserContext()))
            using (var reader = new FatFileSystem(device))
            {
                reader.RecordExpectedChange(expected, request, cancellationToken);
            }
        }
        FatWriteMetadata.UpdateFat32FreeSpace(stream, cancellationToken);
        stream.Flush();
        using (var device = new StreamBlockDevice(stream, new ParserContext()))
        using (var reader = new FatFileSystem(device))
        {
            reader.ValidateWritableAllocation(cancellationToken);
            reader.ValidatePreservation(expected, cancellationToken);
        }
    }

    private void RequireQualified(IImageSession image, uint expectedHiddenSectors = 0)
    {
        Capability capability = Qualify(image, expectedHiddenSectors);
        if (!capability.Available)
        {
            throw new NotSupportedException(capability.Reason);
        }
    }

    public void ValidatePreservedMetadata(IReadOnlyFileSystem source, IReadOnlyFileSystem output,
        IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        if (source is not FatFileSystem original || output is not FatFileSystem saved)
        {
            throw new NotSupportedException("FAT metadata validation requires the qualified bounded FAT readers.");
        }
        saved.ValidateWritableAllocation(cancellationToken);
        saved.ValidateSourcePreservation(original, operations, cancellationToken);
    }

    public void ValidateOutput(IImageSession source, IImageSession output,
        IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        RequireQualified(source);
        RequireQualified(output);
        if (source.Device.Length != output.Device.Length
            || source.Volumes[0].FileSystem.Name != output.Volumes[0].FileSystem.Name)
        {
            throw new ImageFormatException("Raw FAT output changed its volume size or filesystem type.");
        }
        byte[] boot = Bounds.Read(source.Device, 0, 512);
        if (!boot.AsSpan().SequenceEqual(Bounds.Read(output.Device, 0, 512)))
        {
            throw new ImageFormatException("Raw FAT output changed its boot-sector bytes.");
        }
        if (source.Volumes[0].FileSystem.Name == "FAT32")
        {
            long backup = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(50)) * 512L;
            if (!Bounds.Read(source.Device, backup, 512).AsSpan().SequenceEqual(Bounds.Read(output.Device, backup, 512)))
            {
                throw new ImageFormatException("Raw FAT output changed its backup boot-sector bytes.");
            }
        }
        ValidatePreservedMetadata(source.Volumes[0].FileSystem, output.Volumes[0].FileSystem, operations, cancellationToken);
    }

}
