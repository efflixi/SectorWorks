using System.Buffers.Binary;
using System.Security.Cryptography;
using DiscUtils.Streams;
using SectorWorks.Core;

namespace SectorWorks.Storage;

/// <summary>Applies qualified volume edits only to a disposable, copied disk image.</summary>
public sealed class DiskImageWriter : IStagedImageWriter
{
    private readonly RawFatImageWriter _fat = new();

    public Capability Qualify(IImageSession image)
    {
        Capability[] results = image.Volumes.Select(volume => QualifyVolume(image, volume.Id)).ToArray();
        return results.FirstOrDefault(result => result.Available)
            ?? new(false, results.FirstOrDefault()?.Reason ?? "No qualified filesystem volume is available.");
    }

    public Capability QualifyVolume(IImageSession image, string volumeId)
    {
        try
        {
            RequireDisk(image);
            ImageVolume volume = FindVolume(image, volumeId);
            if (volume.FileSystem.Name is not ("FAT12" or "FAT16" or "FAT32"))
            {
                return new(false, volume.FileSystem.Name == "NTFS"
                    ? "Existing-volume NTFS Add/mkdir portability is blocked by the approved dependency's ACL inheritance (RawSecurityDescriptor on Linux), independently of formatting. No NTFS writer is qualified, even where individual modification operations run."
                    : "This filesystem has no qualified disk-stack writer.");
            }
            using var bounded = new FatVolumeSession(image, volume);
            Capability result = _fat.Qualify(bounded, bounded.HiddenSectors);
            return result.Available
                ? new(true, "Qualified bounded FAT staged editing and validated disk-image Save As; no source commit or bootability guarantee.")
                : result;
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException or InvalidOperationException or OverflowException or UnauthorizedAccessException)
        {
            return new(false, "Disk write qualification failed; inspection remains read-only: " + error.Message);
        }
    }

    public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        foreach (IGrouping<string, StagedWriteOperation> group in operations.GroupBy(operation => operation.Request.VolumeId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireVolume(image, group.Key);
            using var volume = new FatVolumeSession(image, FindVolume(image, group.Key));
            _fat.ValidateCapacity(volume, group.ToArray(), volume.HiddenSectors, cancellationToken);
        }
    }

    public void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        var ranges = new List<(long Offset, long Length)>();
        string format;
        string identity;
        string untouched;
        var volumeHashes = new Dictionary<string, string>();
        using (IImageSession copy = new ImageOpener().OpenReadOnly(copyPath, cancellationToken))
        {
            RequireDisk(copy);
            ValidateCapacity(copy, operations, cancellationToken);
            format = copy.Metadata.Format;
            identity = Identity(copy);
            foreach (string id in operations.Select(operation => operation.Request.VolumeId).Distinct())
            {
                ImageVolume volume = FindVolume(copy, id);
                ranges.Add((volume.Partition?.Offset ?? 0, volume.Partition?.Length ?? copy.Device.Length));
            }
            untouched = HashOutside(copy.Device, ranges, cancellationToken);
        }

