using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.ServicesTests.EditingTests;

namespace SectorWorks.ServicesTests;

public sealed class RuntimeConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateProcessesAndInstancesAppendCompleteRecordsAndRotateTogether(bool rotate)
    {
        using var work = new EditDirectory();
        string path = Path.Combine(work.Path, "concurrent.jsonl");
        string? original = rotate ? JsonSerializer.Serialize(new { Padding = new string('x', 4 * 1024 * 1024) }) + "\n" : null;
        if (original is not null) { File.WriteAllText(path, original); }
        string release = Path.Combine(work.Path, "release");
        string firstReady = Path.Combine(work.Path, "first-ready");
        string secondReady = Path.Combine(work.Path, "second-ready");
        using Process first = StartProbe("log", path, "first", "150", firstReady, release);
        using Process second = StartProbe(true, "log", path, "second", "150", secondReady, release);
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!(File.Exists(firstReady) && File.Exists(secondReady)) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            Assert.True(File.Exists(firstReady) && File.Exists(secondReady), "Independent log probes did not reach their start barrier.");
            File.WriteAllText(release, "go");
            await Parallel.ForEachAsync(Enumerable.Range(0, 150), (index, _) =>
            {
                new JsonExplorerLog(path).Write("ConcurrentProbe", "parent", "parent:" + index);
                return ValueTask.CompletedTask;
            });
            await Task.WhenAll(RequireSuccess(first), RequireSuccess(second));
            string[] messages = File.ReadLines(path).Select(line =>
            {
                using JsonDocument record = JsonDocument.Parse(line);
                return record.RootElement.GetProperty("Message").GetString()!;
            }).ToArray();
            Assert.Equal(450, messages.Length);
            Assert.Equal(450, messages.Distinct(StringComparer.Ordinal).Count());
            foreach (string worker in new[] { "first", "second", "parent" })
            {
                Assert.All(Enumerable.Range(0, 150), index => Assert.Contains(worker + ":" + index, messages));
            }
            if (rotate) { Assert.Equal(original, File.ReadAllText(path + ".previous")); }
            else { Assert.False(File.Exists(path + ".previous")); }
        }
        finally
        {
            StopIfRunning(first);
            StopIfRunning(second);
        }
    }

    [Fact]
    public void LogIoFailuresRemainVisibleAndDoNotReplaceTheDestination()
    {
        using var work = new EditDirectory();
        string path = Path.Combine(work.Path, "log-directory");
        Directory.CreateDirectory(path);
        Exception? error = Record.Exception(() => new JsonExplorerLog(path).Write("test", "", "not saved"));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Logging unexpectedly succeeded.");
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(path));
    }

    [Fact]
    public async Task GlobalPathGateExcludesAWriterInAnIndependentProcessSession()
    {
        using var work = new EditDirectory();
        string path = Path.Combine(work.Path, "isolated.jsonl");
        string ready = Path.Combine(work.Path, "ready");
        string release = Path.Combine(work.Path, "release");
        using var gate = new Mutex(false, JsonExplorerLog.MutexNameFor(path));
        using Process writer = StartProbe(true, "log", path, "isolated", "1", ready, release);
        gate.WaitOne();
        try
        {
            try
            {
                WaitForFile(ready);
                File.WriteAllText(release, "go");
                WaitForFile(ready + ".attempt");
                Assert.False(writer.WaitForExit(750), "An independent process/session bypassed the held path gate.");
                Assert.False(File.Exists(path));
            }
            finally { gate.ReleaseMutex(); }
            await RequireSuccess(writer);
            using JsonDocument record = JsonDocument.Parse(Assert.Single(File.ReadAllLines(path)));
            Assert.Equal("isolated:0", record.RootElement.GetProperty("Message").GetString());
        }
        finally { StopIfRunning(writer); }
    }

    [Fact]
    public async Task WriterExclusionSurvivesSourceVerificationBackupAndAtomicReplacement()
    {
        using var work = new EditDirectory();
        string source = work.File("source \u03bb.bin", new string('a', 65537));
        string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        string backup = Path.Combine(work.Path, "backup.bin");
        using (SourceWriteLock held = SourceWriteLock.Acquire(source))
        {
            held.VerifySource(expectedHash, new FileInfo(source).Length);
            held.CreateBackup(backup, expectedHash, new FileInfo(source).Length);
            held.VerifySource(expectedHash, new FileInfo(source).Length);
            using (var unrelatedReader = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                Assert.Equal('a', unrelatedReader.ReadByte());
            }
            Assert.Throws<IOException>(() => SourceWriteLock.Acquire(source));
            using Process competitor = StartProbe("lock-blocked", source);
            await RequireSuccess(competitor);
            string replacement = work.File("replacement \u03bb.bin", "replacement");
            held.ReplaceSource(replacement);
            Assert.Equal("replacement", File.ReadAllText(source));
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup))));
            string oldHandleBackup = Path.Combine(work.Path, "old-handle.bin");
            held.CreateBackup(oldHandleBackup, expectedHash, 65537);
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(oldHandleBackup))));
        }
        using SourceWriteLock afterRelease = SourceWriteLock.Acquire(source);
    }

    [Fact]
    public void AtomicReplacementRefusesNonSiblingStagingWithoutChangingEitherFile()
    {
        using var work = new EditDirectory();
        string source = work.File("source.bin", "original");
        string other = Path.Combine(work.Path, "other");
        Directory.CreateDirectory(other);
        string staging = Path.Combine(other, "staging.bin");
        File.WriteAllText(staging, "staged");
        using SourceWriteLock held = SourceWriteLock.Acquire(source);
        Assert.Throws<IOException>(() => held.ReplaceSource(staging));
        Assert.Equal("original", ReadShared(source));
        Assert.Equal("staged", File.ReadAllText(staging));
    }

    [Fact]
    public void AtomicReplacementFailureRetainsStagingAndNeverRemovesADirectoryAtTheSourceName()
    {
        using var work = new EditDirectory();
        string source = work.File("source.bin", "original");
        string retained = Path.Combine(work.Path, "retained.bin");
        string staging = work.File("staging.bin", "staged");
        using SourceWriteLock held = SourceWriteLock.Acquire(source);
        File.Move(source, retained);
        Directory.CreateDirectory(source);
        Exception? error = Record.Exception(() => held.ReplaceSource(staging));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Atomic replacement unexpectedly succeeded.");
        Assert.True(Directory.Exists(source));
        Assert.Equal("original", ReadShared(retained));
        Assert.Equal("staged", File.ReadAllText(staging));
    }

    private static Process StartProbe(params string[] arguments) => StartProbe(false, arguments);

    private static Process StartProbe(bool independentSession, params string[] arguments)
    {
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        bool isolated = independentSession && OperatingSystem.IsLinux();
        var start = new ProcessStartInfo(isolated ? "/usr/bin/setsid" : dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (isolated)
        {
            start.ArgumentList.Add("--wait");
            start.ArgumentList.Add(dotnet);
        }
        foreach (string argument in new[]
        {
            "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "SectorWorks.ServicesTests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "SectorWorks.ServicesTests.deps.json"),
            typeof(SectorWorks.RuntimeProbe.Program).Assembly.Location
        }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new IOException("The independent runtime probe could not start.");
    }

    private static string ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    private static void WaitForFile(string path)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(path) && DateTime.UtcNow < deadline) { Thread.Sleep(20); }
        Assert.True(File.Exists(path), "The independent runtime probe did not signal: " + path);
    }

    private static async Task RequireSuccess(Process process)
    {
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally { StopIfRunning(process); }
        Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
    }

    private static void StopIfRunning(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000)) { throw new TimeoutException("The runtime probe could not be stopped."); }
        }
    }
}
