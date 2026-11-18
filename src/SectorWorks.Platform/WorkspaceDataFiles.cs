namespace SectorWorks.Platform;

public static class WorkspaceDataFiles
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PayloadMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static string ApplicationDataDirectory()
    {
        // Resolving a first-run XDG directory must not depend on its already
        // existing. Never fall back to a relative path in the working folder.
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(local) || !Path.IsPathFullyQualified(local))
        { throw new IOException("A fully qualified local application-data directory is required."); }
        return Path.Combine(local, "SectorWorks");
    }

    public static void CreatePrivateDirectory(string path)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        RejectExistingLink(fullPath);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(fullPath);
        }
        else
        {
            Directory.CreateDirectory(fullPath, DirectoryMode);
        }
        RejectExistingLink(fullPath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fullPath, DirectoryMode);
        }
        VerifyPrivateDirectory(fullPath);
    }

    public static FileStream CreatePayload(string path)
    {
        string fullPath = Path.GetFullPath(path);
        VerifyPrivateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Workspace payload requires a private parent directory."));
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.Read,
            BufferSize = 65536,
            Options = FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = PayloadMode; }
        var stream = new FileStream(fullPath, options);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(stream.SafeFileHandle, PayloadMode);
                if (File.GetUnixFileMode(stream.SafeFileHandle) != PayloadMode)
                {
                    throw new IOException("Cannot enforce private workspace payload permissions: " + fullPath);
                }
            }
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void VerifyPrivateDirectory(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException("Workspace data directory must be a real directory, not a link or reparse point: " + path);
        }
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != DirectoryMode)
        {
            throw new IOException("Cannot enforce private workspace directory permissions: " + path);
        }
    }

    private static void RejectExistingLink(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Workspace data directory cannot be a link or reparse point: " + path);
        }
    }
}