        foreach (IGrouping<string, StagedWriteOperation> group in operations.GroupBy(operation => operation.Request.VolumeId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string scratch = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(copyPath))!,
                ".sectorworks-volume-" + Guid.NewGuid().ToString("N") + ".img");
            bool created = false;
            try
            {
                long offset;
                long length;
                uint hidden;
                int backup;
                byte[] boot;
                byte[]? backupBoot;
                using (IImageSession copy = new ImageOpener().OpenReadOnly(copyPath, cancellationToken))
                {
                    ImageVolume volume = FindVolume(copy, group.Key);
                    offset = volume.Partition?.Offset ?? 0;
                    length = volume.Partition?.Length ?? copy.Device.Length;
                    using var adapted = new FatVolumeSession(copy, volume);
                    hidden = adapted.HiddenSectors;
                    backup = adapted.BackupSector;
                    boot = Bounds.Read(adapted.Device, 0, 512);
                    backupBoot = backup > 0 ? Bounds.Read(adapted.Device, backup * 512L, 512) : null;
                    using var target = new FileStream(scratch, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    created = true;
                    Copy(adapted.Device, target, cancellationToken);
                    target.Flush(true);
                }
                using (var extracted = new FileStream(scratch, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    _fat.ApplyToStream(extracted, group.ToArray(), cancellationToken);
                    extracted.Flush(true);
                }
                using (IImageSession extracted = new ImageOpener().OpenReadOnly(scratch, cancellationToken))
                {
                    Capability qualified = _fat.Qualify(extracted, hidden);
                    if (!qualified.Available) { throw new ImageFormatException(qualified.Reason); }
                    if (!Bounds.Read(extracted.Device, 0, 512).AsSpan().SequenceEqual(boot)
                        || (backupBoot is not null && !Bounds.Read(extracted.Device, backup * 512L, 512).AsSpan().SequenceEqual(backupBoot)))
                    {
                        throw new ImageFormatException("FAT writer changed primary or backup boot-sector bytes.");
                    }
                }
                using (var extracted = new FileBlockDevice(scratch))
                {
                    volumeHashes.Add(group.Key, HashOutside(extracted, [], cancellationToken));
                }
                using var file = new FileStream(copyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using DiscUtils.VirtualDisk? disk = format switch
                {
                    "VHD" => new DiscUtils.Vhd.Disk(file, Ownership.None),
                    "VHDX" => new DiscUtils.Vhdx.Disk(file, Ownership.None),
                    _ => null,
                };
                Stream virtualDisk = disk is null ? file : disk.Content;
                using var changed = File.OpenRead(scratch);
                PatchChanged(virtualDisk, changed, offset, length, cancellationToken);
                // VHDX ContentStream.Flush is unimplemented upstream; payload and BAT writes are synchronous.
                if (format != "VHDX") { virtualDisk.Flush(); }
                disk?.Dispose();
                file.Flush(true);
            }
            catch (Exception error)
            {
                if (created) { DeleteScratch(scratch, error); }
                throw;
            }
            if (created) { DeleteScratch(scratch); }
        }

        using IImageSession output = new ImageOpener().OpenReadOnly(copyPath, cancellationToken);
        RequireDisk(output);
        if (identity != Identity(output) || untouched != HashOutside(output.Device, ranges, cancellationToken))
        {
            throw new ImageFormatException("Save As changed container identity, partition metadata or untouched virtual bytes.");
        }
        foreach (string id in operations.Select(operation => operation.Request.VolumeId).Distinct())
        {
            RequireVolume(output, id);
            ImageVolume volume = FindVolume(output, id);
            using var bounded = new PartitionBlockDevice(output.Device, volume.Partition?.Offset ?? 0,
                volume.Partition?.Length ?? output.Device.Length);
            if (volumeHashes[id] != HashOutside(bounded, [], cancellationToken))
            {
                throw new ImageFormatException("Reopened virtual volume bytes differ from the validated extracted-volume result.");
            }
        }
    }

    public void ValidateOutput(IImageSession source, IImageSession output,
        IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
    {
        RequireDisk(source);
        RequireDisk(output);
        if (Identity(source) != Identity(output))
        {
            throw new ImageFormatException("Output container identity or partition metadata differs from the source.");
        }
        var ranges = operations.Select(operation => operation.Request.VolumeId).Distinct().Select(id =>
        {
            ImageVolume volume = FindVolume(source, id);
            return (volume.Partition?.Offset ?? 0, volume.Partition?.Length ?? source.Device.Length);
        }).ToList();
        if (HashOutside(source.Device, ranges, cancellationToken) != HashOutside(output.Device, ranges, cancellationToken))
        {
            throw new ImageFormatException("Output changed virtual bytes outside the selected volumes.");
        }
        foreach (IGrouping<string, StagedWriteOperation> group in operations.GroupBy(operation => operation.Request.VolumeId))
        {
            RequireVolume(source, group.Key);
            RequireVolume(output, group.Key);
            ImageVolume original = FindVolume(source, group.Key);
            ImageVolume saved = FindVolume(output, group.Key);
            _fat.ValidatePreservedMetadata(original.FileSystem, saved.FileSystem, group.ToArray(), cancellationToken);
            using var originalVolume = new FatVolumeSession(source, original);
            using var savedVolume = new FatVolumeSession(output, saved);
            if (!Bounds.Read(originalVolume.Device, 0, 512).AsSpan().SequenceEqual(Bounds.Read(savedVolume.Device, 0, 512))
                || (originalVolume.BackupSector > 0 && !Bounds.Read(originalVolume.Device, originalVolume.BackupSector * 512L, 512)
                    .AsSpan().SequenceEqual(Bounds.Read(savedVolume.Device, originalVolume.BackupSector * 512L, 512))))
            {
                throw new ImageFormatException("Output changed primary or backup FAT boot-sector bytes.");
            }
        }
    }

    private static void DeleteScratch(string path, Exception? original = null)
    {
        try { File.Delete(path); }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Save As failed. Bounded-volume scratch could not be removed; residual temporary volume: " + path,
                original is null ? cleanup : new AggregateException(original, cleanup));
        }
    }

