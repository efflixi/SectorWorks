using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.ServicesTests.EditingTests;

namespace SectorWorks.ServicesTests;

public sealed class CommitRecoveryCatalogTests
{
    [Theory]
    [InlineData("Registered recovery", false)]
    [InlineData("Staging changes", false)]
    [InlineData("Finalizing commit", false)]
    [InlineData("Source replaced", true)]
    public async Task ActualProcessKillRetainsDiscoverableArtifactsAndNeverRollsBackReplacement(string stage, bool replaced)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat16.img");
        string original = Hash(source);
        string catalogPath = Path.Combine(work.Path, "catalog");
        string marker = Path.Combine(work.Path, "interruption-ready");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[]
        {
            "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "SectorWorks.ServicesTests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "SectorWorks.ServicesTests.deps.json"),
            typeof(SectorWorks.RuntimeProbe.Program).Assembly.Location,
            "commit-pause", source, catalogPath, marker, stage
        }) { start.ArgumentList.Add(argument); }
        using Process child = Process.Start(start) ?? throw new IOException("Interruption child could not start.");
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> error = child.StandardError.ReadToEndAsync();
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(25);
            while (!File.Exists(marker) && !child.HasExited && DateTime.UtcNow < deadline) { await Task.Delay(20); }
            Assert.True(File.Exists(marker), child.HasExited ? await output + await error : "Interruption barrier not reached.");
            Assert.Equal(stage, File.ReadAllText(marker));
            child.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, child.ExitCode);
            var restarted = new CommitRecoveryCatalog(catalogPath);
            RecoveryScan scan = await restarted.DiscoverAsync();
            Assert.Empty(scan.Warnings);
            RecoveryItem item = Assert.Single(scan.Items);
            string afterKill = Hash(source);
            if (replaced)
            {
                Assert.NotEqual(original, afterKill);
                await Assert.ThrowsAsync<IOException>(() => restarted.CleanAsync(item));
                Assert.Equal(afterKill, Hash(source));
                Assert.True(File.Exists(item.JournalPath));
            }
            else
            {
                Assert.Equal(original, afterKill);
                await restarted.CleanAsync(item);
                Assert.Equal(original, Hash(source));
                Assert.Empty((await restarted.DiscoverAsync()).Items);
            }
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); }
        }
    }

    [Fact]
    public async Task NewProcessCatalogFindsInterruptedCommitAndCleansOnlyUnchangedSourceArtifacts()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat16.img");
        string original = Hash(source);
        string catalogPath = Path.Combine(work.Path, "catalog");
        var catalog = new CommitRecoveryCatalog(catalogPath);
        using (var service = new ExplorerService(stagingDirectory: work.Path, recoveryCatalog: catalog))
        {
            WorkspaceInfo info = await service.OpenAsync(source);
            await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/PENDING"));
            await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(info.Id, new InlineProgress(p =>
            {
                if (p.Stage == "Staging changes") { throw new IOException("Controlled interruption after backup/journal."); }
            })));
        }
        var restarted = new CommitRecoveryCatalog(catalogPath);
        RecoveryScan scan = await restarted.DiscoverAsync();
        Assert.Empty(scan.Warnings);
        RecoveryItem item = Assert.Single(scan.Items);
        Assert.True(item.CanClean);
        Assert.Equal("BackupTaken", item.Stage);
        Assert.Equal(original, Hash(source));
        await restarted.CleanAsync(item);
        Assert.Equal(original, Hash(source));
        Assert.Empty((await restarted.DiscoverAsync()).Items);
        Assert.Empty(Directory.GetFiles(work.Path, "*.sectorworks-*"));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("backup")]
    [InlineData("journal")]
    public async Task ChangedInputsRefuseCleanupWithoutOverwritingSourceOrDeletingEvidence(string changed)
    {
        using var work = new EditDirectory();
        (CommitRecoveryCatalog catalog, TransactionRecord record, string journal) = Prepare(work);
        RecoveryItem item = Assert.Single((await catalog.DiscoverAsync()).Items);
        File.AppendAllText(changed switch { "source" => record.SourcePath, "backup" => record.BackupPath, _ => journal }, "changed");
        string sourceHash = Hash(record.SourcePath);
        await Assert.ThrowsAsync<IOException>(() => catalog.CleanAsync(item));
        Assert.Equal(sourceHash, Hash(record.SourcePath));
        Assert.True(File.Exists(record.BackupPath));
        Assert.True(File.Exists(record.StagingPath));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public async Task FinalizedCleanupNeverRestoresAnOldBackup()
    {
        using var work = new EditDirectory();
        var (catalog, record, journal) = Prepare(work, CommitStage.Finalized);
        File.WriteAllText(record.SourcePath, "new committed source");
        string committed = Hash(record.SourcePath);
        await catalog.CleanAsync(Assert.Single((await catalog.DiscoverAsync()).Items));
        Assert.Equal(committed, Hash(record.SourcePath));
        Assert.False(File.Exists(record.BackupPath));
        Assert.False(File.Exists(journal));
    }

    [Theory]
    [InlineData("foreign-backup")]
    [InlineData("foreign-staging")]
    [InlineData("relative-source")]
    [InlineData("invalid-stage")]
    [InlineData("oversized")]
    [InlineData("null")]
    public async Task UntrustedJournalsRemainVisibleAsWarningsAndCannotRedirectCleanup(string malformed)
    {
        using var work = new EditDirectory();
        var (catalog, record, journal) = Prepare(work);
        string valuable = work.File("valuable-owner-data", "preserve");
        TransactionRecord bad = malformed switch
        {
            "foreign-backup" => record with { BackupPath = valuable },
            "foreign-staging" => record with { StagingPath = valuable },
            "relative-source" => record with { SourcePath = "../source" },
            "invalid-stage" => record with { Stage = (CommitStage)99 },
            _ => record
        };
        File.WriteAllText(journal, malformed == "oversized" ? new string(' ', 32769)
            : malformed == "null" ? "null" : JsonSerializer.Serialize(bad));
        RecoveryScan scan = await catalog.DiscoverAsync();
        Assert.Empty(scan.Items);
        Assert.Single(scan.Warnings);
        Assert.Equal("preserve", File.ReadAllText(valuable));
        Assert.True(File.Exists(record.BackupPath));
    }

    [Fact]
    public async Task NonregularArtifactFailsBeforeAnyCleanupAndSourceLockContentionRetainsEvidence()
    {
        using var work = new EditDirectory();
        var (catalog, record, journal) = Prepare(work);
        RecoveryItem item = Assert.Single((await catalog.DiscoverAsync()).Items);
        using (SourceWriteLock held = SourceWriteLock.Acquire(record.SourcePath))
        {
            await Assert.ThrowsAsync<IOException>(() => catalog.CleanAsync(item));
        }
        File.Delete(record.StagingPath);
        Directory.CreateDirectory(record.StagingPath);
        await Assert.ThrowsAsync<IOException>(() => catalog.CleanAsync(item));
        Assert.True(File.Exists(record.BackupPath));
        Assert.True(File.Exists(journal));
        Assert.True(Directory.Exists(record.StagingPath));
    }

    [Fact]
    public async Task LinkedJournalAndArtifactAreRefusedWithoutFollowingTheirTargets()
    {
        using var work = new EditDirectory();
        var (catalog, record, journal) = Prepare(work);
        RecoveryItem item = Assert.Single((await catalog.DiscoverAsync()).Items);
        string valuable = work.File("valuable", "preserve");
        string valuableDirectory = Path.Combine(work.Path, "valuable-directory");
        Directory.CreateDirectory(valuableDirectory);
        File.WriteAllText(Path.Combine(valuableDirectory, "sentinel"), "preserve");
        try
        {
            File.Delete(record.StagingPath);
            CreateLink(record.StagingPath, OperatingSystem.IsWindows() ? valuableDirectory : valuable);
            await Assert.ThrowsAsync<IOException>(() => catalog.CleanAsync(item));
            Assert.Equal("preserve", File.ReadAllText(valuable));
            File.Delete(journal);
            CreateLink(journal, OperatingSystem.IsWindows() ? valuableDirectory : valuable);
            RecoveryScan scan = await catalog.DiscoverAsync();
            Assert.Empty(scan.Items);
            Assert.Single(scan.Warnings);
            Assert.Equal("preserve", File.ReadAllText(valuable));
            Assert.Equal("preserve", File.ReadAllText(Path.Combine(valuableDirectory, "sentinel")));
        }
        finally
        {
            RemoveTestLink(record.StagingPath);
            RemoveTestLink(journal);
        }
    }

    [Fact]
    public async Task MissingJournalCleanupOnlyForgetsTheCatalogPointerAndRefusesActiveOrChangedTransactions()
    {
        using var work = new EditDirectory();
        var (catalog, record, journal) = Prepare(work);
        File.Delete(journal);
        RecoveryItem stale = Assert.Single((await catalog.DiscoverAsync()).Items);
        Assert.True(stale.CatalogOnly);
        using (SourceWriteLock held = SourceWriteLock.Acquire(record.SourcePath))
        { await Assert.ThrowsAsync<IOException>(() => catalog.CleanAsync(stale)); }
        CommitJournal.Write(journal, record);
        await Assert.ThrowsAsync<IOException>(() => catalog.CleanAsync(stale));
        File.Delete(journal);
        string original = Hash(record.SourcePath);
        await catalog.CleanAsync(stale);
        Assert.Equal(original, Hash(record.SourcePath));
        Assert.True(File.Exists(record.BackupPath));
        Assert.True(File.Exists(record.StagingPath));
        Assert.Empty((await catalog.DiscoverAsync()).Items);
    }

    [Fact]
    public async Task LinkedCatalogParentIsRejectedBeforeReadingAnyPointer()
    {
        using var work = new EditDirectory();
        _ = Prepare(work);
        string alias = Path.Combine(work.Path, "linked-catalog");
        string target = Path.Combine(work.Path, "catalog");
        if (OperatingSystem.IsWindows()) { CreateLink(alias, target); }
        else { Directory.CreateSymbolicLink(alias, target); }
        try
        {
            var redirected = new CommitRecoveryCatalog(alias);
            await Assert.ThrowsAnyAsync<IOException>(() => redirected.DiscoverAsync());
            Assert.Single(Directory.GetFiles(target, "*.json"));
        }
        finally { RemoveTestLink(alias); }
    }

    private static void RemoveTestLink(string path)
    {
        if (!Path.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) { return; }
        // Remove the exact task-created link before recursive work-directory
        // cleanup. Recursive Windows junction cleanup can attempt privileged
        // mount-point removal; nonrecursive deletion does not touch its target.
        if (OperatingSystem.IsWindows()) { Directory.Delete(path); }
        else { File.Delete(path); }
    }

    private static void CreateLink(string path, string target)
    {
        if (!OperatingSystem.IsWindows()) { File.CreateSymbolicLink(path, target); return; }
        // Junctions are available to the ordinary test account without granting
        // symbolic-link privileges. Windows file symlinks are a separate gap.
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''", StringComparison.Ordinal)
            + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null" })
        { start.ArgumentList.Add(argument); }
        using Process child = Process.Start(start)!;
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> error = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(10000)) { child.Kill(true); child.WaitForExit(5000); throw new TimeoutException("Owned junction creator timed out."); }
        Assert.True(child.ExitCode == 0, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        Assert.True((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);
    }

    [Fact]
    public async Task SuccessfulCommitRemovesItsDurablePointerAndAllowsRepeatedCommit()
    {
        using var work = new EditDirectory();
        var catalog = new CommitRecoveryCatalog(Path.Combine(work.Path, "catalog"));
        using var service = new ExplorerService(stagingDirectory: work.Path, recoveryCatalog: catalog);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        foreach (string name in new[] { "/FIRST", "/SECOND" })
        {
            await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, name));
            await service.CommitAsync(info.Id);
            Assert.Empty((await catalog.DiscoverAsync()).Items);
            Assert.Empty((await catalog.DiscoverAsync()).Warnings);
        }
    }

    private static (CommitRecoveryCatalog, TransactionRecord, string) Prepare(EditDirectory work, CommitStage stage = CommitStage.Validated)
    {
        string source = work.File("source.img", "original");
        string id = Guid.NewGuid().ToString("N");
        string backup = CommitJournal.BackupPathFor(source, id);
        File.Copy(source, backup);
        string staging = CommitJournal.StagingPathFor(source, id);
        File.WriteAllText(staging, "staged");
        var record = new TransactionRecord(id, source, backup, staging, stage, DateTime.UtcNow)
        { SourceSha256 = Hash(source), SourceLength = new FileInfo(source).Length };
        string journal = CommitJournal.PathFor(source, id);
        CommitJournal.Write(journal, record);
        var catalog = new CommitRecoveryCatalog(Path.Combine(work.Path, "catalog"));
        catalog.Track(id, journal);
        return (catalog, record, journal);
    }

    private static string Hash(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
