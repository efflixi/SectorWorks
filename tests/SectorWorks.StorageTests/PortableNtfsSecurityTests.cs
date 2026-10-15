using System.Buffers.Binary;
using System.Security.AccessControl;
using SectorWorks.NtfsSecurity;

namespace SectorWorks.StorageTests;

public sealed class PortableNtfsSecurityTests
{
    private static readonly byte[] Owner = Convert.FromHexString("010100000000000512000000");
    private static readonly byte[] Group = Convert.FromHexString("01020000000000052000000020020000");
    private static readonly byte[] Everyone = Convert.FromHexString("010100000000000100000000");
    private static readonly byte[] CreatorOwner = Convert.FromHexString("010100000000000300000000");
    private static readonly byte[] CreatorGroup = Convert.FromHexString("010100000000000301000000");

    [Fact]
    public void IndependentlySpecifiedBinaryHasExactCanonicalSerialization()
    {
        byte[] binary = Convert.FromHexString(
            "01001480" + "4C000000" + "58000000" + "30000000" + "14000000" +
            "02001C0001000000" +
            "0003140089001200010100000000000100000000" +
            "02001C0001000000" +
            "02C3140001000000010100000000000100000000" +
            "010100000000000512000000" +
            "01020000000000052000000020020000");

        PortableSecurityDescriptor descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal(0x8014, descriptor.Control);
        Assert.Equal(Owner, descriptor.OwnerSid);
        Assert.Equal(Group, descriptor.GroupSid);
        Assert.Equal(binary, descriptor.ToBinary());
        descriptor.ValidateSupportedInheritance();
    }

    [Fact]
    public void NoncanonicalComponentPositionsAreReorderedWithoutChangingAclBytes()
    {
        byte[] ace = Ace(0, 3, 1, Everyone);
        byte[] acl = Acl(ace);
        byte[] noncanonical = new byte[20 + Owner.Length + Group.Length + acl.Length];
        noncanonical[0] = 1;
        Put16(noncanonical, 2, 0x8004);
        Put32(noncanonical, 4, 20);
        Put32(noncanonical, 8, (uint)(20 + Owner.Length));
        Put32(noncanonical, 16, (uint)(20 + Owner.Length + Group.Length));
        Owner.CopyTo(noncanonical, 20);
        Group.CopyTo(noncanonical, 20 + Owner.Length);
        acl.CopyTo(noncanonical, 20 + Owner.Length + Group.Length);

        Assert.Equal(Descriptor(acl), PortableSecurityDescriptor.Parse(noncanonical).ToBinary());
    }

    [Fact]
    public void EnclosingBufferPrefixIsExplicitlyBoundedAndReportsOriginalConsumedExtent()
    {
        byte[] descriptor = Descriptor(Acl(Ace(0, 3, 1, Everyone)));
        var enclosing = new byte[100000];
        enclosing.AsSpan().Fill(0xa5);
        descriptor.CopyTo(enclosing, 0);
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(enclosing));
        var parsed = PortableSecurityDescriptor.ParsePrefix(enclosing, out int consumed);
        Assert.Equal(descriptor.Length, consumed);
        Assert.Equal(descriptor, parsed.ToBinary());

        byte[] withGap = new byte[descriptor.Length + 4];
        descriptor.AsSpan(0, 20).CopyTo(withGap);
        descriptor.AsSpan(20).CopyTo(withGap.AsSpan(24));
        foreach (int field in new[] { 4, 8, 16 })
        {
            Put32(withGap, field, Get32(withGap, field) + 4);
        }

