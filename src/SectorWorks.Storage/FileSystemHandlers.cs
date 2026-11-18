using SectorWorks.Core;

namespace SectorWorks.Storage;

internal interface IReadOnlyFileSystemHandler
{
    IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token);
}

internal sealed class FatFileSystemHandler : IReadOnlyFileSystemHandler
{
    internal const string EarlyFloppyWarning = "Legacy FAT12 floppy opened without a 0x55AA boot signature after BPB, geometry and mirrored-FAT checks. Read-only access is not a bootability guarantee.";

    public IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (device.Length < 512)
        {
            return null;
        }
        byte[] boot = Bounds.Read(device, 0, 512);
        if (!FatFileSystem.LooksLikeFat(boot))
        {
            return null;
        }
        var fileSystem = new FatFileSystem(device);
        if (boot[510] != 0x55 || boot[511] != 0xaa)
        {
            warnings.Add(EarlyFloppyWarning);
        }
        return fileSystem;
    }
}

internal sealed class NtfsFileSystemHandler : IReadOnlyFileSystemHandler
{
    public IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return device.Length >= 512 && Bounds.Read(device, 3, 8).AsSpan().SequenceEqual("NTFS    "u8)
            ? DiscFileSystemAdapter.OpenNtfs(device, context, token) : null;
    }
}

internal sealed class OpticalFileSystemHandler : IReadOnlyFileSystemHandler
{
    public IReadOnlyFileSystem? TryOpen(IReadOnlyBlockDevice device, ParserContext context,
        List<string> warnings, CancellationToken token) => ImageOpener.OpenOpticalFileSystem(device, context, warnings, token);
}