    private static void RequireDisk(IImageSession image)
    {
        if (image.Metadata.Format is not ("Raw" or "VHD" or "VHDX"))
        {
            throw new NotSupportedException("Only raw, fixed/dynamic VHD and fixed/dynamic VHDX disk stacks are qualified.");
        }
        if (image.Warnings.Count != 0)
        {
            throw new NotSupportedException("Disk structural warnings prevent writing; damaged backup GPT, hybrid/extended layouts and implicit repair are unsupported.");
        }
        if (image.Device.LogicalSectorSize != 512 || image.Device.Length > RawFatImageWriter.MaximumImageBytes)
        {
            throw new NotSupportedException("Disk writing currently requires 512-byte logical sectors and a virtual size no greater than 64 GiB; 4Kn remains unqualified.");
        }
        if (image.Partitions.Any(partition => partition.Table is not ("MBR" or "GPT")
            || partition.Offset % 512 != 0 || partition.Length % 512 != 0))
        {
            throw new NotSupportedException("Only bounded primary MBR and consistent GPT partitions are qualified.");
        }
        if (image.Metadata.Format != "Raw")
        {
            using var source = new FileBlockDevice(image.Metadata.Path);
            DiskWriteValidation.Validate(source, image.Metadata.Format);
        }
    }

    private void RequireVolume(IImageSession image, string id)
    {
        Capability capability = QualifyVolume(image, id);
        if (!capability.Available) { throw new NotSupportedException(capability.Reason); }
    }

    private static ImageVolume FindVolume(IImageSession image, string id) =>
        image.Volumes.SingleOrDefault(volume => volume.Id == id)
        ?? throw new NotSupportedException("The requested volume is not present in the disk image.");

    private static string Identity(IImageSession image) =>
        string.Join("|", image.Metadata.Format, image.Metadata.VirtualLength, image.Metadata.SectorSize,
            image.Metadata.Properties.GetValueOrDefault("VHD allocation"),
            image.Metadata.Properties.GetValueOrDefault("VHDX allocation"),
            image.Metadata.Properties.GetValueOrDefault("VHDX block size"),
            image.Metadata.Properties.GetValueOrDefault("Physical sector size"),
            string.Join(";", image.Partitions.Select(partition => partition.ToString())),
            ContainerMetadataIdentity(image));

