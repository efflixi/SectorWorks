using SectorWorks.Platform;
using SectorWorks.Services;

namespace SectorWorks.ServicesTests;

public sealed class StagingContentTests
{
    [Theory]
    [InlineData(8589934591L, 1L, false)]
    [InlineData(8589934591L, 2L, true)]
    [InlineData(8589934592L, 0L, false)]
    [InlineData(8589934592L, 1L, true)]
    [InlineData(long.MaxValue, long.MaxValue, true)]
    public void RetainedStagingBudgetIncludesAllHistoryAndAvoidsOverflow(long retained, long incoming, bool rejected)
    {
        if (rejected)
        {
            Assert.Throws<IOException>(() => ExplorerService.ValidateStagingBudget(retained, incoming));
        }
        else
        {
            ExplorerService.ValidateStagingBudget(retained, incoming);
        }
    }
    [Theory]
    [InlineData(100, 101, 1000)]
    [InlineData(100, 100, 99)]
    public void InitialLengthMismatchOrLimitViolationCopiesNothing(long expected, long actual, long maximum)
    {
        using var work = new StagingDirectory();
        using var input = new ChangingSource(actual);
        Assert.Throws<IOException>(() => new StagedContent(input, work.Path, expected, maximum, default));
        Assert.Equal(0, input.BytesRead);
        Assert.Empty(Directory.GetFiles(work.Path));
    }

    [Fact]
    public void GrowthByANativeWriterCannotCopyBeyondInitialLength()
    {
        using var work = new StagingDirectory();
        const int expected = 131072;
        using var input = new ChangingSource(expected, afterFirstReadLength: 1_000_000_000);
        IOException failure = Assert.Throws<IOException>(() => new StagedContent(input, work.Path, expected, 2_000_000_000, default));
        Assert.Contains("length changed", failure.Message, StringComparison.Ordinal);
        Assert.Equal(expected, input.BytesRead);
        Assert.Empty(Directory.GetFiles(work.Path));
    }

    [Fact]
    public void PrematureEofDiscardsPartialSnapshot()
    {
        using var work = new StagingDirectory();
        using var input = new ChangingSource(131072, afterFirstReadLength: 32768);
        Assert.Throws<EndOfStreamException>(() => new StagedContent(input, work.Path, 131072, 1_000_000, default));
        Assert.Equal(65536, input.BytesRead);
        Assert.Empty(Directory.GetFiles(work.Path));
    }

    [Fact]
    public void CancellationDuringCaptureDiscardsPartialSnapshot()
    {
        using var work = new StagingDirectory();
        using var cancellation = new CancellationTokenSource();
        using var input = new ChangingSource(131072, afterRead: cancellation.Cancel);
        Assert.Throws<OperationCanceledException>(() => new StagedContent(input, work.Path, 131072, 1_000_000, cancellation.Token));
        Assert.InRange(input.BytesRead, 1, 65536);
        Assert.Empty(Directory.GetFiles(work.Path));
    }

    private sealed class ChangingSource(long length, long? afterFirstReadLength = null, Action? afterRead = null) : Stream
    {
        private long currentLength = length;
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => currentLength;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = checked((int)Math.Min(count, Math.Max(0, currentLength - Position)));
            buffer.AsSpan(offset, read).Clear();
            Position += read;
            BytesRead += read;
            if (afterFirstReadLength.HasValue) { currentLength = afterFirstReadLength.Value; }
            afterRead?.Invoke();
            return read;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StagingDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "staging-content-work", Guid.NewGuid().ToString("N"));
        public StagingDirectory() => WorkspaceDataFiles.CreatePrivateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
