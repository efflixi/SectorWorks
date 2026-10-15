using DiscUtils;
using System;

namespace SectorWorks.ManagedNtfs
{
    [Flags]
    internal enum AttributeFlags : ushort
    {
        None = 0x0000,
        Compressed = 0x0001,
        Encrypted = 0x4000,
        Sparse = 0x8000
    }
}