    private static string ContainerMetadataIdentity(IImageSession image)
    {
        if (image.Metadata.Format == "Raw") { return ""; }
        using var source = new FileBlockDevice(image.Metadata.Path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (image.Metadata.Format == "VHD")
        {
            byte[] footer = Bounds.Read(source, source.Length - 512, 512);
            hash.AppendData(footer);
            if (image.Metadata.Properties["VHD allocation"] == "Dynamic")
            {
                long header = checked((long)BinaryPrimitives.ReadUInt64BigEndian(footer.AsSpan(16)));
                hash.AppendData(Bounds.Read(source, 0, 512));
                hash.AppendData(Bounds.Read(source, header, 1024));
            }
        }
        else
        {
            hash.AppendData(Bounds.Read(source, 0, 65536));
            byte[] regions = Bounds.Read(source, 3 * 65536, 65536);
            hash.AppendData(regions);
            hash.AppendData(Bounds.Read(source, 4 * 65536, 65536));
            for (int entry = 0; entry < 2; entry++)
            {
                int offset = 16 + entry * 32;
                if (new Guid(regions.AsSpan(offset, 16)) != new Guid("8b7ca206-4790-4b9a-b8fe-575f050f886e")) { continue; }
                long start = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(regions.AsSpan(offset + 16)));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(regions.AsSpan(offset + 24));
                for (long position = 0; position < length; position += 65536)
                {
                    hash.AppendData(Bounds.Read(source, start + position, (int)Math.Min(65536, length - position)));
                }
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string HashOutside(IReadOnlyBlockDevice device, List<(long Offset, long Length)> ranges, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long position = 0;
        foreach ((long start, long length) in ranges.OrderBy(range => range.Offset).Append((device.Length, 0L)))
        {
            while (position < start)
            {
                token.ThrowIfCancellationRequested();
                int count = (int)Math.Min(buffer.Length, start - position);
                Bounds.ReadExactly(device, position, buffer.AsSpan(0, count));
                hash.AppendData(buffer, 0, count);
                position += count;
            }
            position = checked(start + length);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Copy(IReadOnlyBlockDevice source, Stream target, CancellationToken token)
    {
        byte[] buffer = new byte[65536];
        for (long offset = 0; offset < source.Length;)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, source.Length - offset);
            Bounds.ReadExactly(source, offset, buffer.AsSpan(0, count));
            target.Write(buffer, 0, count);
            offset += count;
        }
    }

    private static void PatchChanged(Stream target, Stream changed, long offset, long length, CancellationToken token)
    {
        Bounds.Check(offset, length, target.Length);
        if (changed.Length != length) { throw new ImageFormatException("Extracted volume length changed."); }
        byte[] original = new byte[65536];
        byte[] updated = new byte[65536];
        for (long position = 0; position < length;)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(original.Length, length - position);
            target.Position = checked(offset + position);
            target.ReadExactly(original.AsSpan(0, count));
            changed.ReadExactly(updated.AsSpan(0, count));
            if (!original.AsSpan(0, count).SequenceEqual(updated.AsSpan(0, count)))
            {
                // Sector-granular patches avoid allocating unrelated sparse payload blocks.
                for (int sector = 0; sector < count; sector += 512)
                {
                    if (!original.AsSpan(sector, 512).SequenceEqual(updated.AsSpan(sector, 512)))
                    {
                        target.Position = checked(offset + position + sector);
                        target.Write(updated, sector, 512);
                    }
                }
            }
            position += count;
        }
    }

    private sealed class FatVolumeSession : IImageSession
    {
        public FatVolumeSession(IImageSession image, ImageVolume volume)
        {
            var bounded = new PartitionBlockDevice(image.Device, volume.Partition?.Offset ?? 0,
                volume.Partition?.Length ?? image.Device.Length);
            byte[] boot = Bounds.Read(bounded, 0, 512);
            HiddenSectors = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(28));
            long expected = (volume.Partition?.Offset ?? 0) / 512;
            if (HiddenSectors != expected)
            {
                bounded.Dispose();
                throw new NotSupportedException("FAT BPB hidden sectors do not match the selected partition's absolute 512-byte-sector offset; no source repair is performed.");
            }
            BackupSector = volume.FileSystem.Name == "FAT32"
                ? BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(50)) : 0;
            if (BackupSector > 0 && BinaryPrimitives.ReadUInt32LittleEndian(Bounds.Read(bounded,
                checked(BackupSector * 512L + 28), 4)) != HiddenSectors)
            {
                bounded.Dispose();
                throw new NotSupportedException("FAT32 primary/backup hidden-sector metadata disagree; no source repair is performed.");
            }
            Device = bounded;
            Metadata = new(image.Metadata.Path, "Raw", Device.Length, Device.Length, 512, image.Metadata.Properties);
            Volumes = [new(volume.Id, null, volume.FileSystem)];
        }
        public uint HiddenSectors { get; }
        public int BackupSector { get; }
        public ImageMetadata Metadata { get; }
        public IReadOnlyBlockDevice Device { get; }
        public IReadOnlyList<PartitionInfo> Partitions => [];
        public IReadOnlyList<ImageVolume> Volumes { get; }
        public IReadOnlyList<string> Warnings => [];
        public void Dispose() => Device.Dispose();
    }

}
