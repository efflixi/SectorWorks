namespace SectorWorks.DependencyTests;

internal sealed class MeasuredReadStream(Stream source) : Stream
{
    public long TotalRead { get; private set; }
    public int LargestRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        LargestRead = Math.Max(LargestRead, count);
        int read = source.Read(buffer, offset, count);
        TotalRead = checked(TotalRead + read);
        return read;
    }
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
