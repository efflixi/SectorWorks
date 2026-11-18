using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SectorWorks.Core;

namespace SectorWorks.Storage;

// CUE companions are untrusted filesystem paths, not user-selected inputs.
internal static class CuePath
{
    internal static string Normalize(string name)
    {
        string normalized = name.Replace('\\', '/');
        string[] parts = normalized.Split('/');
        if (normalized.Length > 1024 || parts.Length > 16 || parts.Any(part => !ImageEditNames.IsSupportedName(part)
            || part.Any(c => c is '\u00b9' or '\u00b2' or '\u00b3')
            || part.Split('.')[0].Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || part.Split('.')[0].Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)))
        { throw new ImageFormatException("Unsafe CUE companion reference: " + name); }
        return normalized;
    }

    internal static SafeFileHandle Open(string directory, string relative) =>
        Open(directory, relative, null, out _, out _);

    internal static SafeFileHandle OpenCompanion(string directory, string relative, string catalog,
        out string resolved, out bool sidecar) => Open(directory, relative, catalog, out resolved, out sidecar);

    private static SafeFileHandle Open(string directory, string relative, string? catalog, out string resolved, out bool sidecar)
    {
        resolved = Normalize(relative);
        sidecar = false;
        string[] parts = resolved.Split('/');
        if (OperatingSystem.IsLinux())
        {
            using SafeFileHandle root = Linux.Handle(-100, directory, Linux.Directory);
            SafeFileHandle file = Linux.Open(root, parts, catalog, out sidecar);
            resolved = string.Join('/', parts);
            return file;
        }
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("CUE sources require Windows or Linux."); }
        List<SafeFileHandle> guards = [];
        string current = directory;
        try
        {
            guards.Add(Windows.Open(current, true));
            foreach (string part in parts[..^1])
            {
                current = Path.Combine(current, part);
                guards.Add(Windows.Open(current, true));
            }
            return Windows.Open(Path.Combine(current, parts[^1]), false);
        }
        finally
        {
            foreach (SafeFileHandle guard in guards) { guard.Dispose(); }
        }
    }

    private static IOException Failure(string path) => new("Cannot open regular, unlinked CUE source: " + path,
        new Win32Exception(Marshal.GetLastPInvokeError()));

    private static class Linux
    {
        private const int CloseExec = 1 << 19, PathOnly = 1 << 21;
        // The Linux arm64 UAPI keeps the AArch32 directory/no-follow flag values.
        private static readonly bool Arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        private static readonly int NoFollow = 1 << (Arm64 ? 15 : 17);
        internal static readonly int Directory = PathOnly | NoFollow | CloseExec | (1 << (Arm64 ? 14 : 16));

        internal static SafeFileHandle Handle(int parent, string path, int flags)
        {
            int fd = OpenAt(parent, path, flags);
            return fd >= 0 ? new SafeFileHandle(fd, true) : throw Failure(path);
        }

        internal static SafeFileHandle Open(SafeFileHandle root, string[] parts, string? catalog, out bool sidecar)
        {
            sidecar = false;
            SafeFileHandle parent = root;
            try
            {
                foreach (string part in parts[..^1])
                {
                    SafeFileHandle child = Handle(parent.DangerousGetHandle().ToInt32(), part, Directory);
                    if (parent != root) { parent.Dispose(); }
                    parent = child;
                }
                int parentFd = parent.DangerousGetHandle().ToInt32();
                using SafeFileHandle path = FindLeaf(parentFd, parts, catalog, out sidecar);
                int fd = path.DangerousGetHandle().ToInt32();
                if (Statx(fd, "", 0x1000, 1, out Stat status) != 0) { throw Failure(parts[^1]); }
                if ((status.Mask & 1) == 0 || (status.Mode & 0xf000) != 0x8000)
                { throw new ImageFormatException("CUE references must be regular files, not links, devices or pipes: " + parts[^1]); }
                return File.OpenHandle($"/proc/self/fd/{fd}", FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
            }
            finally { if (parent != root) { parent.Dispose(); } }
        }

        private static SafeFileHandle FindLeaf(int parent, string[] parts, string? catalog, out bool sidecar)
        {
            sidecar = false;
            const int missingEntry = 2; // ENOENT
            const int maxEntries = 8192;
            int flags = PathOnly | NoFollow | CloseExec;
            string requested = parts[^1];
            int exact = OpenAt(parent, requested, flags);
            if (exact >= 0) { return new SafeFileHandle(exact, true); }
            IOException error = Failure(requested);
            if (catalog is null || parts.Length != 1
                || error.InnerException is not Win32Exception { NativeErrorCode: missingEntry })
            { throw error; }

            string? match = null;
            string stem = Path.GetFileNameWithoutExtension(requested);
            string catalogStem = Path.GetFileNameWithoutExtension(catalog);
            int count = 0;
            // Enumerate the already-open directory; the final open still uses
            // openat and the same no-follow and regular-file checks.
            foreach (string path in System.IO.Directory.EnumerateFileSystemEntries($"/proc/self/fd/{parent}"))
            {
                if (++count > maxEntries) { throw new ImageFormatException("CUE companion case lookup exceeds its directory bound."); }
                string name = Path.GetFileName(path);
                if (name.Equals(requested, StringComparison.OrdinalIgnoreCase))
                {
                    if (match is not null) { throw new ImageFormatException("Ambiguous case-only CUE companion: " + requested); }
                    match = name;
                }
                string candidateStem = Path.GetFileNameWithoutExtension(name);
                if ((candidateStem.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    || candidateStem.Equals(catalogStem, StringComparison.OrdinalIgnoreCase))
                    && Path.GetExtension(name).ToLowerInvariant() is ".ccd" or ".sub" or ".cdt")
                { sidecar = true; }
            }
            if (match is null) { throw error; }
            parts[^1] = match;
            return Handle(parent, match, flags);
        }

        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct Stat
        {
            [FieldOffset(0)] public uint Mask;
            [FieldOffset(28)] public ushort Mode;
        }

        [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
        private static extern int OpenAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        private static extern int Statx(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat stat);
    }

    private static class Windows
    {
        internal static SafeFileHandle Open(string path, bool directory)
        {
            const uint read = 0x80000000, attributes = 0x80, openReparse = 0x00200000, backupSemantics = 0x02000000;
            SafeFileHandle file = CreateFile(path, directory ? attributes : read, directory ? 3u : 1u, 0, 3,
                openReparse | (directory ? backupSemantics : 0), 0);
            if (file.IsInvalid)
            {
                IOException error = Failure(path);
                file.Dispose();
                throw error;
            }
            if (!GetAttributes(file, 9, out AttributeTag tag, 8))
            {
                IOException error = Failure(path);
                file.Dispose();
                throw error;
            }
            if ((tag.Attributes & 0x440) != 0 || ((tag.Attributes & 0x10) != 0) != directory || GetFileType(file) != 1)
            {
                file.Dispose();
                throw new ImageFormatException("CUE sources cannot use reparse points or non-disk files: " + path);
            }
            return file;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AttributeTag { public uint Attributes, Tag; }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, nint security,
            uint creation, uint flags, nint template);
        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetAttributes(SafeFileHandle file, int type, out AttributeTag info, uint size);
        [DllImport("kernel32.dll")]
        private static extern uint GetFileType(SafeFileHandle file);
    }
}
