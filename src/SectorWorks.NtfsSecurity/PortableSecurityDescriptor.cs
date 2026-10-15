using System.Buffers.Binary;

namespace SectorWorks.NtfsSecurity;

/// <summary>
/// Bounded, host-independent codec for self-relative NTFS security descriptors.
/// This is not an access-check engine or an SDDL/token implementation.
/// </summary>
public sealed class PortableSecurityDescriptor
{
    public const int MaximumBinaryLength = 65536;
    private const ushort SelfRelative = 0x8000;
    private const ushort DaclPresent = 0x0004;
    private const ushort SaclPresent = 0x0010;
    private const ushort ResourceManagerControlValid = 0x4000;
    private const byte ObjectInherit = 0x01;
    private const byte ContainerInherit = 0x02;
    private const byte NoPropagate = 0x04;
    private const byte InheritOnly = 0x08;
    private const byte Inherited = 0x10;
    private const uint GenericRights = 0xf0000000;
    private readonly byte[]? owner;
    private readonly byte[]? group;
    private readonly Acl? dacl;
    private readonly Acl? sacl;

    private PortableSecurityDescriptor(ushort control, byte resourceManagerControl, byte[]? owner,
        byte[]? group, Acl? dacl, Acl? sacl)
    {
        Control = control;
        ResourceManagerControl = resourceManagerControl;
        this.owner = owner;
        this.group = group;
        this.dacl = dacl;
        this.sacl = sacl;
    }

    public ushort Control { get; }
    public byte ResourceManagerControl { get; }
    public bool HasDiscretionaryAcl => (Control & DaclPresent) != 0;
    public bool HasSystemAcl => (Control & SaclPresent) != 0;
    public bool IsDiscretionaryAclNull => HasDiscretionaryAcl && dacl is null;
    public bool IsSystemAclNull => HasSystemAcl && sacl is null;
    public byte[]? OwnerSid => owner?.ToArray();
    public byte[]? GroupSid => group?.ToArray();

    /// <summary>
    /// Parses revision-one self-relative descriptors, rejecting ambiguous extents,
    /// nonzero padding, malformed known ACE layouts and reserved fields. Unknown ACE
    /// bodies are retained verbatim but are never accepted for inheritance.
    /// </summary>
    public static PortableSecurityDescriptor Parse(ReadOnlySpan<byte> binary)
    {
        if (binary.Length is < 20 or > MaximumBinaryLength || binary[0] != 1)
        {
            throw Invalid("Invalid security descriptor size or revision.");
        }

        ushort control = U16(binary, 2);
        if ((control & SelfRelative) == 0 ||
            ((control & ResourceManagerControlValid) == 0 && binary[1] != 0))
        {
            throw Invalid("A self-relative descriptor and valid resource-manager control are required.");
        }

        int ownerOffset = Offset(binary, 4);
        int groupOffset = Offset(binary, 8);
        int saclOffset = Offset(binary, 12);
        int daclOffset = Offset(binary, 16);
        if (((control & DaclPresent) == 0 && daclOffset != 0) ||
            ((control & SaclPresent) == 0 && saclOffset != 0))
        {
            throw Invalid("An absent ACL cannot have a nonzero offset.");
        }

        var extents = new List<(int Start, int Length)>(4);
        byte[]? owner = ReadSid(binary, ownerOffset, extents);
        byte[]? group = ReadSid(binary, groupOffset, extents);
        Acl? dacl = ReadAcl(binary, daclOffset, extents);
        Acl? sacl = ReadAcl(binary, saclOffset, extents);
        extents.Sort((a, b) => a.Start.CompareTo(b.Start));
        int end = 20;
        foreach ((int start, int length) in extents)
        {
            if (start < end)
            {
                throw Invalid("Security descriptor components overlap.");
            }

            RequireZero(binary.Slice(end, start - end));
            end = checked(start + length);
        }

        RequireZero(binary[end..]);
        return new PortableSecurityDescriptor(control, binary[1], owner, group, dacl, sacl);
    }

