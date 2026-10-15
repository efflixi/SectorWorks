namespace SectorWorks.Platform;

public interface IStorageSpace
{
    long AvailableBytes(string directory);
}

public sealed class HostStorageSpace : IStorageSpace
{
    public long AvailableBytes(string directory) => new DriveInfo(directory).AvailableFreeSpace;
}
