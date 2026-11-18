using Microsoft.Win32.SafeHandles;
using SectorWorks.Core;

namespace SectorWorks.Storage;

public sealed class FileBlockDevice : IReadOnlyBlockDevice
{
    private readonly SafeFileHandle _handle;

    public FileBlockDevice(string path, int logicalSectorSize = 512)
        : this(File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess), logicalSectorSize) { }

    internal FileBlockDevice(SafeFileHandle handle, int logicalSectorSize)
    {
        _handle = handle;
        try { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalSectorSize); Length = RandomAccess.GetLength(_handle); }
        catch { handle.Dispose(); throw; }
        LogicalSectorSize = logicalSectorSize;
    }

    public long Length { get; }
    public int LogicalSectorSize { get; }
    public int? PhysicalSectorSize => null;
    public bool IsReadOnly => true;

    public int Read(long offset, Span<byte> buffer)
    {
        Bounds.Check(offset, buffer.Length, Length);
        return RandomAccess.Read(_handle, buffer[..Math.Min(buffer.Length, 1024 * 1024)], offset);
    }

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Bounds.Check(offset, buffer.Length, Length);
        return RandomAccess.ReadAsync(_handle, buffer[..Math.Min(buffer.Length, 1024 * 1024)], offset, cancellationToken);
    }

    public void Dispose() => _handle.Dispose();
}

public sealed class PartitionBlockDevice : IReadOnlyBlockDevice
{
    private readonly IReadOnlyBlockDevice _parent;
    private readonly long _offset;
    private bool _disposed;

    public PartitionBlockDevice(IReadOnlyBlockDevice parent, long offset, long length)
    {
        Bounds.Check(offset, length, parent.Length);
        _parent = parent;
        _offset = offset;
        Length = length;
    }

    public long Length { get; }
    public int LogicalSectorSize => _parent.LogicalSectorSize;
    public int? PhysicalSectorSize => _parent.PhysicalSectorSize;
    public bool IsReadOnly => true;

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bounds.Check(offset, buffer.Length, Length);
        return _parent.Read(checked(_offset + offset), buffer);
    }

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bounds.Check(offset, buffer.Length, Length);
        return _parent.ReadAsync(checked(_offset + offset), buffer, cancellationToken);
    }

    public void Dispose() => _disposed = true;
}

internal static class Bounds
{
    internal static void Check(long offset, long count, long length)
    {
        if (offset < 0 || count < 0 || offset > length || count > length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), $"Range {offset}+{count} is outside device length {length}.");
        }
    }

    internal static byte[] Read(IReadOnlyBlockDevice device, long offset, int count)
    {
        var bytes = new byte[count];
        ReadExactly(device, offset, bytes);
        return bytes;
    }

    internal static void ReadExactly(IReadOnlyBlockDevice device, long offset, Span<byte> bytes)
    {
        Bounds.Check(offset, bytes.Length, device.Length);
        int total = 0;
        while (total < bytes.Length)
        {
            int read = device.Read(checked(offset + total), bytes[total..]);
            if (read == 0)
            {
                throw new ImageFormatException("Image ended unexpectedly while reading a declared extent.");
            }
            total += read;
        }
    }
}

internal sealed class ParserContext
{
    internal object Gate { get; } = new();
    private CancellationToken _token;
    private long _remaining;
    private int _operations;
    private int _depth;

    internal T Run<T>(Func<T> action, CancellationToken token = default)
    {
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            if (_depth++ == 0)
            {
                _token = token;
                _remaining = 128L * 1024 * 1024;
                _operations = 131072;
            }
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is IOException and not ImageFormatException
                or ArgumentException or OverflowException or IndexOutOfRangeException
                or NotSupportedException or InvalidOperationException)
            {
                throw new ImageFormatException($"Invalid or unsupported image metadata: {ex.Message}", ex);
            }
            finally
            {
                if (--_depth == 0)
                {
                    _token = default;
                }
            }
        }
    }

    internal void Consume(int bytes)
    {
        _token.ThrowIfCancellationRequested();
        _remaining -= bytes;
        if (--_operations < 0 || _remaining < 0)
        {
            throw new ImageFormatException("Parser resource budget exceeded (128 MiB / 131072 reads or seeks per operation).");
        }
    }
}

internal sealed class DeviceStream(IReadOnlyBlockDevice device, ParserContext? context = null, bool ownsDevice = false) : Stream
{
    private long _position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => device.Length;
    public override long Position
    {
        get => _position;
        set
        {
            context?.Consume(0);
            Bounds.Check(value, 0, Length);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(Math.Min(buffer.Length, 1024 * 1024), Length - _position);
        context?.Consume(count);
        int read = device.Read(_position, buffer[..count]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = checked(origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        });
        return Position;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && ownsDevice) { device.Dispose(); }
        base.Dispose(disposing);
    }

    public override void Flush() => throw new NotSupportedException("Read-only image.");
    public override void SetLength(long value) => throw new NotSupportedException("Read-only image.");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Read-only image.");
}

internal sealed class StreamBlockDevice(Stream stream, ParserContext context, int sectorSize = 512,
    int? physicalSectorSize = null) : IReadOnlyBlockDevice
{
    private bool _disposed;
    public long Length => stream.Length;
    public int LogicalSectorSize => sectorSize;
    public int? PhysicalSectorSize => physicalSectorSize;
    public int Read(long offset, Span<byte> buffer) => ReadCore(offset, buffer, default);

    private int ReadCore(long offset, Span<byte> buffer, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bounds.Check(offset, buffer.Length, Length);
        int requested = Math.Min(buffer.Length, 1024 * 1024);
        byte[] bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(requested);
        try
        {
            int count = context.Run(() =>
            {
                stream.Position = offset;
                return stream.Read(bytes, 0, requested);
            }, token);
            bytes.AsSpan(0, count).CopyTo(buffer);
            return count;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ReadCore(offset, buffer.Span, cancellationToken));
    }

    public void Dispose()
    {
        lock (context.Gate)
        {
            _disposed = true;
        }
    }
}
