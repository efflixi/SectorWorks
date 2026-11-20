using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Ntfs;
using SectorWorks.Core;
using static SectorWorks.Storage.PartitionReader;

namespace SectorWorks.Storage;

internal sealed class DiscFileSystemAdapter : IReadOnlyFileSystem
{
    private readonly DiscFileSystem _fileSystem;
    private readonly ParserContext _context;
    private readonly IReadOnlyBlockDevice? _opticalDevice;
    private readonly HashSet<ParserFileStream> _streams = [];
    private bool _disposed;

    internal DiscFileSystemAdapter(DiscFileSystem fileSystem, ParserContext context, string name,
        Dictionary<string, string> properties, IReadOnlyBlockDevice? opticalDevice = null, bool caseSensitive = false)
    {
        _fileSystem = fileSystem;
        _context = context;
        _opticalDevice = opticalDevice;
        Name = name;
        VolumeLabel = fileSystem.VolumeLabel;
        IsCaseSensitive = caseSensitive || fileSystem is CDReader cd && cd.ActiveVariant == Iso9660Variant.RockRidge;
        Properties = properties.AsReadOnly();
    }

    internal static DiscFileSystemAdapter OpenNtfs(IReadOnlyBlockDevice device, ParserContext context, CancellationToken token)
    {
        byte[] boot = Bounds.Read(device, 0, 512);
        int sector = U16(boot, 11);
        int sectorsPerCluster = boot[13];
        if (sector is not (512 or 1024 or 2048 or 4096) || sectorsPerCluster == 0
            || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0 || sectorsPerCluster > 128
            || U64(boot, 40) == 0 || U64(boot, 40) > (ulong)(device.Length / sector)
            || boot[510] != 0x55 || boot[511] != 0xaa)
        {
            throw new ImageFormatException("Invalid NTFS boot sector or bounded volume size.");
        }
        int cluster = checked(sector * sectorsPerCluster);
        ValidateRecordSize(boot[64], cluster);
        ValidateRecordSize(boot[68], cluster);
        if (U64(boot, 48) >= (ulong)(device.Length / cluster) || U64(boot, 56) >= (ulong)(device.Length / cluster))
        {
            throw new ImageFormatException("NTFS MFT/MFT mirror location exceeds volume bounds.");
        }
        NtfsReadInfo info = NtfsReadValidation.Validate(device, boot, sector, cluster, token);
        // Only ordinary data/metadata reads are used. Windows SID/ACL/format APIs are never called.
#pragma warning disable CA1416
        var ntfs = new NtfsFileSystem(new DeviceStream(device, context));
#pragma warning restore CA1416
        return new(ntfs, context, "NTFS", new()
        {
            ["Access"] = "Read-only ordinary resident/nonresident file data.",
            ["Unsupported features"] = "Compressed, sparse, encrypted, reparse/link files and alternate data streams are not opened. MFT bootstrap attribute lists remain unsupported.",
            ["Cluster size"] = cluster.ToString(),
            ["NTFS version"] = info.Version,
            ["Attribute lists"] = info.HasAttributeLists ? "Validated by the read-only reader; staged writing is qualified separately per volume." : "None detected.",
        });
    }

    private static void ValidateRecordSize(byte raw, int cluster)
    {
        int value = unchecked((sbyte)raw);
        if (value == 0 || value < -16 || value > 128)
        {
            throw new ImageFormatException("NTFS record/index size exceeds the supported metadata bounds.");
        }
        long size = value < 0 ? 1L << -value : (long)value * cluster;
        if (size is < 512 or > 65536)
        {
            throw new ImageFormatException("NTFS record/index size exceeds the 64 KiB metadata cap.");
        }
    }

