using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SectorWorks.Storage;

internal static class WindowsAtomicReplacement
{
    public static void Replace(string staging, string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Handle-based Windows atomic replacement requires Windows.");
        }
        using SafeFileHandle file = CreateFile(ExtendedPath(staging), 0x00010000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (file.IsInvalid) { throw Failure("Opening the validated staging file for atomic replacement"); }

        string fullDestination = ExtendedPath(destination);
        // Encoding replacement fallback must never retarget a host pathname.
        byte[] name = MemoryMarshal.AsBytes(fullDestination.AsSpan()).ToArray();
        int nameOffset = Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.FileName)).ToInt32();
        int size = checked(nameOffset + name.Length + 2);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            // FileRenameInfoEx + REPLACE_IF_EXISTS | POSIX_SEMANTICS replaces
            // the name atomically without closing the old source's write lock.
            var information = new RenameInformation { Flags = 3, RootDirectory = IntPtr.Zero, FileNameLength = checked((uint)name.Length), FileName = '\0' };
            Marshal.StructureToPtr(information, buffer, false);
            Marshal.Copy(name, 0, IntPtr.Add(buffer, nameOffset), name.Length);
            Marshal.WriteInt16(buffer, checked(nameOffset + name.Length), 0);
            if (!SetInformation(file, 22, buffer, checked((uint)size)))
            {
                throw Failure("Atomically replacing the locked source image");
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string ExtendedPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? fullPath
            : fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;
    }

    private static IOException Failure(string operation)
    {
        int error = Marshal.GetLastPInvokeError();
        return new IOException($"{operation} failed (Win32 {error}); no unlock or non-atomic fallback was attempted.",
            new Win32Exception(error));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RenameInformation
    {
        public uint Flags;
        public IntPtr RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformation(SafeFileHandle file, int informationClass, IntPtr information, uint size);
}