    /// <summary>
    /// Parses a descriptor at the start of an enclosing serialization buffer.
    /// Consumed bytes are the original component extent, not the canonical
    /// serialized length. Bytes after that extent belong to the caller.
    /// </summary>
    public static PortableSecurityDescriptor ParsePrefix(ReadOnlySpan<byte> buffer, out int consumed)
    {
        consumed = 0;
        if (buffer.Length < 20)
        {
            throw Invalid("Truncated security descriptor header.");
        }

        ReadOnlySpan<byte> bounded = buffer[..Math.Min(buffer.Length, MaximumBinaryLength)];
        int length = 20;
        foreach (int field in new[] { 4, 8, 12, 16 })
        {
            int offset = Offset(bounded, field);
            if (offset == 0)
            {
                continue;
            }

            int componentLength;
            if (field is 4 or 8)
            {
                componentLength = SidLength(bounded[offset..]);
            }
            else
            {
                if (bounded.Length - offset < 8)
                {
                    throw Invalid("Truncated ACL header.");
                }

                componentLength = U16(bounded, offset + 2);
            }

            int end = checked(offset + componentLength);
            if (end > bounded.Length)
            {
                throw Invalid("Security descriptor component exceeds the enclosing buffer or 64 KiB budget.");
            }

            length = Math.Max(length, end);
        }

        PortableSecurityDescriptor descriptor = Parse(bounded[..length]);
        consumed = length;
        return descriptor;
    }

