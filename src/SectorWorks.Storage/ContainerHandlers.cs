using DiscUtils.Streams;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal interface IReadOnlyContainerHandler
{
    FormatDetection? Detect(IReadOnlyBlockDevice source);
    IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties);
}

internal sealed class VhdContainerHandler : IReadOnlyContainerHandler
{
    public FormatDetection? Detect(IReadOnlyBlockDevice source)
    {
        if ((source.Length >= 8 && Bounds.Read(source, 0, 8).AsSpan().SequenceEqual("conectix"u8))
            || (source.Length >= 512 && Bounds.Read(source, source.Length - 512, 8).AsSpan().SequenceEqual("conectix"u8)))
        {
            return new("VHD", 90, true, "VHD footer cookie");
        }
        return null;
    }

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        uint type = ContainerValidation.Vhd(source);
        var disk = new DiscUtils.Vhd.Disk(new DeviceStream(source, context), Ownership.Dispose);
        resources.Add(disk);
        var device = new StreamBlockDevice(disk.Content, context);
        resources.Add(device);
        properties["VHD allocation"] = type == 2 ? "Fixed" : "Dynamic";
        properties["Virtual capacity"] = disk.Capacity.ToString();
        return device;
    }
}

internal sealed class VhdxContainerHandler : IReadOnlyContainerHandler
{
    public FormatDetection? Detect(IReadOnlyBlockDevice source) =>
        source.Length >= 8 && Bounds.Read(source, 0, 8).AsSpan().SequenceEqual("vhdxfile"u8)
            ? new("VHDX", 100, true, "VHDX file identifier") : null;

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties)
    {
        ContainerValidation.Vhdx(source);
        var disk = new DiscUtils.Vhdx.Disk(new DeviceStream(source, context), Ownership.Dispose);
        resources.Add(disk);
        var layer = (DiscUtils.Vhdx.DiskImageFile)disk.Layers.First();
        var device = new StreamBlockDevice(disk.Content, context, checked((int)layer.LogicalSectorSize),
            checked((int)layer.Information.PhysicalSectorSize));
        resources.Add(device);
        properties["VHDX allocation"] = layer.Information.LeaveBlocksAllocated ? "Fixed" : "Dynamic";
        properties["VHDX block size"] = layer.Information.BlockSize.ToString();
        properties["Physical sector size"] = layer.Information.PhysicalSectorSize.ToString();
        properties["VHDX creator"] = layer.Information.Creator;
        return device;
    }
}

internal sealed class IsoContainerHandler : IReadOnlyContainerHandler
{
    public FormatDetection? Detect(IReadOnlyBlockDevice source) =>
        source.Length >= 32768 + 7 && Bounds.Read(source, 32768 + 1, 5).AsSpan().SequenceEqual("CD001"u8)
            ? new("ISO", 80, true, "ISO9660 volume descriptor") : null;

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties) => source;
}

internal sealed class RawContainerHandler : IReadOnlyContainerHandler
{
    public FormatDetection Detect(IReadOnlyBlockDevice source) =>
        new("Raw", 0, false, "Unrecognized container; bounded raw fallback with 512-byte sector assumption");

    public IReadOnlyBlockDevice OpenReadOnly(IReadOnlyBlockDevice source, ParserContext context,
        List<IDisposable> resources, Dictionary<string, string> properties) => source;
}
