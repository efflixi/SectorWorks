using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SectorWorks.Platform;

/// <summary>Publishes a complete sibling-directory tree without replacing an existing entry.</summary>
public static class AtomicOutputDirectory
{
    public static void Publish(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveNoReplace(source, destination, 8)) { throw Failure(destination); }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                if (RenameNoReplace(-100, source, -100, destination, 1) != 0) { throw Failure(destination); }
            }
            catch (EntryPointNotFoundException error)
            { throw new IOException("The host cannot publish a BIN/CUE folder without risking replacement.", error); }
        }
        else { throw new PlatformNotSupportedException(); }
    }

    private static IOException Failure(string path) => new("Cannot atomically publish the new output folder: " + path,
        new Win32Exception(Marshal.GetLastPInvokeError()));

    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameNoReplace(int oldDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        int newDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveNoReplace(string existing, string destination, uint flags);
}
