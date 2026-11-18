using System.Runtime.ExceptionServices;
using System.Text;
using DiscUtils.Iso9660;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

public sealed class ImageOpener : IImageOpener
{
    private static readonly IReadOnlyContainerHandler[] Containers =
        [new VhdxContainerHandler(), new VhdContainerHandler(), new IsoContainerHandler(), new RawContainerHandler()];
    private static readonly IReadOnlyFileSystemHandler[] FileSystems =
        [new NtfsFileSystemHandler(), new FatFileSystemHandler(), new OpticalFileSystemHandler()];

    public IImageSession OpenReadOnly(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        { return CueImageSession.Open(path, cancellationToken); }
        var resources = new List<IDisposable>();
        var context = new ParserContext();
        try
        {
            return context.Run<IImageSession>(() =>
            {
                var source = new FileBlockDevice(path);
                resources.Add(source);
                var properties = new Dictionary<string, string>();
                var warnings = new List<string>();
                (IReadOnlyContainerHandler handler, FormatDetection detection) = SelectContainer(source);
                string format = detection.Format;
                IReadOnlyBlockDevice device = handler.OpenReadOnly(source, context, resources, properties);
                if (format == "Raw" && source.Length < 512 && Path.GetExtension(path).ToLowerInvariant() is ".iso" or ".vhd" or ".vhdx")
                {
                    throw new ImageFormatException($"Truncated or unrecognized {Path.GetExtension(path)} image (less than one sector).");
                }
                if (source.Length is >= 24 and < 512)
                {
                    byte[] prefix = Bounds.Read(source, 0, (int)source.Length);
                    if (prefix[0] is 0xeb or 0xe9 && U16(prefix, 11) is 512 or 1024 or 2048 or 4096
                        && prefix[13] != 0 && prefix[16] is 1 or 2)
                    {
                        throw new ImageFormatException("Truncated filesystem boot sector.");
                    }
                }
                bool optical = format is "ISO" or "UDF";
                properties["Access"] = "Read-only; no source writes, parent resolution, repair or log replay.";
                ImageFormatException? partitionError = null;
                IReadOnlyList<PartitionInfo> partitions;
                try { partitions = PartitionReader.Read(device, warnings, properties, cancellationToken); }
                catch (ImageFormatException error) when (optical)
                {
                    partitionError = error;
                    partitions = [];
                    warnings.Add("Optical system-area partition view rejected: " + error.Message);
                }
                var volumes = new List<ImageVolume>();
                ImageFormatException? opticalError = null;
                if (optical)
                {
                    try
                    {
                        if (OpenOpticalFileSystem(device, context, warnings, cancellationToken, standaloneCompatibility: true) is { } fs)
                        { AddOpenedVolume("volume-0", null, fs); }
                    }
                    catch (ImageFormatException error) { opticalError = error; }
                }
                else if (partitions.Count == 0)
                {
                    AddVolume(device, null, "volume-0");
                }
                bool hasOpticalVolume = optical && volumes.Count != 0;
                foreach (PartitionInfo partition in partitions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (partition.Table == "MBR" && partition.Type is "0x05" or "0x0F" or "0x85")
                    {
                        continue;
                    }
                    var view = new PartitionBlockDevice(device, partition.Offset, partition.Length);
                    resources.Add(view);
                    try { AddVolume(view, partition, $"partition-{partition.Index}"); }
                    catch (ImageFormatException error) when (optical)
                    {
                        warnings.Add($"Optical system-area partition {partition.Index} filesystem rejected: {error.Message}");
                    }
                }
                if (optical && !hasOpticalVolume)
                {
                    if (partitions.Count == 0 && partitionError is null)
                    { AddVolume(device, null, "volume-0", diskOnly: true); }
                    if (volumes.Any(v => v.FileSystem.Name is "FAT12" or "FAT16" or "FAT32" or "NTFS"))
                    {
                        format = "Raw";
                        optical = false;
                        warnings.Add("An optical signature did not validate; only the verified disk filesystem view is available. "
                            + "Editing remains disabled with conflicting signatures.");
                    }
                    else if ((opticalError ?? partitionError) is { } error) { ExceptionDispatchInfo.Throw(error); }
                }
                if (volumes.Count == 0)
                {
                    warnings.Add("No supported filesystem was opened. Unknown layouts remain available only as bounded read-only bytes.");
                }
                bool hybrid = optical && (partitions.Count > 0 || partitionError is not null);
                int reportedSectorSize = optical ? OpticalLayout.SectorSize : device.LogicalSectorSize;
                properties["Sector size"] = optical ? "2048-byte optical logical blocks; physical sector size unknown"
                    : $"{device.LogicalSectorSize} bytes" + (format == "Raw" ? " (assumed; physical sector size unknown)" : "");
                if (optical)
                {
                    if (partitions.Count != 0) { properties["Partition addressing unit"] = $"{device.LogicalSectorSize} bytes"; }
                    if (hybrid)
                    {
                        properties["Optical system area"] = partitionError is null
                            ? "Validated disk partition map alongside the optical filesystem; views are read-only."
                            : "Disk partition map rejected; independently validated optical view only.";
                    }
                    warnings.Add("Optical/boot metadata is descriptive only. Bootability and exact-layout preservation are not guaranteed.");
                }
                var metadata = new ImageMetadata(Path.GetFullPath(path), format, source.Length, device.Length,
                    reportedSectorSize, properties.AsReadOnly())
                { HasOpticalPartitionLayout = hybrid };
                return new ImageSession(metadata, device, partitions,
                    volumes.AsReadOnly(), warnings.AsReadOnly(), resources, context);

                void AddVolume(IReadOnlyBlockDevice volumeDevice, PartitionInfo? partition, string id, bool diskOnly = false)
                {
                    foreach (IReadOnlyFileSystemHandler fileSystemHandler in FileSystems)
                    {
                        if (diskOnly && fileSystemHandler is OpticalFileSystemHandler) { continue; }
                        if (fileSystemHandler.TryOpen(volumeDevice, context, warnings, cancellationToken) is { } fs)
                        {
                            AddOpenedVolume(id, partition, fs);
                            return;
                        }
                    }
                }

                void AddOpenedVolume(string id, PartitionInfo? partition, IReadOnlyFileSystem filesystem)
                {
                    resources.Add(filesystem);
                    volumes.Add(new(id, partition, filesystem));
                }
            }, cancellationToken);
        }
        catch
        {
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                resources[i].Dispose();
            }
            throw;
        }
    }

    public FormatDetection Detect(IReadOnlyBlockDevice source) => SelectContainer(source).Detection;

    private static (IReadOnlyContainerHandler Handler, FormatDetection Detection) SelectContainer(IReadOnlyBlockDevice source) =>
        Containers.Select(handler => (Handler: handler, Detection: handler.Detect(source)))
            .Where(candidate => candidate.Detection is not null)
            .OrderByDescending(candidate => candidate.Detection!.Priority)
            .Select(candidate => (candidate.Handler, candidate.Detection!)).First();

    internal static IReadOnlyFileSystem? OpenOpticalFileSystem(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token, bool standaloneCompatibility = false)
    {
        var optical = new Dictionary<string, string>();
        bool iso = false;
        bool udf = false;
        for (int i = 16; i < 272 && (long)(i + 1) * 2048 <= device.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            byte[] descriptor = Bounds.Read(device, i * 2048L, 2048);
            string id = Encoding.ASCII.GetString(descriptor, 1, 5);
            if (id is "NSR02" or "NSR03")
            {
                udf = true;
            }
            if (id != "CD001")
            {
                if (i > 32)
                {
                    break;
                }
                continue;
            }
            if (descriptor[6] != 1)
            {
                throw new ImageFormatException("Unsupported ISO9660 descriptor version.");
            }
            iso = true;
            if (descriptor[0] is 1 or 2)
            {
                if (U16(descriptor, 128) != 2048 || U32(descriptor, 80) == 0
                    || checked((long)U32(descriptor, 80) * 2048) > device.Length
                    || U32(descriptor, 166) > 16 * 1024 * 1024
                    || checked((long)U32(descriptor, 158) * 2048 + U32(descriptor, 166)) > device.Length)
                {
                    throw new ImageFormatException("ISO9660 volume/root directory bounds or block size is invalid.");
                }
                optical[descriptor[0] == 1 ? "ISO9660" : "Supplementary descriptor"] = "Present";
                if (descriptor[0] == 2 && descriptor[88] == 0x25 && descriptor[89] == 0x2f
                    && descriptor[90] is 0x40 or 0x43 or 0x45)
                {
                    optical["Joliet"] = "Present";
                }
            }
            if (descriptor[0] == 0 && Encoding.ASCII.GetString(descriptor, 7, 23).TrimEnd('\0', ' ') == "EL TORITO SPECIFICATION")
            {
                uint catalog = U32(descriptor, 71);
                if (checked((long)catalog * 2048 + 32) > device.Length)
                {
                    throw new ImageFormatException("El Torito catalog LBA is outside the optical image.");
                }
                optical["El Torito catalog LBA"] = catalog.ToString();
                optical["El Torito platforms"] = ElToritoMetadata.Inspect(device, catalog);
                optical["Bootability"] = "Catalog present; not a bootability/preservation guarantee.";
            }
            // UDF's volume recognition sequence may follow the ISO terminator.
        }
        int recognition = OpticalRebuildDetection.FindUdfRecognition(device, token);
        if ((iso || udf || recognition != 0)
            && OpticalRebuildDetection.TryOpen(device, iso, recognition, optical, warnings, token, standaloneCompatibility) is { } qualified)
        {
            return qualified;
        }
        if (udf || recognition != 0)
        {
            warnings.Add("Unqualified UDF layout; UDF browsing is unavailable. The ISO view is used when supported.");
            optical["UDF"] = "Detected; browsing unavailable for this layout";
        }
        if (iso)
        {
            if (IsoValidation.Validate(device, token))
            {
                warnings.Add("ISO zero-length files have unused out-of-range extent addresses. They are read as empty without seeking to those addresses; no source data is rewritten.");
            }
            bool joliet = optical.ContainsKey("Joliet");
            var reader = new CDReader(new DeviceStream(device, context), joliet, true);
            optical["Active naming variant"] = joliet ? "Joliet" : reader.ActiveVariant.ToString();
            optical["Bootability"] = "Read-only metadata only; not guaranteed.";
            return new DiscFileSystemAdapter(reader, context, "ISO9660", optical, device);
        }
        return null;
    }

    private sealed class ImageSession(ImageMetadata metadata, IReadOnlyBlockDevice device,
        IReadOnlyList<PartitionInfo> partitions, IReadOnlyList<ImageVolume> volumes, IReadOnlyList<string> warnings,
        List<IDisposable> resources, ParserContext context) : IImageSession
    {
        private bool _disposed;
        public ImageMetadata Metadata => metadata;
        public IReadOnlyBlockDevice Device => device;
        public IReadOnlyList<PartitionInfo> Partitions => partitions;
        public IReadOnlyList<ImageVolume> Volumes => volumes;
        public IReadOnlyList<string> Warnings => warnings;
        public void Dispose()
        {
            lock (context.Gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                for (int i = resources.Count - 1; i >= 0; i--)
                {
                    resources[i].Dispose();
                }
            }
        }
    }
}
