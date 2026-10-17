using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SectorWorks.Storage;

internal static class LinuxSourceLock
{
    public static void Set(SafeFileHandle handle, long length, bool acquire)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            throw new PlatformNotSupportedException("Open-file-description source locking requires Linux x64 or ARM64.");
        }
        var range = new LockRange { Type = acquire ? (short)1 : (short)2, Whence = 0, Start = 0, Length = length, ProcessId = 0 };
        // F_OFD_SETLK conflicts with ordinary fcntl writers, but unrelated reader
        // handles closing in this process cannot release this descriptor's lock.
        if (SetLock(handle, 37, ref range) != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new IOException("Linux open-file-description source locking failed: " + new Win32Exception(error).Message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LockRange
    {
        public short Type;
        public short Whence;
        public long Start;
        public long Length;
        public int ProcessId;
    }

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int SetLock(SafeFileHandle handle, int command, ref LockRange range);
}
