using DiscUtils.Ntfs;

#pragma warning disable CA1416 // This probe measures the annotated API's actual cross-platform behavior.

namespace SectorWorks.DependencyProbe;

public enum NtfsProbeOperation
{
    AddResident,
    AddNonresident,
    AddWithoutShortNames,
    AddUncompressedWithoutShortNames,
    CreateDirectory,
    CreateDirectoryWithoutShortNames,
    ReadSecurity,
    GrowResident,
    ShrinkNonresident,
    DeleteResident,
    DeleteNonresident,
    RenameResidentUnicode,
    RenameNonresidentUnicode
}

public sealed record NtfsProbeResult(string? ExceptionType, string? Message, string? StackTrace)
{
    public bool Succeeded => ExceptionType is null;
}

/// <summary>
/// A bounded dependency experiment on disposable images, not a qualified product writer.
/// No formatting, mounting, security substitution or source modification is performed.
/// </summary>
public static class ExistingNtfsVolumeProbe
{
    public const string UnicodeFileName = "Renamed-λ-文件.txt";
    public const string UnicodeDirectoryName = "Folder-λ-目录";

    public static NtfsProbeResult Run(string sourcePath, string newCopyPath, NtfsProbeOperation operation)
    {
        if (!Enum.IsDefined(operation)) { throw new ArgumentOutOfRangeException(nameof(operation)); }
        using FileStream source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length is < 512 or > 128L * 1024 * 1024)
        {
            throw new ArgumentException("Probe accepts existing synthetic volumes from 512 bytes to 128 MiB.");
        }
        using (FileStream copy = File.Open(newCopyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(copy, 64 * 1024);
            copy.Flush(true);
        }
        try
        {
            using FileStream image = File.Open(newCopyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using (var fs = new NtfsFileSystem(image))
            {
                Execute(fs, operation);
            }
            image.Flush(true);
            return new(null, null, null);
        }
        catch (Exception error) when (error is PlatformNotSupportedException or IOException)
        {
            return new(error.GetType().FullName, error.Message, error.StackTrace);
        }
    }

    private static void Execute(NtfsFileSystem fs, NtfsProbeOperation operation)
    {
        switch (operation)
        {
            case NtfsProbeOperation.AddResident:
                Write(fs, UnicodeFileName, FileMode.CreateNew, "added"u8.ToArray());
                break;
            case NtfsProbeOperation.AddNonresident:
                Write(fs, UnicodeFileName, FileMode.CreateNew, LargePayload());
                break;
            case NtfsProbeOperation.AddWithoutShortNames:
            case NtfsProbeOperation.AddUncompressedWithoutShortNames:
                using (Stream file = fs.OpenFile(UnicodeFileName, FileMode.CreateNew, FileAccess.ReadWrite,
                    new NewFileOptions
                    {
                        CreateShortNames = false,
                        Compressed = operation == NtfsProbeOperation.AddUncompressedWithoutShortNames ? false : null
                    }))
                {
                    file.Write("added"u8);
                }
                break;
            case NtfsProbeOperation.CreateDirectory:
                fs.CreateDirectory(UnicodeDirectoryName);
                break;
            case NtfsProbeOperation.CreateDirectoryWithoutShortNames:
                fs.CreateDirectory(UnicodeDirectoryName, new NewFileOptions { CreateShortNames = false });
                break;
            case NtfsProbeOperation.ReadSecurity:
                fs.GetSecurity("SENTINEL.TXT");
                break;
            case NtfsProbeOperation.GrowResident:
                Write(fs, "SENTINEL.TXT", FileMode.Create, LargePayload());
                break;
            case NtfsProbeOperation.ShrinkNonresident:
                Write(fs, "LARGE.BIN", FileMode.Create, "small"u8.ToArray());
                break;
            case NtfsProbeOperation.DeleteResident:
                fs.DeleteFile("SENTINEL.TXT");
                break;
            case NtfsProbeOperation.DeleteNonresident:
                fs.DeleteFile("LARGE.BIN");
                break;
            case NtfsProbeOperation.RenameResidentUnicode:
                fs.MoveFile("SENTINEL.TXT", UnicodeFileName);
                break;
            case NtfsProbeOperation.RenameNonresidentUnicode:
                fs.MoveFile("LARGE.BIN", UnicodeFileName);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    public static byte[] LargePayload() => Enumerable.Range(0, 16384).Select(i => (byte)(i % 251)).ToArray();

    private static void Write(NtfsFileSystem fs, string path, FileMode mode, byte[] content)
    {
        using Stream file = fs.OpenFile(path, mode, FileAccess.ReadWrite);
        file.Write(content);
    }
}