    /// <summary>Serializes DWORD-aligned components in NTFS order: DACL, SACL, owner, group.</summary>
    public byte[] ToBinary()
    {
        int length = checked(20 + (dacl?.Binary.Length ?? 0) + (sacl?.Binary.Length ?? 0) +
            (owner?.Length ?? 0) + (group?.Length ?? 0));
        if (length > MaximumBinaryLength)
        {
            throw Invalid("Security descriptor exceeds the 64 KiB budget.");
        }

        var result = new byte[length];
        result[0] = 1;
        result[1] = ResourceManagerControl;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), Control);
        int position = 20;
        WriteComponent(result, 16, dacl?.Binary, ref position);
        WriteComponent(result, 12, sacl?.Binary, ref position);
        WriteComponent(result, 4, owner, ref position);
        WriteComponent(result, 8, group, ref position);
        return result;
    }

    /// <summary>
    /// Rejects security semantics outside ordinary basic allow/deny/audit inheritance,
    /// including unsupported ACEs even when not inheritable. No permissive fallback exists.
    /// </summary>
    public void ValidateSupportedInheritance()
    {
        if ((Control & 0x00c0) != 0)
        {
            throw new NotSupportedException("Server-security and untrusted-DACL token semantics are unsupported.");
        }

        ValidateAcl(dacl, false);
        ValidateAcl(sacl, true);
    }

    /// <summary>
    /// Creates a new image object's descriptor. Owner and primary group are copied
    /// exactly from the image parent, never from the host user or a process token.
    /// Parent protection/default/request/RM flags do not become child flags; ACL
    /// presence/null/empty remain distinct and present child ACLs are auto-inherited.
    /// </summary>
    public PortableSecurityDescriptor Inherit(bool isDirectory)
    {
        ValidateSupportedInheritance();
        Acl? childDacl = InheritAcl(dacl, isDirectory);
        Acl? childSacl = InheritAcl(sacl, isDirectory);
        ushort control = (ushort)(SelfRelative | (Control & (DaclPresent | SaclPresent)));
        if (HasDiscretionaryAcl)
        {
            control |= 0x0400;
        }

        if (HasSystemAcl)
        {
            control |= 0x0800;
        }

        var child = new PortableSecurityDescriptor(control, 0, owner, group, childDacl, childSacl);
        _ = child.ToBinary();
        return child;
    }

    private void ValidateAcl(Acl? acl, bool isSystemAcl)
    {
        if (acl is null)
        {
            return;
        }

        bool seenExplicitAllow = false;
        bool seenInherited = false;
        foreach (byte[] ace in acl.Aces)
        {
            if ((isSystemAcl && ace[0] != 2) || (!isSystemAcl && ace[0] is not (0 or 1)))
            {
                throw new NotSupportedException("Only basic access allow/deny DACL and audit SACL ACEs can inherit.");
            }

            if (!isSystemAcl)
            {
                bool isInherited = (ace[1] & Inherited) != 0;
                if (!isInherited && (seenInherited || (ace[0] == 1 && seenExplicitAllow)))
                {
                    throw new NotSupportedException("Noncanonical explicit DACL order cannot be inherited safely.");
                }

                seenInherited |= isInherited;
                seenExplicitAllow |= !isInherited && ace[0] == 0;
            }

            if ((ace[1] & (ObjectInherit | ContainerInherit)) == 0 &&
                (ace[1] & (NoPropagate | InheritOnly)) != 0)
            {
                throw new NotSupportedException("Propagation modifiers without inheritance targets are unsupported.");
            }

            ReadOnlySpan<byte> sid = ace.AsSpan(8);
            if ((ace[1] & (ObjectInherit | ContainerInherit)) != 0 &&
                ((IsCreator(sid, 0) && owner is null) || (IsCreator(sid, 1) && group is null)))
            {
                throw new NotSupportedException("A creator SID requires the corresponding image-parent owner/group.");
            }

            if (IsCreator(sid, 2) || IsCreator(sid, 3) || IsCreator(sid, 4))
            {
                throw new NotSupportedException("Creator server and owner-rights SID semantics are unsupported.");
            }
        }
    }

    private Acl? InheritAcl(Acl? acl, bool isDirectory)
    {
        if (acl is null)
        {
            return null;
        }

        var inherited = new List<byte[]>();
        foreach (byte[] ace in acl.Aces)
        {
            byte flags = ace[1];
            bool objectInherit = (flags & ObjectInherit) != 0;
            bool containerInherit = (flags & ContainerInherit) != 0;
            bool noPropagate = (flags & NoPropagate) != 0;
            if ((!isDirectory && !objectInherit) ||
                (isDirectory && !containerInherit && (!objectInherit || noPropagate)))
            {
                continue;
            }

            byte auditFlags = (byte)(flags & 0xc0);
            if (!isDirectory)
            {
                inherited.Add(EffectiveAce(ace, (byte)(auditFlags | Inherited)));
                continue;
            }

            byte propagation = noPropagate ? (byte)0 : (byte)(flags & (ObjectInherit | ContainerInherit));
            if (!containerInherit)
            {
                inherited.Add(CloneAce(ace, (byte)(auditFlags | propagation | InheritOnly | Inherited)));
                continue;
            }

            bool generic = (U32(ace, 4) & GenericRights) != 0 ||
                IsCreator(ace.AsSpan(8), 0) || IsCreator(ace.AsSpan(8), 1);
            // Effective generic rights/SIDs must not replace the generic template
            // needed for the next generation's owner/group and rights mapping.
            if (generic && propagation != 0)
            {
                inherited.Add(EffectiveAce(ace, (byte)(auditFlags | Inherited)));
                inherited.Add(CloneAce(ace, (byte)(auditFlags | propagation | InheritOnly | Inherited)));
            }
            else
            {
                inherited.Add(EffectiveAce(ace, (byte)(auditFlags | propagation | Inherited)));
            }
        }

        // Parent explicit ACEs precede the older inherited generations. Sorting
        // all inherited denies first would change an ancestor's access semantics.
        return BuildAcl(acl.Binary[0], inherited);
    }

    private byte[] EffectiveAce(byte[] original, byte flags)
    {
        ReadOnlySpan<byte> originalSid = original.AsSpan(8);
        byte[] sid = IsCreator(originalSid, 0) ? owner! :
            IsCreator(originalSid, 1) ? group! : originalSid.ToArray();
        var result = new byte[checked(8 + sid.Length)];
        result[0] = original[0];
        result[1] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), checked((ushort)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), MapFileGenericMask(U32(original, 4)));
        sid.CopyTo(result, 8);
        return result;
    }

    /// <summary>Maps FILE_GENERIC_READ/WRITE/EXECUTE/ALL, retaining all nongeneric mask bits.</summary>
    public static uint MapFileGenericMask(uint mask)
    {
        uint mapped = mask & ~GenericRights;
        if ((mask & 0x80000000) != 0)
        {
            mapped |= 0x00120089;
        }

        if ((mask & 0x40000000) != 0)
        {
            mapped |= 0x00120116;
        }

        if ((mask & 0x20000000) != 0)
        {
            mapped |= 0x001200a0;
        }

        if ((mask & 0x10000000) != 0)
        {
            mapped |= 0x001f01ff;
        }

        return mapped;
    }

    private static bool IsCreator(ReadOnlySpan<byte> sid, uint kind) =>
        sid.Length == 12 && sid[0] == 1 && sid[1] == 1 &&
        sid.Slice(2, 5).IndexOfAnyExcept((byte)0) < 0 && sid[7] == 3 && U32(sid, 8) == kind;

    private static byte[] CloneAce(byte[] original, byte flags)
    {
        byte[] result = original.ToArray();
        result[1] = flags;
        return result;
    }

    private static Acl BuildAcl(byte revision, List<byte[]> aces)
    {
        int length = 8;
        foreach (byte[] ace in aces)
        {
            length = checked(length + ace.Length);
        }

        if (length > ushort.MaxValue || aces.Count > ushort.MaxValue)
        {
            throw Invalid("Inherited ACL exceeds the binary ACL budget.");
        }

        var binary = new byte[length];
        binary[0] = revision;
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(4), (ushort)aces.Count);
        int position = 8;
        foreach (byte[] ace in aces)
        {
            ace.CopyTo(binary, position);
            position = checked(position + ace.Length);
        }

        return new Acl(binary, aces);
    }

    private static byte[]? ReadSid(ReadOnlySpan<byte> binary, int offset, List<(int, int)> extents)
    {
        if (offset == 0)
        {
            return null;
        }

        int length = SidLength(binary[offset..]);
        extents.Add((offset, length));
        return binary.Slice(offset, length).ToArray();
    }

    private static int SidLength(ReadOnlySpan<byte> binary)
    {
        if (binary.Length < 8 || binary[0] != 1 || binary[1] > 15)
        {
            throw Invalid("Invalid SID revision, count or header.");
        }

        int length = checked(8 + 4 * binary[1]);
        if (length > binary.Length)
        {
            throw Invalid("Truncated SID.");
        }

        return length;
    }

    private static Acl? ReadAcl(ReadOnlySpan<byte> binary, int offset, List<(int, int)> extents)
    {
        if (offset == 0)
        {
            return null;
        }

        ReadOnlySpan<byte> acl = binary[offset..];
        if (acl.Length < 8 || acl[0] is not (2 or 4) || acl[1] != 0 || U16(acl, 6) != 0)
        {
            throw Invalid("Invalid ACL header or revision.");
        }

        int size = U16(acl, 2);
        int count = U16(acl, 4);
        if (size < 8 || size > acl.Length || size % 4 != 0 || count > (size - 8) / 4)
        {
            throw Invalid("Invalid ACL size or ACE count.");
        }

        acl = acl[..size];
        var aces = new List<byte[]>(count);
        int position = 8;
        for (int i = 0; i < count; i++)
        {
            if (size - position < 4)
            {
                throw Invalid("Truncated ACE header.");
            }

            int aceLength = U16(acl, position + 2);
            if (aceLength < 4 || aceLength % 4 != 0 || aceLength > size - position)
            {
                throw Invalid("Invalid ACE length.");
            }

            ReadOnlySpan<byte> ace = acl.Slice(position, aceLength);
            ValidateAce(ace, acl[0]);
            aces.Add(ace.ToArray());
            position = checked(position + aceLength);
        }

        RequireZero(acl[position..]);
        extents.Add((offset, size));
        return new Acl(acl.ToArray(), aces);
    }

    private static void ValidateAce(ReadOnlySpan<byte> ace, byte aclRevision)
    {
        byte type = ace[0];
        bool objectAce = type is 5 or 6 or 7 or 8 or 11 or 12 or 15 or 16;
        bool basicAce = type is 0 or 1 or 2 or 3 or 9 or 10 or 13 or 14 or 17 or 18 or 19 or 20 or 21;
        if (!objectAce && !basicAce)
        {
            return;
        }

        if ((ace[1] & 0x20) != 0 && type != 21)
        {
            throw Invalid("Reserved ACE flag is set.");
        }

        if ((type is 0 or 1) && (ace[1] & 0xc0) != 0)
        {
            throw Invalid("Access ACE has audit flags.");
        }

        int sidOffset = 8;
        if (objectAce)
        {
            if (aclRevision != 4 || ace.Length < 12)
            {
                throw Invalid("Object ACE requires revision-four ACL and object flags.");
            }

            uint objectFlags = U32(ace, 8);
            if ((objectFlags & ~3u) != 0)
            {
                throw Invalid("Reserved object ACE flags are set.");
            }

            sidOffset = checked(12 + ((objectFlags & 1) != 0 ? 16 : 0) +
                ((objectFlags & 2) != 0 ? 16 : 0));
        }

        if (sidOffset > ace.Length)
        {
            throw Invalid("Truncated ACE SID offset.");
        }

        int sidLength = SidLength(ace[sidOffset..]);
        bool applicationData = type is 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 18 or 21;
        if (!applicationData && checked(sidOffset + sidLength) != ace.Length)
        {
            throw Invalid("Unexpected trailing bytes in a fixed-layout ACE.");
        }
    }

    private static int Offset(ReadOnlySpan<byte> binary, int field)
    {
        uint offset = U32(binary, field);
        if (offset == 0)
        {
            return 0;
        }

        if (offset < 20 || offset >= binary.Length || offset % 4 != 0)
        {
            throw Invalid("Invalid or unaligned security descriptor offset.");
        }

        return checked((int)offset);
    }

    private static void WriteComponent(byte[] binary, int field, byte[]? component, ref int position)
    {
        if (component is null)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(field), checked((uint)position));
        component.CopyTo(binary, position);
        position = checked(position + component.Length);
    }

    private static void RequireZero(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyExcept((byte)0) >= 0)
        {
            throw Invalid("Nonzero reserved padding or unclaimed descriptor bytes.");
        }
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);

    private static uint U32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    private static InvalidDataException Invalid(string message) => new(message);

    private sealed record Acl(byte[] Binary, List<byte[]> Aces);
}
