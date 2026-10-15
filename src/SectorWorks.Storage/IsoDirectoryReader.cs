using System.Buffers.Binary;
using System.Text;
using DiscUtils.Streams;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal static class IsoDirectoryReader
{
    private static readonly Encoding JolietEncoding = new UnicodeEncoding(true, false, true);

    internal static IReadOnlyList<FileEntry> Read(Stream stream, StreamExtent[] extents, string path,
        bool joliet, CancellationToken token)
    {
        long length = 0;
        foreach (StreamExtent extent in extents)
        {
            Bounds.Check(extent.Start, extent.Length, stream.Length);
            length = checked(length + extent.Length);
            if (length > 16 * 1024 * 1024)
            {
                throw new ImageFormatException("ISO directory exceeds the 16 MiB metadata safety cap.");
            }
        }
        if (length < 34)
        {
            throw new ImageFormatException("ISO directory has an invalid metadata length.");
        }
        var data = new byte[checked((int)length)];
        int copied = 0;
        foreach (StreamExtent extent in extents)
        {
            token.ThrowIfCancellationRequested();
            stream.Position = extent.Start;
            stream.ReadExactly(data.AsSpan(copied, checked((int)extent.Length)));
            copied = checked(copied + (int)extent.Length);
        }
        var result = new List<FileEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int position = 0; position < data.Length;)
        {
            token.ThrowIfCancellationRequested();
            int count = data[position];
            if (count == 0)
            {
                position = checked((position / 2048 + 1) * 2048);
                continue;
            }
            if (count < 34 || count > data.Length - position || position % 2048 + count > 2048)
            {
                throw new ImageFormatException("ISO directory record exceeds its sector or directory extent.");
            }
            ReadOnlySpan<byte> record = data.AsSpan(position, count);
            int identifierLength = record[32];
            if (identifierLength == 0 || identifierLength > count - 33)
            {
                throw new ImageFormatException("Invalid ISO directory identifier length.");
            }
            position += count;
            if (identifierLength == 1 && record[33] is 0 or 1)
            {
                continue;
            }
            if (result.Count >= 131072)
            {
                throw new ImageFormatException("Directory exceeds the 131072-entry metadata cap.");
            }
            string name = (joliet && identifierLength != 1 ? JolietEncoding : Encoding.ASCII)
                .GetString(record.Slice(33, identifierLength)).TrimEnd(' ');
            int version = name.LastIndexOf(';');
            if (version > 0)
            {
                name = name[..version];
            }
            ImagePath.CheckName(name);
            if (!names.Add(name))
            {
                throw new ImageFormatException($"Ambiguous duplicate image filename: {name}");
            }
            bool directory = (record[25] & 2) != 0;
            FileAttributes attributes = FileAttributes.ReadOnly
                | (directory ? FileAttributes.Directory : 0)
                | ((record[25] & 1) != 0 ? FileAttributes.Hidden : 0);
            DateTime modified = new DateTime(1900 + record[18], record[19], record[20],
                record[21], record[22], record[23], DateTimeKind.Utc)
                .AddMinutes(-15 * unchecked((sbyte)record[24]));
            long bytes = directory ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(record[10..]);
            string child = path == "/" ? "/" + name : path + "/" + name;
            result.Add(new(child, name, directory, bytes, modified, attributes.ToString()));
        }
        return result.AsReadOnly();
    }
}
