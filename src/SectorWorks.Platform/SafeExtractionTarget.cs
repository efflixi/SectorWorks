using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SectorWorks.Platform;

public sealed class SafeExtractionTarget : IDisposable
{
    private readonly string root;
    private readonly SafeFileHandle? unixRoot;
    private readonly List<SafeFileHandle> windowsRootLocks = [];

    public SafeExtractionTarget(string destination)
    {
        root = Path.GetFullPath(destination);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("Extraction destination must be an existing directory.");
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                string current = Path.GetPathRoot(root)!;
                windowsRootLocks.Add(OpenWindowsDirectory(current));
                foreach (string part in root[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    windowsRootLocks.Add(OpenWindowsDirectory(current));
                }
            }
            else
            {
                using SafeFileHandle starting = OpenUnixDirectory(-1, "/");
                unixRoot = TraverseUnix(starting, root.Split('/', StringSplitOptions.RemoveEmptyEntries), false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void CreateDirectory(string relativePath)
    {
        string[] parts = ValidateRelativePath(relativePath);
        if (OperatingSystem.IsWindows())
        {
            using var directories = OpenWindowsPath(parts, true);
        }
        else
        {
            using SafeFileHandle directory = TraverseUnix(unixRoot!, parts, true);
        }
    }

    public ExtractionFile CreateFile(string relativePath)
    {
        string[] parts = ValidateRelativePath(relativePath);
        string[] parents = parts[..^1];
        string name = parts[^1];
        if (OperatingSystem.IsWindows())
        {
            WindowsDirectories directories = OpenWindowsPath(parents, true);
            string path = Path.Combine(directories.Path, name);
            SafeFileHandle file = Native.CreateFile(path, 0x40000000, 0, IntPtr.Zero, 1, 0x00200000, IntPtr.Zero);
            if (file.IsInvalid)
            {
                IOException error = NativeError("Cannot create extraction file (existing files are never overwritten)", path);
                file.Dispose();
                directories.Dispose();
                throw error;
            }
            try
            {
                RejectWindowsReparsePoint(file, path);
                return new ExtractionFile(new FileStream(file, FileAccess.Write),
                    () => File.Delete(path), directories);
            }
            catch
            {
                file.Dispose();
                directories.Dispose();
                throw;
            }
        }

        SafeFileHandle parent = TraverseUnix(unixRoot!, parents, true);
        int descriptor = Native.OpenAt(parent.DangerousGetHandle().ToInt32(), name, UnixCreateFlags, 0x180);
        if (descriptor < 0)
        {
            IOException error = NativeError("Cannot create extraction file (existing files are never overwritten)", relativePath);
            parent.Dispose();
            throw error;
        }
        var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        try
        {
            return new ExtractionFile(new FileStream(handle, FileAccess.Write),
                () =>
                {
                    if (Native.UnlinkAt(parent.DangerousGetHandle().ToInt32(), name, 0) != 0)
                    {
                        throw NativeError("Cannot remove incomplete extraction file", relativePath);
                    }
                }, parent);
        }
        catch
        {
            handle.Dispose();
            parent.Dispose();
            throw;
        }
    }

    public static string[] ValidateRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains('\\'))
        {
            throw new InvalidDataException("Extraction path must be a relative image path without backslashes.");
        }
        string[] parts = path.Split('/');
        foreach (string part in parts)
        {
            string stem = part.Split('.')[0].TrimEnd(' ');
            bool reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                    && (stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3'));
            if (part.Length is 0 or > 255 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c)) || reserved)
            {
                throw new InvalidDataException($"Unsafe or host-incompatible extraction name: {part}");
            }
        }
        if (parts.Length > 128)
        {
            throw new InvalidDataException("Extraction path exceeds the 128-component limit.");
        }
        return parts;
    }

    public void Dispose()
    {
        unixRoot?.Dispose();
        foreach (SafeFileHandle directory in windowsRootLocks)
        {
            directory.Dispose();
        }
        windowsRootLocks.Clear();
    }

