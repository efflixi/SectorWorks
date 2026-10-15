#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using DiscUtils.Streams;

namespace SectorWorks.ManagedNtfs;

public sealed partial class NtfsFileSystem
{
    /// <summary>
    /// Captures the selected name's FILE_NAME and directory-index timestamp quads
    /// independently. Each quad is creation, modification, MFT-change, access
    /// FILETIME values (little-endian Int64). Root has only the 32-byte attribute
    /// quad; other names have 64 bytes. Capture DOS alias paths separately.
    /// </summary>
    public byte[] GetFileNameTimestamps(string path)
    {
        DirectoryEntry entry = RequireTimestampEntry(path);
        File file = GetFile(entry.Reference);
        NtfsStream nameStream = FindTimestampNameStream(file, entry);
        bool root = file.IndexInMft == MasterFileTable.RootDirIndex;
        byte[] result = new byte[root ? 32 : 64];
        using (Stream stream = nameStream.Open(FileAccess.Read))
        {
            stream.Position = 8;
            StreamUtilities.ReadExact(stream, result, 0, 32);
        }
        _ = ReadTimestampQuad(result.AsSpan(0, 32));
        if (!root)
        {
            WriteTimestampQuad(entry.Details, result.AsSpan(32, 32));
        }
        return result;
    }

    /// <summary>
    /// Restores only the selected FILE_NAME/index timestamp quads, without
    /// regenerating them from SI or changing names, sizes, flags, IDs or SI.
    /// Call after all operations and standard-information/alias restoration.
    /// </summary>
    public void SetFileNameTimestamps(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (NtfsTransaction.Current != null)
        {
            throw new InvalidOperationException("Explicit metadata restoration cannot run inside a timestamping transaction.");
        }
        DirectoryEntry entry = RequireTimestampEntry(path);
        File file = GetFile(entry.Reference);
        bool root = file.IndexInMft == MasterFileTable.RootDirIndex;
        if (bytes.Length != (root ? 32 : 64))
        {
            throw new ArgumentException("Expected a 32-byte root or 64-byte ordinary-name timestamp snapshot.", nameof(bytes));
        }
        TimestampQuad attributeTimes = ReadTimestampQuad(bytes.AsSpan(0, 32));
        TimestampQuad indexTimes = root ? default : ReadTimestampQuad(bytes.AsSpan(32, 32));
        NtfsStream nameStream = FindTimestampNameStream(file, entry);
        FileNameRecord name = nameStream.GetContent<FileNameRecord>();
        SetTimestampQuad(name, attributeTimes);
        nameStream.SetContent(name);
        file.UpdateRecordInMft();
        if (!root)
        {
            SetTimestampQuad(entry.Details, indexTimes);
            GetDirectory(entry.Details.ParentDirectory).UpdateEntry(entry);
        }
    }

    private DirectoryEntry RequireTimestampEntry(string path)
    {
        DirectoryEntry entry = GetDirectoryEntry(path);
        if (entry == null)
        {
            throw new FileNotFoundException("File not found", path);
        }
        return entry;
    }

    private static NtfsStream FindTimestampNameStream(File file, DirectoryEntry entry)
    {
        NtfsStream? result = null;
        foreach (NtfsStream stream in file.GetStreams(AttributeType.FileName, null))
        {
            using (Stream content = stream.Open(FileAccess.Read))
            {
                if (content.Length is < 0x42 or > 0x42 + 510)
                {
                    throw new IOException("FILE_NAME attribute exceeds the bounded ordinary-name layout.");
                }
            }
            if (stream.GetContent<FileNameRecord>().Equals(entry.Details))
            {
                if (result != null)
                {
                    throw new IOException("Ambiguous FILE_NAME metadata for the selected name.");
                }
                result = stream;
            }
        }
        return result ?? throw new IOException("No matching FILE_NAME metadata for the selected name.");
    }

    private static TimestampQuad ReadTimestampQuad(ReadOnlySpan<byte> bytes)
    {
        return new TimestampQuad(
            DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(bytes)),
            DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(bytes[8..])),
            DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(bytes[16..])),
            DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(bytes[24..])));
    }

    private static void WriteTimestampQuad(FileNameRecord name, Span<byte> bytes)
    {
        BinaryPrimitives.WriteInt64LittleEndian(bytes, name.CreationTime.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], name.ModificationTime.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(bytes[16..], name.MftChangedTime.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(bytes[24..], name.LastAccessTime.ToFileTimeUtc());
    }

    private static void SetTimestampQuad(FileNameRecord name, TimestampQuad times)
    {
        name.CreationTime = times.Creation;
        name.ModificationTime = times.Modification;
        name.MftChangedTime = times.Change;
        name.LastAccessTime = times.Access;
    }

    private readonly record struct TimestampQuad(DateTime Creation, DateTime Modification, DateTime Change, DateTime Access);
}
