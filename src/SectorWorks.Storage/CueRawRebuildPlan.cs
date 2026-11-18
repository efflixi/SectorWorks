using System.Security.Cryptography;
using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed class CueRawRebuildPlan(OpticalRebuildPlan cooked, ImageSetMember member, CdTrack track, int fileStart)
    : IImageRebuildPlan
{
    public long OutputLength => member.Length;
    public long AdditionalScratchBytes => cooked.OutputLength;
    public RebuildMemberHashes? MemberHashes { get; private set; }

    internal static void ValidateSourceTrack(FileBlockDevice member, CdTrack track, int fileStart, CancellationToken token)
    {
        using var input = new DeviceStream(new PartitionBlockDevice(member,
            (long)fileStart * CdRawSector.RawBytes, (long)track.SectorCount * CdRawSector.RawBytes));
        Span<byte> calculated = stackalloc byte[CdRawSector.RawBytes];
        byte[] frames = new byte[64 * CdRawSector.RawBytes];
        int address = -1;
        for (int i = 0; i < track.SectorCount;)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(64, track.SectorCount - i);
            input.ReadExactly(frames.AsSpan(0, count * CdRawSector.RawBytes));
            for (int frame = 0; frame < count; frame++, i++)
            {
                ReadOnlySpan<byte> sector = frames.AsSpan(frame * CdRawSector.RawBytes, CdRawSector.RawBytes);
                if (i == 0) { address = CdRawSector.Address(sector); }
                CdRawSector.Validate(sector, calculated, track.Mode, address);
                address++;
            }
        }
    }

    public void Write(Stream output, IProgress<OperationProgress>? progress, CancellationToken cancellationToken,
        string? scratchDirectory = null)
    {
        if (scratchDirectory is null) { throw new ArgumentException("Raw CUE rebuild requires a private output workspace."); }
        string scratchPath = Path.Combine(scratchDirectory, ".sectorworks-cooked-" + Guid.NewGuid().ToString("N"));
        using var scratch = new FileStream(scratchPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            65536, FileOptions.DeleteOnClose);
        cooked.Write(scratch, progress, cancellationToken);
        if (scratch.Length != (long)track.SectorCount * CdRawSector.UserBytes)
        { throw new InvalidDataException("Rebuilt ISO must keep the original raw track sector count."); }
        scratch.Position = 0;
        using Stream input = member.OpenRead();
        if (input.Length != member.Length) { throw new IOException("The raw CUE member length changed."); }
        using var originalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var outputHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * CdRawSector.RawBytes];

        CopyUnchanged((long)fileStart * CdRawSector.RawBytes);
        for (int frame = 0; frame < track.SectorCount;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(track.SectorCount - frame, 64);
            Span<byte> span = buffer.AsSpan(0, count * CdRawSector.RawBytes);
            input.ReadExactly(span);
            originalHash.AppendData(span);
            for (int i = 0; i < count; i++)
            {
                Span<byte> sector = span.Slice(i * CdRawSector.RawBytes, CdRawSector.RawBytes);
                scratch.ReadExactly(sector.Slice(CdRawSector.PayloadOffset(track.Mode), CdRawSector.UserBytes));
                CdRawSector.Protect(sector, track.Mode);
            }
            outputHash.AppendData(span);
            output.Write(span);
            frame += count;
            progress?.Report(new("Encoding raw CD data sectors", track.SourceName, (long)frame * CdRawSector.RawBytes, 0));
        }
        CopyUnchanged(member.Length - input.Position);
        if (input.ReadByte() != -1) { throw new IOException("The raw CUE member grew during rebuild."); }
        MemberHashes = new(Convert.ToHexString(originalHash.GetHashAndReset()), Convert.ToHexString(outputHash.GetHashAndReset()));

        void CopyUnchanged(long left)
        {
            while (left > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int take = (int)Math.Min(left, buffer.Length);
                input.ReadExactly(buffer.AsSpan(0, take));
                originalHash.AppendData(buffer, 0, take);
                outputHash.AppendData(buffer, 0, take);
                output.Write(buffer, 0, take);
                left -= take;
            }
        }
    }

    public void Validate(IReadOnlyFileSystem reopened, CancellationToken cancellationToken) => cooked.Validate(reopened, cancellationToken);
}
