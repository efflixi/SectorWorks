using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.AccessControl;
using System.Security.Principal;
using DiscUtils.Ntfs;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable CA1416 // Measure the dependency's security API boundary, not product write capability.

namespace SectorWorks.DependencyTests;

public sealed class NtfsSecurityBoundaryTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyDictionary<short, OpCode> Instructions = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => field.GetValue(null))
        .OfType<OpCode>()
        .ToDictionary(code => code.Value);

    public enum SecurityApi
    {
        BinarySid,
        BinaryAcl,
        BinaryAce,
        BinaryDescriptor,
        SddlDescriptor
    }

    [Theory]
    [InlineData(SecurityApi.BinarySid)]
    [InlineData(SecurityApi.BinaryAcl)]
    [InlineData(SecurityApi.BinaryAce)]
    [InlineData(SecurityApi.BinaryDescriptor)]
    [InlineData(SecurityApi.SddlDescriptor)]
    public void ApprovedDependencySecurityPrimitivesNeedMoreThanASddlWorkaround(SecurityApi api)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(Construct(api));
            output.WriteLine($"{api}: constructor succeeded on Windows; NTFS writing is not qualified by this.");
        }
        else
        {
            PlatformNotSupportedException error = Assert.Throws<PlatformNotSupportedException>(() => Construct(api));
            output.WriteLine($"{api}: {error.GetType().Name}: {error.Message}");
        }
    }

    [Theory]
    [InlineData("CreateDirectory")]
    [InlineData("CreateNewFile")]
    public void ApprovedDependencyPassesNoncontainerEvenWhenCreatingADirectory(string methodName)
    {
        MethodInfo? method = typeof(NtfsFileSystem).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            [typeof(string), typeof(NewFileOptions)]);
        Assert.NotNull(method);
        OpCode argument = FindInheritanceArgument(method);
        Assert.Equal(OpCodes.Ldc_I4_0, argument);
        output.WriteLine($"{methodName}: installed {typeof(NtfsFileSystem).Assembly.GetName()} " +
            "passes literal false to CalcNewObjectDescriptor(parent, isContainer).");
    }

    private static object Construct(SecurityApi api) => api switch
    {
        SecurityApi.BinarySid => new SecurityIdentifier(Convert.FromHexString("010100000000000100000000"), 0),
        SecurityApi.BinaryAcl => new RawAcl(Convert.FromHexString("0200080000000000"), 0),
        SecurityApi.BinaryAce => GenericAce.CreateFromBinaryForm(
            Convert.FromHexString("0000140001000000010100000000000100000000"), 0),
        SecurityApi.BinaryDescriptor => new RawSecurityDescriptor(
            Convert.FromHexString("01000480000000000000000000000000140000000200080000000000"), 0),
        SecurityApi.SddlDescriptor => new RawSecurityDescriptor("O:SYG:SYD:(A;OICI;FA;;;SY)"),
        _ => throw new ArgumentOutOfRangeException(nameof(api))
    };

    private static OpCode FindInheritanceArgument(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException("The dependency method has no inspectable IL.");
        OpCode previous = default;
        for (int position = 0; position < il.Length;)
        {
            short value = il[position++];
            if (value == 0xfe)
            {
                if (position == il.Length) { throw new InvalidDataException("Truncated two-byte IL opcode."); }
                value = unchecked((short)(0xfe00 | il[position++]));
            }
            if (!Instructions.TryGetValue(value, out OpCode instruction))
            {
                throw new InvalidDataException("Unknown dependency IL opcode.");
            }
            int size = OperandSize(instruction.OperandType, il.AsSpan(position));
            if (size > il.Length - position) { throw new InvalidDataException("Truncated dependency IL operand."); }
            if (instruction == OpCodes.Call &&
                method.Module.ResolveMethod(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position, 4)))
                    is { Name: "CalcNewObjectDescriptor", DeclaringType.FullName: "DiscUtils.Ntfs.SecurityDescriptor" })
            {
                if (previous != OpCodes.Ldc_I4_0 && previous != OpCodes.Ldc_I4_1)
                {
                    throw new InvalidOperationException("Inheritance argument is not an immediately preceding Boolean literal.");
                }
                return previous;
            }
            position += size;
            previous = instruction;
        }
        throw new InvalidOperationException("The dependency method has no direct security inheritance call.");
    }

    private static int OperandSize(OperandType type, ReadOnlySpan<byte> remaining) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or
            OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
            OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch when remaining.Length >= 4 &&
            BinaryPrimitives.ReadInt32LittleEndian(remaining) >= 0
            => checked(4 + 4 * BinaryPrimitives.ReadInt32LittleEndian(remaining)),
        _ => throw new InvalidDataException("Unsupported or truncated dependency IL operand.")
    };
}