    public string Name { get; }
    public string VolumeLabel { get; }
    public bool IsCaseSensitive { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyList<FileEntry> ReadDirectory(string path, CancellationToken cancellationToken = default) =>
        _context.Run<IReadOnlyList<FileEntry>>(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string normalized = ImagePath.Normalize(path);
            string parserPath = CheckPath(normalized);
            if (_fileSystem is CDReader cd)
            {
                // The pinned VFS reparses parents for each entry property; batch ordinary ISO/Joliet metadata.
                if (_opticalDevice is not null && !IsCaseSensitive)
                {
                    if ((_fileSystem.GetAttributes(parserPath) & FileAttributes.Directory) == 0)
                    {
                        throw new DirectoryNotFoundException($"Not an ISO directory: {normalized}");
                    }
                    using var stream = new DeviceStream(_opticalDevice, _context);
                    return IsoDirectoryReader.Read(stream, cd.PathToExtents(parserPath), normalized,
                        Properties["Active naming variant"] == "Joliet", cancellationToken);
                }
                ValidateIsoDirectory(cd, parserPath);
            }
            DiscFileSystemInfo[] entries = _fileSystem.GetDirectoryInfo(parserPath).GetFileSystemInfos();
            if (entries.Length > 131072)
            {
                throw new ImageFormatException("Directory exceeds the 131072-entry metadata cap.");
            }
            var result = new List<FileEntry>(entries.Length);
            var names = new HashSet<string>(IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (DiscFileSystemInfo entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImagePath.CheckName(entry.Name);
                if (!names.Add(entry.Name))
                {
                    throw new ImageFormatException($"Ambiguous duplicate image filename: {entry.Name}");
                }
                string child = normalized == "/" ? "/" + entry.Name : normalized + "/" + entry.Name;
                FileAttributes attributes = entry.Attributes;
                bool link = IsLink(ToParserPath(child), attributes);
                bool directory = (attributes & FileAttributes.Directory) != 0;
                long length = directory || link ? 0 : _fileSystem.GetFileLength(ToParserPath(child));
                if (length < 0)
                {
                    throw new ImageFormatException("Invalid negative image file length.");
                }
                result.Add(new(child, entry.Name, directory, length, entry.LastWriteTimeUtc,
                    attributes.ToString(), IsLink: link));
            }
            return result.AsReadOnly();
        }, cancellationToken);

    public Stream OpenRead(string path) => _context.Run<Stream>(() =>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string normalized = ImagePath.Normalize(path);
        string parserPath = CheckPath(normalized);
        FileAttributes attributes = _fileSystem.GetAttributes(parserPath);
        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new ImageFormatException("Cannot open a directory as a file.");
        }
        if (Name == "NTFS" && (attributes & (FileAttributes.Compressed | FileAttributes.SparseFile | FileAttributes.Encrypted)) != 0)
        {
            throw new ImageFormatException("Compressed, sparse, or encrypted NTFS file contents are not supported.");
        }
        // The pinned ISO extent stream seeks even when its length is zero.
        Stream content = _fileSystem is CDReader && _fileSystem.GetFileLength(parserPath) == 0
            ? new MemoryStream([], false)
            : _fileSystem.OpenFile(parserPath, FileMode.Open, FileAccess.Read);
        var stream = new ParserFileStream(this, content);
        _streams.Add(stream);
        return stream;
    });

    private string CheckPath(string normalized)
    {
        string prefix = "";
        foreach (string component in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            prefix += "\\" + component;
            FileAttributes attributes = _fileSystem.GetAttributes(prefix);
            if (IsLink(prefix, attributes))
            {
                throw new ImageFormatException("Image links/reparse points are not followed.");
            }
        }
        return ToParserPath(normalized);
    }

    private bool IsLink(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }
        return _fileSystem switch
        {
            CDReader { ActiveVariant: Iso9660Variant.RockRidge } cd => cd.GetUnixFileInfo(path).FileType == UnixFileType.Link,
            DiscUtils.Ext.ExtFileSystem ext => ext.GetUnixFileInfo(path).FileType is not (UnixFileType.Regular or UnixFileType.Directory),
            _ => false,
        };
    }

    private static void ValidateIsoDirectory(CDReader cd, string path)
    {
        long total = 0;
        foreach (var extent in cd.PathToExtents(path))
        {
            total = checked(total + extent.Length);
            if (total > 16 * 1024 * 1024)
            {
                throw new ImageFormatException("ISO directory exceeds the 16 MiB metadata safety cap.");
            }
        }
    }

    private static string ToParserPath(string normalized) => normalized == "/" ? "\\" : normalized.Replace('/', '\\');

    public void Dispose()
    {
        lock (_context.Gate)
        {
            if (_disposed)
            {
                return;
            }
            foreach (ParserFileStream stream in _streams.ToArray())
            {
                stream.Dispose();
            }
            _fileSystem.Dispose();
            _disposed = true;
        }
    }

    private sealed class ParserFileStream(DiscFileSystemAdapter owner, Stream inner) : Stream
    {
        private bool _closed;
        public override bool CanRead => !_closed;
        public override bool CanSeek => !_closed;
        public override bool CanWrite => false;
        public override long Length => owner._context.Run(() => inner.Length);
        public override long Position
        {
            get => owner._context.Run(() => inner.Position);
            set => owner._context.Run(() =>
            {
                ObjectDisposedException.ThrowIf(_closed || owner._disposed, this);
                Bounds.Check(value, 0, inner.Length);
                inner.Position = value;
                return 0;
            });
        }
        public override int Read(byte[] buffer, int offset, int count) => owner._context.Run(() =>
        {
            ObjectDisposedException.ThrowIf(_closed || owner._disposed, this);
            return inner.Read(buffer, offset, Math.Min(count, 1024 * 1024));
        });
        public override int Read(Span<byte> buffer)
        {
            byte[] bytes = new byte[Math.Min(buffer.Length, 1024 * 1024)];
            int count = Read(bytes, 0, bytes.Length);
            bytes.AsSpan(0, count).CopyTo(buffer);
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = new byte[Math.Min(buffer.Length, 1024 * 1024)];
            int count = owner._context.Run(() =>
            {
                ObjectDisposedException.ThrowIf(_closed || owner._disposed, this);
                return inner.Read(bytes, 0, bytes.Length);
            }, cancellationToken);
            bytes.AsMemory(0, count).CopyTo(buffer);
            return ValueTask.FromResult(count);
        }
        public override long Seek(long offset, SeekOrigin origin) => owner._context.Run(() =>
        {
            ObjectDisposedException.ThrowIf(_closed || owner._disposed, this);
            long position = checked(origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => inner.Position + offset,
                SeekOrigin.End => inner.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            });
            Bounds.Check(position, 0, inner.Length);
            return inner.Seek(position, SeekOrigin.Begin);
        });
        public override void Flush() => throw new NotSupportedException("Read-only image.");
        public override void SetLength(long value) => throw new NotSupportedException("Read-only image.");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read-only image.");
        protected override void Dispose(bool disposing)
        {
            lock (owner._context.Gate)
            {
                if (!_closed && disposing)
                {
                    inner.Dispose();
                    owner._streams.Remove(this);
                    _closed = true;
                }
            }
            base.Dispose(disposing);
        }
    }
}
