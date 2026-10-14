using System.Buffers.Binary;
using SectorWorks.Core;

namespace SectorWorks.Storage;

public sealed class RawFatImageWriter : IStagedImageWriter
{
    public const long MaximumImageBytes = 64L * 1024 * 1024 * 1024;
    public const long MaximumFatTableBytes = 8L * 1024 * 1024;

    public Capability Qualify(IImageSession image)
    {
        try { return QualifyCore(image); }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException or OverflowException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(false, "FAT write qualification failed; read-only inspection remains available: " + error.Message);
        }
    }

    private static Capability QualifyCore(IImageSession image)
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
        if (checked((long)sectors * 512) != image.Device.Length || BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(28)) != 0
            || (fs.Name == "FAT12" && (image.Device.Length != 1_474_560
                || BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(24)) != 18
                || BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(26)) != 2)))
        {
            return new(false, "Only exact-sized filesystem-only volumes with zero hidden sectors are qualified; FAT12 writes are limited to conventional 1,474,560-byte PC floppy layouts. 360K/720K and legacy variants remain read-only.");
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
                if (++count > 100000 || entry.IsLink || !FatEditNames.IsSupported(entry.Name)
                    || (entry.ShortName is not null && entry.Name != entry.ShortName))
                {
                    return new(false, "FAT writing is qualified only for uppercase DOS 8.3 trees (A-Z, 0-9, underscore and hyphen), without long filenames.");
                }
                if (entry.IsDirectory) { pending.Push((entry.Path, item.Depth + 1)); }
            }
        }
        return new(true, "Staged DOS 8.3 editing and validated Save As to a new unpartitioned raw FAT image; no source writes or bootability guarantee.");
    }

    public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        RequireQualified(image);
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
        var rootNames = image.Volumes[0].FileSystem.ReadDirectory("/").Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (StagedWriteOperation operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageChangeRequest r = operation.Request;
            string path = r.Path;
            if (r.Path.LastIndexOf('/') == 0)
            {
                string name = r.Path[1..];
                if (r.Kind is ImageChangeKind.AddFile or ImageChangeKind.CreateDirectory) { rootNames.Add(name); }
                if (r.Kind == ImageChangeKind.Delete) { rootNames.Remove(name); }
                if (r.Kind == ImageChangeKind.Rename) { rootNames.Remove(name); rootNames.Add(r.NewName!); }
                if (fixedRootEntries != 0 && rootNames.Count >= fixedRootEntries)
                {
                    throw new IOException("The fixed FAT root directory has no remaining entry slots (one slot reserved for the volume label).");
                }
            }
            long OldSize() => sizes.GetValueOrDefault(path);
            long Allocated(long bytes) => checked((bytes + cluster - 1) / cluster * cluster);
            switch (r.Kind)
            {
                case ImageChangeKind.AddFile:
                case ImageChangeKind.ReplaceFile:
                    using (Stream content = operation.OpenContent!())
                    {
                        available = checked(available + Allocated(OldSize()) - Allocated(content.Length) - cluster);
                        sizes[path] = content.Length;
                    }
                    break;
                case ImageChangeKind.CreateDirectory:
                    available = checked(available - cluster * 2);
                    break;
                case ImageChangeKind.Delete:
                    // Directory deletion credits no space: avoiding optimistic allocation estimates.
                    available = checked(available + Allocated(OldSize()));
                    foreach (string deleted in sizes.Keys.Where(key => key.Equals(path, StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToArray()) { sizes.Remove(deleted); }
                    break;
                case ImageChangeKind.Rename:
                    available = checked(available - cluster);
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
        var changedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StagedWriteOperation operation in operations)
        {
            // Directory moves leave stale parent-entry IDs in DiscUtils's directory cache.
            using var fs = new DiscUtils.Fat.FatFileSystem(stream);
            cancellationToken.ThrowIfCancellationRequested();
            ImageChangeRequest request = operation.Request;
            string path = Native(request.Path);
            switch (request.Kind)
            {
                case ImageChangeKind.CreateDirectory:
                    fs.CreateDirectory(path);
                    changedDirectories.Add(request.Path);
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
                    changedDirectories.RemoveWhere(directory => directory.Equals(request.Path, StringComparison.OrdinalIgnoreCase)
                        || directory.StartsWith(request.Path + "/", StringComparison.OrdinalIgnoreCase));
                    if (fs.DirectoryExists(path))
                    {
                        fs.DeleteDirectory(path, true);
                    }
                    else
                    {
                        fs.DeleteFile(path);
                    }
                    break;
                case ImageChangeKind.Rename:
                    string renamed = Native(request.Path[..(request.Path.LastIndexOf('/') + 1)] + request.NewName);
                    DateTime modified = fs.GetLastWriteTime(path);
                    bool directory = fs.DirectoryExists(path);
                    if (directory)
                    {
                        fs.MoveDirectory(path, renamed);
                        string renamedPath = renamed.Replace('\\', '/');
                        foreach (string oldPath in changedDirectories.Where(candidate => candidate.Equals(request.Path, StringComparison.OrdinalIgnoreCase)
                            || candidate.StartsWith(request.Path + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
                        {
                            changedDirectories.Remove(oldPath);
                            changedDirectories.Add(renamedPath + oldPath[request.Path.Length..]);
                        }
                        changedDirectories.Add(renamedPath);
                    }
                    else
                    {
                        fs.MoveFile(path, renamed);
                    }
                    // DiscUtils's entry copy omits the modification date during both file and directory moves.
                    fs.SetLastWriteTime(renamed, modified);
                    break;
                default:
                    throw new NotSupportedException("Metadata changes are reserved, not implemented.");
            }
        }
        if (changedDirectories.Count != 0)
        {
            stream.Flush();
            using var device = new StreamBlockDevice(stream, new ParserContext());
            using var reader = new FatFileSystem(device);
            reader.CompleteChangedDirectoryDates(stream, changedDirectories, cancellationToken);
        }
        FatWriteMetadata.UpdateFat32FreeSpace(stream, cancellationToken);
        stream.Flush(true);
    }

    private void RequireQualified(IImageSession image)
    {
        Capability capability = Qualify(image);
        if (!capability.Available)
        {
            throw new NotSupportedException(capability.Reason);
        }
    }

    private static string Native(string path) => path.Replace('/', '\\');

}