    private static int UnixDirectoryFlags => OperatingSystem.IsMacOS() ? 0x100000 | 0x100 | 0x1000000 : 0x10000 | 0x20000 | 0x80000;
    private static int UnixCreateFlags => OperatingSystem.IsMacOS() ? 1 | 0x200 | 0x800 | 0x100 | 0x1000000 : 1 | 0x40 | 0x80 | 0x20000 | 0x80000;

    private static SafeFileHandle OpenUnixDirectory(int parent, string name)
    {
        int descriptor = parent < 0 ? Native.Open(name, UnixDirectoryFlags, 0)
            : Native.OpenAt(parent, name, UnixDirectoryFlags, 0);
        if (descriptor < 0)
        {
            throw NativeError("Cannot open extraction directory without following links", name);
        }
        return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
    }

    private static SafeFileHandle TraverseUnix(SafeFileHandle start, string[] parts, bool create)
    {
        int copy = Native.Duplicate(start.DangerousGetHandle().ToInt32());
        if (copy < 0)
        {
            throw NativeError("Cannot retain extraction directory", string.Empty);
        }
        var current = new SafeFileHandle(new IntPtr(copy), ownsHandle: true);
        try
        {
            foreach (string part in parts)
            {
                if (create && Native.MakeDirectoryAt(current.DangerousGetHandle().ToInt32(), part, 0x1c0) != 0
                    && Marshal.GetLastPInvokeError() != 17)
                {
                    throw NativeError("Cannot create extraction directory", part);
                }
                SafeFileHandle next = OpenUnixDirectory(current.DangerousGetHandle().ToInt32(), part);
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private WindowsDirectories OpenWindowsPath(string[] parts, bool create)
    {
        var result = new WindowsDirectories(root);
        try
        {
            foreach (string part in parts)
            {
                result.Path = Path.Combine(result.Path, part);
                if (create)
                {
                    Directory.CreateDirectory(result.Path);
                }
                result.Handles.Add(OpenWindowsDirectory(result.Path));
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static SafeFileHandle OpenWindowsDirectory(string path)
    {
        SafeFileHandle directory = Native.CreateFile(path, 0x80, 1, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (directory.IsInvalid)
        {
            IOException error = NativeError("Cannot lock extraction directory", path);
            directory.Dispose();
            throw error;
        }
        try
        {
            RejectWindowsReparsePoint(directory, path);
            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    private static void RejectWindowsReparsePoint(SafeFileHandle handle, string path)
    {
        if (!Native.GetFileInformation(handle, 9, out AttributeTag information, 8))
        {
            throw NativeError("Cannot inspect extraction target", path);
        }
        if ((information.Attributes & 0x400) != 0)
        {
            throw new IOException("Extraction refuses symbolic links/reparse points: " + path);
        }
    }

    private static IOException NativeError(string action, string path)
    {
        int error = Marshal.GetLastPInvokeError();
        return new IOException($"{action}: {path} ({new Win32Exception(error).Message})");
    }

    private sealed class WindowsDirectories(string path) : IDisposable
    {
        public string Path { get; set; } = path;
        public List<SafeFileHandle> Handles { get; } = [];
        public void Dispose()
        {
            foreach (SafeFileHandle handle in Handles)
            {
                handle.Dispose();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag
    {
        public uint Attributes;
        public uint Tag;
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);
        [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
        internal static extern int OpenAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);
        [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
        internal static extern int MakeDirectoryAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
        [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
        internal static extern int UnlinkAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
        internal static extern int Duplicate(int descriptor);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformation(SafeFileHandle handle, int informationClass, out AttributeTag information, uint size);
    }
}

public sealed class ExtractionFile(FileStream stream, Action removeIncomplete, IDisposable directoryLease) : IDisposable
{
    private bool complete;
    private bool disposed;
    public Stream Stream => stream;

    public void Complete()
    {
        stream.Flush(flushToDisk: true);
        complete = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            stream.Dispose();
            if (!complete)
            {
                removeIncomplete();
            }
        }
        finally
        {
            directoryLease.Dispose();
        }
    }
}
