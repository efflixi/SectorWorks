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
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Extraction is supported only on Windows and Linux.");
        }
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

    public ExtractionFile CreateFile(string relativePath) => CreateFile(relativePath, null);

    public ExtractionFile CreateAtomicFile(string name)
    {
        if (ValidateRelativePath(name).Length != 1)
        {
            throw new InvalidDataException("Atomic extraction requires one output filename.");
        }
        return CreateFile(".sectorworks-extract-" + Guid.NewGuid().ToString("N") + ".tmp", name);
    }

    private ExtractionFile CreateFile(string relativePath, string? publishAs)
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
                    () => File.Delete(path), directories, publishAs is null ? null
                        : () => File.Move(path, Path.Combine(directories.Path, publishAs), overwrite: false));
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
                }, parent, publishAs is null ? null
                    : () => AtomicOutputDirectory.PublishFile(parent.DangerousGetHandle().ToInt32(), name, publishAs));
        }
        catch
        {
            handle.Dispose();
            parent.Dispose();
            throw;
        }
    }

    /// <summary>Opens one existing regular sibling without following links. The
    /// held parent prevents redirection during recovery reads and cleanup.</summary>
    public RecoveryFile OpenRecoveryFile(string name, bool allowDelete = false)
    {
        if (ValidateRelativePath(name).Length != 1)
        {
            throw new InvalidDataException("Recovery access requires one sibling filename.");
        }
        SafeFileHandle handle;
        Action remove;
        if (OperatingSystem.IsWindows())
        {
            string path = Path.Combine(root, name);
            handle = Native.CreateFile(path, 0x80000000 | (allowDelete ? 0x10000u : 0), 1,
                IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw NativeError("Cannot open recovery file", path); }
            try
            {
                RejectWindowsReparsePoint(handle, path);
                if (!Native.GetFileInformation(handle, 9, out AttributeTag attributes, 8)
                    || (attributes.Attributes & 0x10) != 0)
                {
                    throw new IOException("Recovery requires a regular file: " + path);
                }
            }
            catch { handle.Dispose(); throw; }
            remove = () =>
            {
                int delete = 1;
                if (!Native.SetFileInformation(handle, 4, ref delete, 4)) { throw NativeError("Cannot delete recovery file", path); }
            };
        }
        else
        {
            int descriptor = Native.OpenAt(unixRoot!.DangerousGetHandle().ToInt32(), name, 0x20000 | 0x80000 | 0x800, 0);
            if (descriptor < 0) { throw NativeError("Cannot open recovery file without following links", name); }
            handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
            remove = () =>
            {
                if (Native.UnlinkAt(unixRoot.DangerousGetHandle().ToInt32(), name, 0) != 0)
                {
                    throw NativeError("Cannot delete recovery file", name);
                }
            };
        }
        try
        {
            var stream = new FileStream(handle, FileAccess.Read);
            if (!stream.CanSeek || (File.GetAttributes(handle) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                stream.Dispose();
                throw new IOException("Recovery refuses nonregular files: " + name);
            }
            return new RecoveryFile(stream, allowDelete ? remove : null);
        }
        catch { handle.Dispose(); throw; }
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

    // Linux arm64 and x64 assign different bits to O_DIRECTORY and O_NOFOLLOW.
    private static readonly bool UnixArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    private static readonly int UnixNoFollow = 1 << (UnixArm64 ? 15 : 17);
    private static readonly int UnixDirectoryFlags = (1 << (UnixArm64 ? 14 : 16)) | UnixNoFollow | 0x80000;
    private static readonly int UnixCreateFlags = 1 | 0x40 | 0x80 | UnixNoFollow | 0x80000;

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
        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileInformation(SafeFileHandle handle, int informationClass, ref int information, uint size);
    }
}

public sealed class RecoveryFile(FileStream stream, Action? remove) : IDisposable
{
    public Stream Stream => stream;
    public void Delete() => (remove ?? throw new InvalidOperationException("Recovery file was opened read-only."))();
    public void Dispose() => stream.Dispose();
}

public sealed class ExtractionFile(FileStream stream, Action removeIncomplete, IDisposable directoryLease, Action? publish = null) : IDisposable
{
    private bool complete;
    private bool disposed;
    public Stream Stream => stream;

    public void Complete(CancellationToken cancellationToken = default)
    {
        stream.Flush(flushToDisk: true);
        cancellationToken.ThrowIfCancellationRequested();
        if (publish is not null)
        {
            stream.Dispose();
            publish();
        }
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
