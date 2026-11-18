using System.Buffers.Binary;
using SectorWorks.Core;

namespace SectorWorks.Services;

/// <summary>Streams a bounded Red Book program area to a playback backend, without extraction.</summary>
public static class CdAudioPlayback
{
    public const int SampleRate = 44100;
    public const int FramesPerSector = 588;
    public const int BytesPerFrame = 4;
    private const int ChunkBytes = 2352 * 3;

    public static void Run(Stream input, ICdAudioOutput output, long sectors, long startFrame, bool bigEndian,
        Func<double> volume, IProgress<long>? progress, CancellationToken token)
    {
        long frames = checked(sectors * FramesPerSector);
        if (!input.CanSeek || startFrame < 0 || startFrame > frames || input.Length != frames * BytesPerFrame)
        { throw new IOException("CD audio source length or seek position is invalid."); }
        input.Position = startFrame * BytesPerFrame;
        byte[] buffer = new byte[ChunkBytes];
        long cursor = startFrame;
        while (cursor < frames)
        {
            token.ThrowIfCancellationRequested();
            int count = checked((int)Math.Min(buffer.Length, (frames - cursor) * BytesPerFrame));
            input.ReadExactly(buffer.AsSpan(0, count));
            double gain = Math.Clamp(volume(), 0, 1);
            if (bigEndian || gain != 1)
            {
                for (int i = 0; i < count; i += 2)
                {
                    Span<byte> sample = buffer.AsSpan(i, 2);
                    short value = bigEndian ? BinaryPrimitives.ReadInt16BigEndian(sample) : BinaryPrimitives.ReadInt16LittleEndian(sample);
                    BinaryPrimitives.WriteInt16LittleEndian(sample, (short)Math.Round(value * gain));
                }
            }
            output.Write(buffer, count, token);
            cursor += count / BytesPerFrame;
            progress?.Report(startFrame + Math.Clamp(output.PlayedFrames, 0, cursor - startFrame));
        }
        output.Drain(token);
        progress?.Report(frames);
    }
}
