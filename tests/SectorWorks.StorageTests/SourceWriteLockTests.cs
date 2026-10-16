using System.Diagnostics;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

/// <summary>Phase 3 checklist 3.2/3.6: a second writer must be rejected with an
/// explanatory failure while a direct-commit lock is held, and the lock must
/// release reliably so a subsequent attempt succeeds.</summary>
public class SourceWriteLockTests
{
    [Fact]
    public void LockReleasesReliablyAllowingASubsequentWriterToAcquireIt()
    {
        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "fat16");

        using (SourceWriteLock first = SourceWriteLock.Acquire(source))
        {
            // held and released via Dispose
        }

        using SourceWriteLock second = SourceWriteLock.Acquire(source);
    }

    [Fact]
    public void ConcurrentWriterInAnotherProcessIsRejectedWithAnExplanatoryFailure()
    {
        // fcntl byte-range advisory locks (used on Linux/macOS by FileStream.Lock) are
        // owned per-process, not per-handle: a second lock attempt from the SAME
        // process trivially "succeeds" (POSIX semantics), so a meaningful exclusion
        // test requires a genuinely independent process. Windows' mandatory sharing
        // mode is already covered indirectly via FileShare.None opens elsewhere, so
        // skip here where no independent POSIX writer is available.
        if (OperatingSystem.IsWindows()) { return; }
        string? python = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "python3")).FirstOrDefault(File.Exists);
        if (python is null) { return; }

        using var work = new WorkDirectory();
        string source = DiskWriterFixtures.Create(work, "raw", "filesystem", "fat16");
        string readyMarker = work.PathFor("lock-held.marker");
        string releaseMarker = work.PathFor("lock-release.marker");

        var start = new ProcessStartInfo(python) { RedirectStandardError = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(
            "import fcntl, sys, time\n" +
            "path, ready, release = sys.argv[1], sys.argv[2], sys.argv[3]\n" +
            "f = open(path, 'r+b')\n" +
            "fcntl.lockf(f, fcntl.LOCK_EX)\n" +
            "open(ready, 'w').close()\n" +
            "deadline = time.time() + 10\n" +
            "while not __import__('os').path.exists(release) and time.time() < deadline:\n" +
            "    time.sleep(0.05)\n" +
            "fcntl.lockf(f, fcntl.LOCK_UN)\n" +
            "f.close()\n");
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(readyMarker);
        start.ArgumentList.Add(releaseMarker);
        using Process process = Process.Start(start)!;
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(readyMarker) && DateTime.UtcNow < deadline) { Thread.Sleep(25); }
            Assert.True(File.Exists(readyMarker), "Independent Python writer never signalled lock acquisition.");

            IOException error = Assert.Throws<IOException>(() => SourceWriteLock.Acquire(source));
            Assert.Contains("locked by another writer", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.WriteAllText(releaseMarker, "release");
            process.WaitForExit(10_000);
        }

        using SourceWriteLock afterRelease = SourceWriteLock.Acquire(source);
    }
}
