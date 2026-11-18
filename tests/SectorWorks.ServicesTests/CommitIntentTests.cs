using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.ServicesTests.EditingTests;

namespace SectorWorks.ServicesTests;

public sealed class CommitIntentTests
{
    [Fact]
    public async Task InterruptedJournalDescribesOnlyTheActivePlanAndCapturedBytesWithoutImportHostPaths()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string payload = work.File("private-import-payload.bin", "captured before host change");
        var catalog = new CommitRecoveryCatalog(Path.Combine(work.Path, "catalog"));
        using var service = new ExplorerService(stagingDirectory: work.Path, recoveryCatalog: catalog);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/ADDED.TXT", payload));
        File.WriteAllText(payload, "a different host file now");
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/ADDED.TXT", NewName: "RENAMED.TXT"));
        await service.UndoAsync(info.Id);
        string original = Hash(source);
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id, new StopAtStaging()));
        string journal = Assert.Single(Directory.GetFiles(work.Path, "*.sectorworks-journal-*"));
        TransactionRecord record = Assert.IsType<TransactionRecord>(CommitJournal.Read(journal));
        CommitIntent intent = Assert.IsType<CommitIntent>(record.Intent);
        Assert.Equal(1, intent.OperationCount);
        CommitIntentOperation operation = Assert.Single(intent.Preview);
        Assert.Equal(ImageChangeKind.AddFile, operation.Kind);
        Assert.Equal("/ADDED.TXT", operation.ImagePath);
        Assert.Equal(Encoding.UTF8.GetByteCount("captured before host change"), operation.ContentLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData("captured before host change"u8)), operation.ContentSha256);
        Assert.Null(operation.NewName);
        Assert.Equal(64, intent.PlanSha256.Length);
        Assert.DoesNotContain(payload, File.ReadAllText(journal), StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(payload), File.ReadAllText(journal), StringComparison.Ordinal);
        Assert.DoesNotContain("RENAMED.TXT", File.ReadAllText(journal), StringComparison.Ordinal);
        Assert.Equal(original, Hash(source));
        Assert.Contains("Intended changes: 1", Assert.Single((await catalog.DiscoverAsync()).Items).Description);
    }

    [Fact]
    public async Task CompletePlanDigestChangesBeyondItsBoundedPreviewAndRemainsWithinJournalLimit()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        for (int i = 0; i < 17; i++)
        { await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/DIRECTORY " + i)); }
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id, new StopAtStaging()));
        TransactionRecord first = Assert.IsType<TransactionRecord>(CommitJournal.Read(Assert.Single(Directory.GetFiles(work.Path, "*.sectorworks-journal-*"))));
        Assert.Equal(17, first.Intent!.OperationCount);
        Assert.Equal(8, first.Intent.Preview.Length);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/BEYOND PREVIEW"));
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id, new StopAtStaging()));
        TransactionRecord second = Directory.GetFiles(work.Path, "*.sectorworks-journal-*").Select(CommitJournal.Read).OfType<TransactionRecord>()
            .Single(r => r.TransactionId != first.TransactionId);
        Assert.Equal(18, second.Intent!.OperationCount);
        Assert.Equal(first.Intent.Preview, second.Intent.Preview);
        Assert.NotEqual(first.Intent.PlanSha256, second.Intent.PlanSha256);
        Assert.All(Directory.GetFiles(work.Path, "*.sectorworks-journal-*"), p => Assert.True(new FileInfo(p).Length <= CommitJournal.MaximumJournalBytes));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("digest")]
    [InlineData("null-preview")]
    [InlineData("kind")]
    [InlineData("traversal")]
    [InlineData("host-path")]
    [InlineData("payload")]
    public void InvalidAuditIntentIsRejectedBeforeWritingAnyJournal(string change)
    {
        using var work = new EditDirectory();
        string source = work.File("source.img", "unchanged");
        string id = Guid.NewGuid().ToString("N");
        string journal = CommitJournal.PathFor(source, id);
        var operation = new CommitIntentOperation(ImageChangeKind.CreateDirectory, "volume-0", "/NEW", null, 0, null);
        var intent = new CommitIntent(1, new string('A', 64), [operation]);
        intent = change switch
        {
            "count" => intent with { OperationCount = 0 },
            "digest" => intent with { PlanSha256 = "bad" },
            "null-preview" => intent with { Preview = null! },
            "kind" => intent with { Preview = [operation with { Kind = (ImageChangeKind)99 }] },
            "traversal" => intent with { Preview = [operation with { ImagePath = "/../source" }] },
            "host-path" => intent with { Preview = [operation with { ImagePath = "C:\\private" }] },
            "payload" => intent with { Preview = [operation with { ContentSha256 = new string('B', 64) }] },
            _ => intent
        };
        var record = new TransactionRecord(id, source, CommitJournal.BackupPathFor(source, id),
            CommitJournal.StagingPathFor(source, id), CommitStage.BackupTaken, DateTime.UtcNow)
        { Intent = intent };
        Assert.Throws<InvalidDataException>(() => CommitJournal.Write(journal, record));
        Assert.False(File.Exists(journal));
        Assert.Equal("unchanged", File.ReadAllText(source));
    }

    [Fact]
    public void SerializationCannotWriteALargerJournalThanDiscoveryCanRead()
    {
        using var work = new EditDirectory();
        string source = Path.Combine(work.Path, new string('λ', 3000) + ".img");
        string id = Guid.NewGuid().ToString("N");
        var record = new TransactionRecord(id, source, CommitJournal.BackupPathFor(source, id),
            CommitJournal.StagingPathFor(source, id), CommitStage.Validated, DateTime.UtcNow);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => CommitJournal.Serialize(CommitJournal.PathFor(source, id), record));
        Assert.Contains("32 KiB", error.Message);
    }

    private sealed class StopAtStaging : IProgress<OperationProgress>
    {
        public void Report(OperationProgress progress)
        { if (progress.Stage == "Staging changes") { throw new IOException("Retain journal for audit inspection."); } }
    }

    private static string Hash(string path)
    { using FileStream file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
