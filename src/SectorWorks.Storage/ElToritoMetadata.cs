using SectorWorks.Core;
using static SectorWorks.Storage.OpticalBinary;
using static SectorWorks.Storage.OpticalLayout;

namespace SectorWorks.Storage;

internal static class ElToritoMetadata
{
    // Catalog metadata is descriptive only. A boot entry can have firmware,
    // filesystem or geometry dependencies not represented by these fields.
    internal static string Inspect(IReadOnlyBlockDevice device, uint catalog)
    {
        if ((long)catalog * SectorSize > device.Length - SectorSize)
        { return "Truncated catalog; no bootability guarantee."; }
        byte[] data = Sector(device, checked((int)catalog));
        int sum = 0;
        for (int offset = 0; offset < 32; offset += 2) { sum += U16(data, offset); }
        if (data[0] != 1 || (sum & 65535) != 0 || data[30] != 0x55 || data[31] != 0xaa)
        { return "Invalid validation entry; no bootability guarantee."; }
        var platforms = new HashSet<string>(StringComparer.Ordinal);
        bool unknown = AddEntry(data.AsSpan(32, 32), data[1]);
        for (int cursor = 64; cursor < SectorSize;)
        {
            if (data.AsSpan(cursor, 32).IndexOfAnyExcept((byte)0) < 0) { break; }
            if (data[cursor] is not (0x90 or 0x91)) { unknown = true; break; }
            bool final = data[cursor] == 0x91;
            byte platform = data[cursor + 1];
            int count = U16(data, cursor + 2);
            cursor += 32;
            if (count == 0 || count > (SectorSize - cursor) / 32) { unknown = true; break; }
            for (int index = 0; index < count; index++, cursor += 32)
            {
                unknown |= AddEntry(data.AsSpan(cursor, 32), platform);
            }
            if (final) { break; }
        }
        return (platforms.Count == 0 ? "No bounded BIOS/UEFI boot entries found"
            : string.Join(", ", platforms.Order(StringComparer.Ordinal))) + (unknown ? "; unqualified entries present" : "")
            + "; descriptive only, not a bootability guarantee.";

        bool AddEntry(ReadOnlySpan<byte> entry, byte platform)
        {
            if (entry[0] == 0) { return false; }
            uint block = U32(entry, 8);
            long offset = (long)block * SectorSize;
            int bytes = Math.Max(512, U16(entry, 6) * 512);
            if (entry[0] != 0x88 || block < 16 || offset > device.Length - bytes || platform is not (0 or 0xef))
            { return true; }
            platforms.Add(platform == 0 ? "BIOS" : "UEFI");
            return false;
        }
    }
}

internal sealed record IsoBootLayout(int DescriptorBlock, byte[] Descriptor, int End)
{
    internal static IsoBootLayout Read(IReadOnlyBlockDevice device, int descriptorBlock, byte[] descriptor,
        OpticalNode root, CancellationToken token)
    {
        const int bootRegionLimit = 8192;
        const int recordSize = 32;
        const int maximumCatalogEntries = 8;
        int catalog = checked((int)U32(descriptor, 71));
        if (catalog is < 16 or >= bootRegionLimit)
        { throw new NotSupportedException("El Torito catalog is outside the bounded fixed boot region."); }

        // The catalog and full boot images must be ordinary, unique files in the
        // already checked namespace. This avoids treating only the firmware load
        // count as the complete extent or copying unaccounted system-area bytes.
        var files = new Dictionary<int, OpticalNode>();
        var ambiguous = new HashSet<int>();
        var queue = new Queue<OpticalNode>();
        queue.Enqueue(root);
        while (queue.TryDequeue(out OpticalNode? node))
        {
            token.ThrowIfCancellationRequested();
            int block = checked((int)(node.Offset / SectorSize));
            if (node.Directory)
            {
                foreach (OpticalNode child in node.Children) { queue.Enqueue(child); }
            }
            else if (node.Length != 0 && !files.TryAdd(block, node)) { ambiguous.Add(block); }
        }
        OpticalNode catalogFile = RequireMember(catalog);
        if (catalogFile.Length != SectorSize)
        { throw new NotSupportedException("El Torito catalog is not one complete namespace sector."); }
        byte[] data = Sector(device, catalog);
        int checksum = 0;
        for (int i = 0; i < recordSize; i += 2) { checksum += U16(data, i); }
        if (data[0] != 1 || data[1] is not (0 or 0xef) || U16(data, 2) != 0 || (checksum & 65535) != 0
            || data[30] != 0x55 || data[31] != 0xaa)
        { throw new NotSupportedException("El Torito catalog validation entry is not qualified."); }

        int end = catalog + 1;
        catalogFile.FixedBootBlock = catalog;
        int count = 0;
        if (data.AsSpan(recordSize, recordSize).ContainsAnyExcept((byte)0))
        {
            AddEntry(recordSize);
        }
        int cursor = 2 * recordSize;
        while (data.AsSpan(cursor).ContainsAnyExcept((byte)0))
        {
            if (cursor > SectorSize - 2 * recordSize || data[cursor] is not (0x90 or 0x91)
                || data[cursor + 1] is not (0 or 0xef))
            { throw new NotSupportedException("Unsupported El Torito section header or platform."); }
            bool last = data[cursor] == 0x91;
            int entries = U16(data, cursor + 2);
            if (entries == 0 || entries > maximumCatalogEntries - count || entries > (SectorSize - cursor - recordSize) / recordSize)
            { throw new NotSupportedException("El Torito section entry count exceeds the catalog bound."); }
            cursor += recordSize;
            for (int i = 0; i < entries; i++, cursor += recordSize) { AddEntry(cursor); }
            if (last)
            {
                if (data.AsSpan(cursor).ContainsAnyExcept((byte)0))
                { throw new NotSupportedException("Unrecognized data follows the final El Torito section."); }
                break;
            }
            if (!data.AsSpan(cursor).ContainsAnyExcept((byte)0))
            { throw new NotSupportedException("El Torito section chain has no final header."); }
        }
        if (count == 0) { throw new NotSupportedException("No qualified no-emulation BIOS/UEFI boot entries."); }
        return new(descriptorBlock, descriptor, end);

        OpticalNode RequireMember(int block)
        {
            if (!files.TryGetValue(block, out OpticalNode? member) || ambiguous.Contains(block))
            { throw new NotSupportedException("The complete El Torito catalog and boot images must be unique ordinary namespace files."); }
            return member;
        }

        void AddEntry(int offset)
        {
            ReadOnlySpan<byte> entry = data.AsSpan(offset, recordSize);
            int loadBytes = U16(entry, 6) * 512;
            int block = checked((int)U32(entry, 8));
            if (entry[0] != 0x88 || entry[1] != 0 || entry[4] != 0 || entry[5] != 0 || loadBytes == 0
                || block == catalog || entry[12..].ContainsAnyExcept((byte)0) || ++count > maximumCatalogEntries)
            { throw new NotSupportedException("El Torito emulation, selection criteria or boot entry flags are not qualified."); }
            OpticalNode member = RequireMember(block);
            int after = checked(block + Blocks(member.Length));
            if (member.Length < loadBytes || after > bootRegionLimit)
            { throw new NotSupportedException("El Torito load size or complete fixed boot extent is not qualified."); }
            member.FixedBootBlock = block;
            end = Math.Max(end, after);
        }
    }
}