        parsed = PortableSecurityDescriptor.ParsePrefix(withGap, out consumed);
        Assert.Equal(withGap.Length, consumed);
        Assert.Equal(descriptor.Length, parsed.ToBinary().Length);
        Assert.Equal(descriptor, parsed.ToBinary());
    }

    [Fact]
    public void PrefixParserDoesNotAllowMalformedComponentsToEscapeTheDescriptorBudget()
    {
        byte[] descriptor = Descriptor(Acl(Ace(0, 3, 1, Everyone)));
        foreach (int field in new[] { 4, 8, 12, 16 })
        {
            foreach (uint offset in new uint[] { 19, 21, 65532, 65536, uint.MaxValue })
            {
                byte[] enclosing = new byte[100000];
                descriptor.CopyTo(enclosing, 0);
                Put32(enclosing, field, offset);
                Assert.Throws<InvalidDataException>(() =>
                    PortableSecurityDescriptor.ParsePrefix(enclosing, out _));
            }
        }

        foreach (int length in Enumerable.Range(0, descriptor.Length))
        {
            byte[] truncated = descriptor[..length];
            Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.ParsePrefix(truncated, out _));
        }
    }

    [Theory]
    [InlineData(0x8000, false, false, false, false)]
    [InlineData(0x8004, true, false, true, false)]
    [InlineData(0x8010, false, true, false, true)]
    [InlineData(0x8014, true, true, true, true)]
    public void AbsentAndNullAclStatesRemainDistinct(int control, bool hasDacl, bool hasSacl,
        bool nullDacl, bool nullSacl)
    {
        byte[] binary = Descriptor(null, null, (ushort)control);
        PortableSecurityDescriptor descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal(hasDacl, descriptor.HasDiscretionaryAcl);
        Assert.Equal(hasSacl, descriptor.HasSystemAcl);
        Assert.Equal(nullDacl, descriptor.IsDiscretionaryAclNull);
        Assert.Equal(nullSacl, descriptor.IsSystemAclNull);
        Assert.Equal(binary, descriptor.ToBinary());
        foreach (bool directory in new[] { false, true })
        {
            PortableSecurityDescriptor child = descriptor.Inherit(directory);
            Assert.Equal(hasDacl, child.HasDiscretionaryAcl);
            Assert.Equal(hasSacl, child.HasSystemAcl);
            Assert.Equal(nullDacl, child.IsDiscretionaryAclNull);
            Assert.Equal(nullSacl, child.IsSystemAclNull);
            Assert.Equal(0u, Get32(child.ToBinary(), 12));
            Assert.Equal(0u, Get32(child.ToBinary(), 16));
        }
    }

    [Fact]
    public void EmptyAclIsNeverReplacedByNullOrAbsentAcl()
    {
        byte[] binary = Descriptor(Acl(), Acl(), 0x8014);
        PortableSecurityDescriptor descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.False(descriptor.IsDiscretionaryAclNull);
        Assert.False(descriptor.IsSystemAclNull);
        Assert.Equal(binary, descriptor.ToBinary());
        byte[] child = descriptor.Inherit(true).ToBinary();
        Assert.Equal(20u, Get32(child, 16));
        Assert.Equal(28u, Get32(child, 12));
        Assert.Empty(Aces(child, 16));
        Assert.Empty(Aces(child, 12));
    }

    public static IEnumerable<object[]> InheritanceCases()
    {
        for (byte type = 0; type <= 2; type++)
        {
            for (byte flags = 0; flags < 32; flags++)
            {
                yield return [type, flags, false];
                yield return [type, flags, true];
            }
        }
    }

    // Expected rules come from Microsoft's ACE Inheritance Rules table, not
    // the production implementation or the dependency's inheritance routine.
    [Theory]
    [MemberData(nameof(InheritanceCases))]
    public void BasicAceFlagsFollowPublishedInheritanceTable(byte type, byte flags, bool directory)
    {
        byte audit = type == 2 ? (byte)0xc0 : (byte)0;
        byte[] acl = Acl(Ace(type, (byte)(flags | audit), 0x00020001, Everyone));
        PortableSecurityDescriptor parent = PortableSecurityDescriptor.Parse(
            type == 2 ? Descriptor(null, acl, 0x8010) : Descriptor(acl));
        if ((flags & 3) == 0 && (flags & 12) != 0)
        {
            Assert.Throws<NotSupportedException>(() => parent.Inherit(directory));
            return;
        }

        byte[] child = parent.Inherit(directory).ToBinary();
        List<byte[]> actual = Aces(child, type == 2 ? 12 : 16);
        int? expectedFlags = directory
            ? (flags & 7) switch
            {
                1 => 0x19,
                2 => 0x12,
                3 => 0x13,
                6 or 7 => 0x10,
                _ => null
            }
            : (flags & 1) != 0 ? 0x10 : null;
        if (expectedFlags is null)
        {
            Assert.Empty(actual);
        }
        else
        {
            Assert.Equal(Ace(type, (byte)(expectedFlags.Value | audit), 0x00020001, Everyone),
                Assert.Single(actual));
        }
    }

    [Theory]
    [InlineData(0x00000000u, 0x00000000u)]
    [InlineData(0x80000000u, 0x00120089u)]
    [InlineData(0x40000000u, 0x00120116u)]
    [InlineData(0x20000000u, 0x001200a0u)]
    [InlineData(0x10000000u, 0x001f01ffu)]
    [InlineData(0xc0000000u, 0x0012019fu)]
    [InlineData(0xa0000000u, 0x001200a9u)]
    [InlineData(0x60000000u, 0x001201b6u)]
    [InlineData(0xe0000000u, 0x001201bfu)]
    [InlineData(0x30000000u, 0x001f01ffu)]
    [InlineData(0x50000000u, 0x001f01ffu)]
    [InlineData(0x70000000u, 0x001f01ffu)]
    [InlineData(0x90000000u, 0x001f01ffu)]
    [InlineData(0xb0000000u, 0x001f01ffu)]
    [InlineData(0xd0000000u, 0x001f01ffu)]
    [InlineData(0xf0000000u, 0x001f01ffu)]
    [InlineData(0x81004000u, 0x01124089u)]
    public void GenericMasksUseFileMappingsAndPreserveSpecificBits(uint mask, uint expected)
    {
        Assert.Equal(expected, PortableSecurityDescriptor.MapFileGenericMask(mask));
        var parent = PortableSecurityDescriptor.Parse(Descriptor(Acl(Ace(0, 3, mask, Everyone))));
        byte[] fileAce = Assert.Single(Aces(parent.Inherit(false).ToBinary(), 16));
        Assert.Equal(expected, Get32(fileAce, 4));
    }

    [Fact]
    public void GenericEffectiveDirectoryAceAndUnmappedTemplateAreSeparate()
    {
        var parent = PortableSecurityDescriptor.Parse(Descriptor(Acl(Ace(0, 3, 0x80000000, Everyone))));
        byte[] child = parent.Inherit(true).ToBinary();
        List<byte[]> aces = Aces(child, 16);
        Assert.Equal(2, aces.Count);
        Assert.Equal(Ace(0, 0x10, 0x120089, Everyone), aces[0]);
        Assert.Equal(Ace(0, 0x1b, 0x80000000, Everyone), aces[1]);
        List<byte[]> grandchild = Aces(PortableSecurityDescriptor.Parse(child).Inherit(false).ToBinary(), 16);
        Assert.Equal(Ace(0, 0x10, 0x120089, Everyone), Assert.Single(grandchild));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatorOwnerAndGroupUseImageParentIdentityNotHostIdentity(bool creatorIsGroup)
    {
        byte[] creator = creatorIsGroup ? CreatorGroup : CreatorOwner;
        byte[] imageIdentity = creatorIsGroup ? Group : Owner;
        var parent = PortableSecurityDescriptor.Parse(Descriptor(Acl(Ace(0, 3, 0x10000000, creator))));
        PortableSecurityDescriptor child = parent.Inherit(true);
        Assert.Equal(Owner, child.OwnerSid);
        Assert.Equal(Group, child.GroupSid);
        List<byte[]> directoryAces = Aces(child.ToBinary(), 16);
        Assert.Equal(2, directoryAces.Count);
        Assert.Equal(Ace(0, 0x10, 0x1f01ff, imageIdentity), directoryAces[0]);
        Assert.Equal(Ace(0, 0x1b, 0x10000000, creator), directoryAces[1]);
        Assert.Equal(Ace(0, 0x10, 0x1f01ff, imageIdentity),
            Assert.Single(Aces(child.Inherit(false).ToBinary(), 16)));
    }

    [Fact]
    public void ObjectOnlyCreatorTemplateRemainsGenericUntilFileIsCreated()
    {
        var parent = PortableSecurityDescriptor.Parse(Descriptor(Acl(Ace(0, 1, 0x80000000, CreatorOwner))));
        PortableSecurityDescriptor directory = parent.Inherit(true);
        Assert.Equal(Ace(0, 0x19, 0x80000000, CreatorOwner),
            Assert.Single(Aces(directory.ToBinary(), 16)));
        Assert.Equal(Ace(0, 0x10, 0x120089, Owner),
            Assert.Single(Aces(directory.Inherit(false).ToBinary(), 16)));
    }

    [Theory]
    [InlineData(1, 0x19)]
    [InlineData(2, 0x1a)]
    [InlineData(3, 0x1b)]
    public void GenericPropagationTemplatesRetainExactlyTheirOriginalInheritanceTargets(int flags, int templateFlags)
    {
        var parent = PortableSecurityDescriptor.Parse(
            Descriptor(Acl(Ace(0, (byte)flags, 0x80000000, CreatorGroup))));
        PortableSecurityDescriptor directory = parent.Inherit(true);
        List<byte[]> aces = Aces(directory.ToBinary(), 16);
        Assert.Equal(Ace(0, (byte)templateFlags, 0x80000000, CreatorGroup), aces[^1]);
        Assert.Equal(flags == 1 ? 1 : 2, aces.Count);
        if (flags == 2)
        {
            Assert.Empty(Aces(directory.Inherit(false).ToBinary(), 16));
        }
    }

    [Fact]
    public void NoInheritableAcesProducesAnEmptyAclNotAPermissiveNullAcl()
    {
        var descriptor = PortableSecurityDescriptor.Parse(Descriptor(Acl(Ace(0, 0, 0x1f01ff, Everyone))));
        PortableSecurityDescriptor child = descriptor.Inherit(false);
        Assert.True(child.HasDiscretionaryAcl);
        Assert.False(child.IsDiscretionaryAclNull);
        Assert.Empty(Aces(child.ToBinary(), 16));
    }

    [Fact]
    public void NoPropagateCreatorAceHasOnlyMappedEffectiveCopyAndStops()
    {
        var parent = PortableSecurityDescriptor.Parse(Descriptor(Acl(Ace(0, 7, 0x40000000, CreatorGroup))));
        PortableSecurityDescriptor directory = parent.Inherit(true);
        Assert.Equal(Ace(0, 0x10, 0x120116, Group),
            Assert.Single(Aces(directory.ToBinary(), 16)));
        Assert.Empty(Aces(directory.Inherit(true).ToBinary(), 16));
        Assert.Empty(Aces(directory.Inherit(false).ToBinary(), 16));
    }

    [Fact]
    public void AuditInheritanceRetainsSuccessFailureFlagsAndSubstitutesGenericSids()
    {
        var parent = PortableSecurityDescriptor.Parse(
            Descriptor(null, Acl(Ace(2, 0xc3, 0x20000000, CreatorOwner)), 0x8010));
        List<byte[]> inherited = Aces(parent.Inherit(true).ToBinary(), 12);
        Assert.Equal(2, inherited.Count);
        Assert.Equal(Ace(2, 0xd0, 0x1200a0, Owner), inherited[0]);
        Assert.Equal(Ace(2, 0xdb, 0x20000000, CreatorOwner), inherited[1]);
    }

    [Fact]
    public void InheritedDaclPreservesAncestorPrecedenceInsteadOfMovingAllDeniesFirst()
    {
        byte[] nearDeny = Ace(1, 3, 1, Owner);
        byte[] nearAllow = Ace(0, 3, 2, Owner);
        byte[] farDeny = Ace(1, 0x13, 4, Group);
        byte[] farAllow = Ace(0, 0x13, 8, Group);
        var parent = PortableSecurityDescriptor.Parse(Descriptor(Acl(nearDeny, nearAllow, farDeny, farAllow)));
        List<byte[]> inherited = Aces(parent.Inherit(true).ToBinary(), 16);
        Assert.Equal(new byte[] { 1, 0, 1, 0 }, inherited.Select(a => a[0]).ToArray());
        Assert.Equal(new uint[] { 1, 2, 4, 8 }, inherited.Select(a => Get32(a, 4)).ToArray());
        Assert.All(inherited, a => Assert.Equal(0x13, a[1]));
    }

    [Fact]
    public void NoncanonicalExplicitDaclCanRoundTripButCannotSilentlyChangeInheritanceMeaning()
    {
        byte[] binary = Descriptor(Acl(Ace(0, 3, 1, Everyone), Ace(1, 3, 1, Everyone)));
        var parent = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal(binary, parent.ToBinary());
        Assert.Throws<NotSupportedException>(parent.ValidateSupportedInheritance);
        Assert.Throws<NotSupportedException>(() => parent.Inherit(false));
    }

    [Fact]
    public void ExplicitAcesCannotFollowInheritedAces()
    {
        var parent = PortableSecurityDescriptor.Parse(
            Descriptor(Acl(Ace(1, 0x13, 1, Everyone), Ace(0, 3, 2, Everyone))));
        Assert.Throws<NotSupportedException>(parent.ValidateSupportedInheritance);
    }

    [Fact]
    public void ControlAndResourceManagerFlagsRoundTripButAreNotCopiedAsChildProtection()
    {
        byte[] binary = Descriptor(Acl(Ace(0, 3, 1, Everyone)),
            Acl(Ace(2, 0xc3, 1, Everyone)), 0xff3f);
        binary[1] = 0xa5;
        var descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal(0xff3f, descriptor.Control);
        Assert.Equal(0xa5, descriptor.ResourceManagerControl);
        Assert.Equal(binary, descriptor.ToBinary());
        PortableSecurityDescriptor child = descriptor.Inherit(true);
        Assert.Equal(0x8c14, child.Control);
        Assert.Equal(0, child.ResourceManagerControl);
        Assert.Equal(Owner, child.OwnerSid);
        Assert.Equal(Group, child.GroupSid);
    }

    [Theory]
    [InlineData(0x8044)]
    [InlineData(0x8084)]
    public void TokenDependentControlSemanticsArePreservedButNotInherited(int control)
    {
        byte[] binary = Descriptor(Acl(), null, (ushort)control);
        var descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal(binary, descriptor.ToBinary());
        Assert.Throws<NotSupportedException>(descriptor.ValidateSupportedInheritance);
    }

    [Fact]
    public void SidAuthorityAndFifteenSubauthoritiesRemainByteExactAndDefensivelyOwned()
    {
        byte[] sid = new byte[68];
        sid[0] = 1;
        sid[1] = 15;
        sid.AsSpan(2, 6).Fill(0xff);
        for (int i = 0; i < 15; i++)
        {
            Put32(sid, 8 + 4 * i, uint.MaxValue - (uint)i);
        }

        byte[] binary = Descriptor(Acl(Ace(0, 3, 1, sid)), owner: sid, group: sid);
        byte[] expected = binary.ToArray();
        var descriptor = PortableSecurityDescriptor.Parse(binary);
        binary.AsSpan().Clear();
        Assert.Equal(expected, descriptor.ToBinary());
        Assert.Equal(sid, descriptor.OwnerSid);
        descriptor.OwnerSid!.AsSpan().Clear();
        Assert.Equal(sid, descriptor.OwnerSid);
        Assert.Equal(sid, descriptor.GroupSid);
        Assert.Equal(Ace(0, 0x10, 1, sid),
            Assert.Single(Aces(descriptor.Inherit(false).ToBinary(), 16)));
    }

    [Fact]
    public void MissingImageCreatorIdentityExplicitlyRejectsInheritance()
    {
        foreach (byte[] creator in new[] { CreatorOwner, CreatorGroup })
        {
            byte[] binary = Descriptor(Acl(Ace(0, 3, 1, creator)), owner: [], group: []);
            var descriptor = PortableSecurityDescriptor.Parse(binary);
            Assert.Null(descriptor.OwnerSid);
            Assert.Null(descriptor.GroupSid);
            Assert.Throws<NotSupportedException>(descriptor.ValidateSupportedInheritance);
        }
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    public void UnsupportedCreatorServerOrOwnerRightsSemanticsCannotBecomeOrdinaryAccess(uint kind)
    {
        byte[] sid = CreatorOwner.ToArray();
        Put32(sid, 8, kind);
        var descriptor = PortableSecurityDescriptor.Parse(Descriptor(Acl(Ace(0, 3, 1, sid))));
        Assert.Throws<NotSupportedException>(descriptor.ValidateSupportedInheritance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(17)]
    public void WrongAclAceKindIsNeverReinterpreted(int kind)
    {
        byte[] acl = Acl(Ace((byte)kind, 3, 1, Everyone));
        var descriptor = PortableSecurityDescriptor.Parse(Descriptor(null, acl, 0x8010));
        Assert.Throws<NotSupportedException>(descriptor.ValidateSupportedInheritance);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(21)]
    [InlineData(127)]
    public void OpaqueConditionalAndObjectAcesRoundTripButExplicitlyRejectInheritance(int kind)
    {
        bool objectAce = kind is 5 or 6 or 7 or 8 or 11 or 12 or 15 or 16;
        byte[] ace;
        if (objectAce)
        {
            ace = new byte[12 + Everyone.Length];
            ace[0] = (byte)kind;
            ace[1] = 3;
            Put16(ace, 2, (ushort)ace.Length);
            Put32(ace, 4, 1);
            Everyone.CopyTo(ace, 12);
        }
        else
        {
            ace = Ace((byte)kind, 3, 1, Everyone);
        }

        byte[] acl = Acl(ace);
        acl[0] = 4;
        byte[] binary = Descriptor(acl);
        var descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal(binary, descriptor.ToBinary());
        Assert.Throws<NotSupportedException>(descriptor.ValidateSupportedInheritance);
        Assert.Throws<NotSupportedException>(() => descriptor.Inherit(true));
    }

    [Fact]
    public void ConditionalPayloadIsPreservedRatherThanTruncatedToItsSid()
    {
        byte[] callback = Ace(9, 3, 1, Everyone).Concat(Convert.FromHexString("6172747801020304")).ToArray();
        Put16(callback, 2, (ushort)callback.Length);
        byte[] binary = Descriptor(Acl(callback));
        var descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal(binary, descriptor.ToBinary());
        Assert.Throws<NotSupportedException>(descriptor.ValidateSupportedInheritance);
    }

    [Fact]
    public void ZeroAclSlackAndDescriptorPaddingHaveNoHiddenInterpretation()
    {
        byte[] acl = Acl(Ace(0, 3, 1, Everyone)).Concat(new byte[8]).ToArray();
        Put16(acl, 2, (ushort)acl.Length);
        byte[] binary = Descriptor(acl);
        Assert.Equal(binary, PortableSecurityDescriptor.Parse(binary).ToBinary());
        byte[] padded = binary.Concat(new byte[12]).ToArray();
        Assert.Equal(binary, PortableSecurityDescriptor.Parse(padded).ToBinary());
        padded[^1] = 1;
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(padded));
        acl[^1] = 1;
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(Descriptor(acl)));
    }

    [Fact]
    public void EveryTruncationOfACompleteDescriptorIsRejected()
    {
        byte[] binary = Descriptor(Acl(Ace(0, 3, 1, Everyone)),
            Acl(Ace(2, 0xc3, 1, Everyone)), 0x8014);
        for (int length = 0; length < binary.Length; length++)
        {
            byte[] truncated = binary[..length];
            Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(truncated));
        }
    }

    [Fact]
    public void MalformedHeadersLengthsCountsReservedFieldsAndSidLayoutsAreRejected()
    {
        byte[] valid = Descriptor(Acl(Ace(0, 3, 1, Everyone)));
        var mutations = new List<Action<byte[]>>
        {
            b => b[0] = 0,
            b => b[0] = 2,
            b => b[1] = 1,
            b => Put16(b, 2, 4),
            b => Put16(b, 2, 0x8000),
            b => b[20] = 1,
            b => b[20] = 3,
            b => b[21] = 1,
            b => Put16(b, 22, 0),
            b => Put16(b, 22, 7),
            b => Put16(b, 22, 9),
            b => Put16(b, 22, ushort.MaxValue),
            b => Put16(b, 24, ushort.MaxValue),
            b => Put16(b, 24, 0),
            b => Put16(b, 24, 2),
            b => Put16(b, 26, 1),
            b => Put16(b, 30, 0),
            b => Put16(b, 30, 3),
            b => Put16(b, 30, 7),
            b => Put16(b, 30, 16),
            b => Put16(b, 30, 24),
            b => Put16(b, 30, ushort.MaxValue),
            b => b[29] |= 0x20,
            b => b[29] |= 0x40,
            b => b[36] = 2,
            b => b[37] = 16,
            b => b[48] = 0,
            b => b[49] = 16
        };
        foreach (Action<byte[]> mutate in mutations)
        {
            byte[] malformed = valid.ToArray();
            mutate(malformed);
            Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(malformed));
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void InvalidAndUnalignedComponentOffsetsAreRejected(int field)
    {
        byte[] valid = Descriptor(Acl(Ace(0, 3, 1, Everyone)),
            Acl(Ace(2, 0xc3, 1, Everyone)), 0x8014);
        foreach (uint offset in new uint[] { 1, 4, 16, 19, 21, 23, 0x7ffffffc, 0x80000000, 0xfffffffc,
            uint.MaxValue, (uint)valid.Length, (uint)valid.Length + 4 })
        {
            byte[] malformed = valid.ToArray();
            Put32(malformed, field, offset);
            Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(malformed));
        }
    }

    [Fact]
    public void OverlappingComponentsIncludingAliasedSidOffsetsAreRejected()
    {
        byte[] valid = Descriptor(Acl(Ace(0, 3, 1, Everyone)),
            Acl(Ace(2, 0xc3, 1, Everyone)), 0x8014);
        foreach ((int target, int source) in new[] { (4, 8), (8, 4), (12, 16), (16, 12) })
        {
            byte[] malformed = valid.ToArray();
            Put32(malformed, target, Get32(malformed, source));
            Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(malformed));
        }

        byte[] nestedSid = valid.ToArray();
        Put32(nestedSid, 4, 36);
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(nestedSid));
    }

    [Fact]
    public void ObjectAceGuidFlagsRevisionAndTruncationAreChecked()
    {
        byte[] objectAce = new byte[12 + 32 + Everyone.Length];
        objectAce[0] = 5;
        Put16(objectAce, 2, (ushort)objectAce.Length);
        Put32(objectAce, 8, 3);
        Everyone.CopyTo(objectAce, 44);
        byte[] acl = Acl(objectAce);
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(Descriptor(acl)));
        acl[0] = 4;
        _ = PortableSecurityDescriptor.Parse(Descriptor(acl));
        byte[] invalidFlags = acl.ToArray();
        Put32(invalidFlags, 16, 4);
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(Descriptor(invalidFlags)));
        byte[] truncatedSid = acl[..^4];
        Put16(truncatedSid, 2, (ushort)truncatedSid.Length);
        Put16(truncatedSid, 10, (ushort)(objectAce.Length - 4));
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(Descriptor(truncatedSid)));
    }

    [Fact]
    public void DescriptorAndInheritedExpansionBudgetsAreEnforced()
    {
        byte[] oversized = new byte[PortableSecurityDescriptor.MaximumBinaryLength + 1];
        oversized[0] = 1;
        Put16(oversized, 2, 0x8000);
        Assert.Throws<InvalidDataException>(() => PortableSecurityDescriptor.Parse(oversized));

        byte[] maximum = new byte[PortableSecurityDescriptor.MaximumBinaryLength];
        maximum[0] = 1;
        Put16(maximum, 2, 0x8000);
        _ = PortableSecurityDescriptor.Parse(maximum);

        byte[][] expandingAces = Enumerable.Range(0, 1700)
            .Select(_ => Ace(0, 3, 0x80000000, Everyone)).ToArray();
        var parent = PortableSecurityDescriptor.Parse(Descriptor(Acl(expandingAces)));
        Assert.Throws<InvalidDataException>(() => parent.Inherit(true));
        _ = parent.Inherit(false).ToBinary();
    }

    [Fact]
    public void DeterministicHostileByteMutationNeverLeaksPartialInterpretationOrBoundsExceptions()
    {
        var random = new Random(0x4e544653);
        byte[] seed = Descriptor(Acl(Ace(0, 3, 0x80000000, Everyone)),
            Acl(Ace(2, 0xc3, 1, Everyone)), 0x8014);
        for (int iteration = 0; iteration < 6000; iteration++)
        {
            byte[] input = iteration % 2 == 0 ? seed.ToArray() : new byte[random.Next(257)];
            if (iteration % 2 != 0)
            {
                random.NextBytes(input);
            }
            else
            {
                for (int change = 0; change <= iteration % 7; change++)
                {
                    input[random.Next(input.Length)] = (byte)random.Next(256);
                }
            }

            try
            {
                var descriptor = PortableSecurityDescriptor.Parse(input);
                byte[] canonical = descriptor.ToBinary();
                Assert.Equal(canonical, PortableSecurityDescriptor.Parse(canonical).ToBinary());
                try
                {
                    descriptor.ValidateSupportedInheritance();
                    foreach (bool directory in new[] { false, true })
                    {
                        byte[] child = descriptor.Inherit(directory).ToBinary();
                        Assert.Equal(child, PortableSecurityDescriptor.Parse(child).ToBinary());
                    }
                }
                catch (NotSupportedException)
                {
                }
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [WindowsReferenceFact]
    public void WindowsBclReferenceParsesMatchingSidAclFlagsAndOpaqueBinarySemantics()
    {
        // This is a Windows-only BCL codec comparison, not physical NTFS workflow
        // evidence. Non-Windows runs report a skip rather than a false success.
#pragma warning disable CA1416
        byte[] binary = Descriptor(Acl(Ace(1, 3, 2, Everyone), Ace(0, 3, 0x80000000, CreatorOwner)),
            Acl(Ace(2, 0xc3, 1, Everyone)), 0x8014);
        var reference = new RawSecurityDescriptor(binary, 0);
        var descriptor = PortableSecurityDescriptor.Parse(binary);
        Assert.Equal((ushort)reference.ControlFlags, descriptor.Control);
        byte[] owner = new byte[reference.Owner!.BinaryLength];
        reference.Owner.GetBinaryForm(owner, 0);
        Assert.Equal(owner, descriptor.OwnerSid);
        byte[] group = new byte[reference.Group!.BinaryLength];
        reference.Group.GetBinaryForm(group, 0);
        Assert.Equal(group, descriptor.GroupSid);
        Assert.Equal(reference.DiscretionaryAcl!.Count, Aces(descriptor.ToBinary(), 16).Count);
        Assert.Equal(reference.SystemAcl!.Count, Aces(descriptor.ToBinary(), 12).Count);
        byte[] bclBinary = new byte[reference.BinaryLength];
        reference.GetBinaryForm(bclBinary, 0);
        Assert.Equal(binary, PortableSecurityDescriptor.Parse(bclBinary).ToBinary());
#pragma warning restore CA1416
    }

    private static byte[] Descriptor(byte[]? dacl, byte[]? sacl = null, ushort control = 0x8004,
        byte[]? owner = null, byte[]? group = null)
    {
        owner ??= Owner;
        group ??= Group;
        var binary = new byte[20 + (dacl?.Length ?? 0) + (sacl?.Length ?? 0) + owner.Length + group.Length];
        binary[0] = 1;
        Put16(binary, 2, control);
        int position = 20;
        foreach ((int field, byte[]? component) in new[] { (16, dacl), (12, sacl), (4, owner), (8, group) })
        {
            if (component is null || component.Length == 0)
            {
                continue;
            }

            Put32(binary, field, (uint)position);
            component.CopyTo(binary, position);
            position += component.Length;
        }

        return binary;
    }

    private static byte[] Acl(params byte[][] aces)
    {
        var binary = new byte[8 + aces.Sum(a => a.Length)];
        binary[0] = 2;
        Put16(binary, 2, checked((ushort)binary.Length));
        Put16(binary, 4, checked((ushort)aces.Length));
        int position = 8;
        foreach (byte[] ace in aces)
        {
            ace.CopyTo(binary, position);
            position += ace.Length;
        }

        return binary;
    }

    private static byte[] Ace(byte type, byte flags, uint mask, byte[] sid)
    {
        var binary = new byte[8 + sid.Length];
        binary[0] = type;
        binary[1] = flags;
        Put16(binary, 2, checked((ushort)binary.Length));
        Put32(binary, 4, mask);
        sid.CopyTo(binary, 8);
        return binary;
    }

    private static List<byte[]> Aces(byte[] descriptor, int aclField)
    {
        int offset = checked((int)Get32(descriptor, aclField));
        Assert.NotEqual(0, offset);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(offset + 4));
        int position = offset + 8;
        var result = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(position + 2));
            result.Add(descriptor.AsSpan(position, size).ToArray());
            position += size;
        }

        return result;
    }

    private static uint Get32(byte[] binary, int position) =>
        BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(position));

    private static void Put16(byte[] binary, int position, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(position), value);

    private static void Put32(byte[] binary, int position, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(binary.AsSpan(position), value);

    public sealed class WindowsReferenceFactAttribute : FactAttribute
    {
        public WindowsReferenceFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "Windows BCL reference requires an actual Windows runtime; no Windows physical evidence on this host.";
            }
        }
    }
}
