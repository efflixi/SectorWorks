using System.Text.Json;

namespace SectorWorks.Storage;

/// <summary>
/// Phase 3 safe direct-commit primitives: source locking, durable transaction
/// journaling, and backup/recovery. These are container- and filesystem-agnostic;
/// they wrap the already-qualified <see cref="SectorWorks.Core.IStagedImageWriter"/>
/// Apply/ValidateOutput pipeline (Phase 2B) rather than reimplementing per-format
/// writers. A commit is only ever performed on a disposable staged temporary file
/// which is atomically swapped over the source after independent reopen validation.
/// </summary>
public enum CommitStage { BackupTaken, Staged, Validated, Finalized }

public sealed record TransactionRecord(string TransactionId, string SourcePath, string BackupPath,
    string StagingPath, CommitStage Stage, DateTime CreatedUtc);

/// <summary>Cross-platform advisory exclusive lock on a source image, held for the
/// duration of a direct-commit transaction. On Windows and Linux this uses
/// <see cref="FileStream.Lock"/> (LockFileEx / fcntl byte-range lock on the source
/// file itself), which other processes honoring advisory locking (including other
/// SectorWorks instances) cannot bypass. <see cref="FileStream.Lock"/> is not
/// supported by .NET on macOS, so macOS instead uses an exclusive sidecar marker
/// file; this only prevents concurrent SectorWorks writers, not arbitrary external
/// writers to the source, and is documented as a narrower, best-effort guarantee.</summary>
public sealed class SourceWriteLock : IDisposable
{
    private readonly FileStream handle;
    private readonly FileStream? sidecar;
    private readonly string? sidecarPath;
    private bool locked;

    private SourceWriteLock(FileStream handle, FileStream? sidecar, string? sidecarPath)
    {
        this.handle = handle;
        this.sidecar = sidecar;
        this.sidecarPath = sidecarPath;
    }

    public static SourceWriteLock Acquire(string path)
    {
        FileStream handle;
        try
        {
            handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1, FileOptions.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Cannot open the source image for direct-commit locking: " + error.Message, error);
        }
        if (OperatingSystem.IsMacOS())
        {
            string sidecarPath = path + ".sectorworks-lock";
            FileStream sidecar;
            try
            {
                sidecar = new FileStream(sidecarPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException error)
            {
                handle.Dispose();
                throw new IOException(
                    "The source image is locked by another writer (for example another in-progress SectorWorks direct commit).",
                    error);
            }
            return new SourceWriteLock(handle, sidecar, sidecarPath) { locked = true };
        }
        var result = new SourceWriteLock(handle, null, null);
        try
        {
            long length = Math.Max(handle.Length, 1);
            handle.Lock(0, length);
            result.locked = true;
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            handle.Dispose();
            throw new IOException(
                "The source image is locked by another writer (for example another in-progress SectorWorks direct commit).",
                error);
        }
        return result;
    }

    public void Dispose()
    {
        if (locked && sidecar is null && !OperatingSystem.IsMacOS())
        {
            try { handle.Unlock(0, Math.Max(handle.Length, 1)); }
            catch (IOException) { /* best-effort; handle close below always releases OS-level locks. */ }
        }
        handle.Dispose();
        sidecar?.Dispose();
        if (sidecarPath is not null)
        {
            try { File.Delete(sidecarPath); } catch (IOException) { }
        }
    }
}

/// <summary>Durable, flushed JSON transaction journal recording the current stage of
/// a direct-commit transaction so an interrupted commit can be safely detected and
/// recovered at next startup.</summary>
public static class CommitJournal
{
    public static string PathFor(string sourcePath, string transactionId) =>
        sourcePath + ".sectorworks-journal-" + transactionId;

    public static string BackupPathFor(string sourcePath, string transactionId) =>
        sourcePath + ".sectorworks-backup-" + transactionId;

    public static void Write(string journalPath, TransactionRecord record)
    {
        string json = JsonSerializer.Serialize(record);
        using var file = new FileStream(journalPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096);
        using var writer = new StreamWriter(file);
        writer.Write(json);
        writer.Flush();
        file.Flush(true);
    }

    public static TransactionRecord? Read(string journalPath)
    {
        if (!File.Exists(journalPath)) { return null; }
        try { return JsonSerializer.Deserialize<TransactionRecord>(File.ReadAllText(journalPath)); }
        catch (JsonException) { return null; }
    }

    public static void Delete(string journalPath)
    {
        try { File.Delete(journalPath); } catch (IOException) { }
    }

    /// <summary>Scans a directory for journals left by interrupted direct commits.</summary>
    public static IReadOnlyList<(string JournalPath, TransactionRecord Record)> ScanDirectory(string directory)
    {
        var results = new List<(string, TransactionRecord)>();
        if (!Directory.Exists(directory)) { return results; }
        foreach (string path in Directory.EnumerateFiles(directory, "*.sectorworks-journal-*"))
        {
            TransactionRecord? record = Read(path);
            if (record is not null) { results.Add((path, record)); }
        }
        return results;
    }
}

public static class CommitBackup
{
    /// <summary>Copies the source to a sibling backup path and flushes it to disk
    /// before any modification is attempted. Retained until validation succeeds.</summary>
    public static void Create(string sourcePath, string backupPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        source.CopyTo(backup);
        backup.Flush(true);
    }

    public static void Restore(string backupPath, string sourcePath)
    {
        File.Copy(backupPath, sourcePath, overwrite: true);
        using var target = new FileStream(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        target.Flush(true);
    }
}

/// <summary>Startup/explicit interrupted-transaction discovery and recovery.
/// Never assumes success: a journal at <see cref="CommitStage.Finalized"/> means the
/// source was already safely replaced before interruption (do not restore the
/// backup, only clean up); any earlier stage means the source was never modified
/// (backup restore is only a defensive no-op/idempotent safety net).</summary>
public static class CommitRecovery
{
    public sealed record RecoveryAction(string SourcePath, string JournalPath, string BackupPath,
        CommitStage Stage, string StagingPath)
    {
        public bool SourceWasModified => Stage == CommitStage.Finalized;
    }

    public static IReadOnlyList<RecoveryAction> Discover(string directory) =>
        [.. CommitJournal.ScanDirectory(directory)
            .Select(entry => new RecoveryAction(entry.Record.SourcePath, entry.JournalPath,
                entry.Record.BackupPath, entry.Record.Stage, entry.Record.StagingPath))];

    /// <summary>Restores the pre-commit source from backup (only meaningful/safe
    /// before <see cref="CommitStage.Finalized"/>) and removes transaction artifacts.</summary>
    public static void RestoreFromBackup(RecoveryAction action)
    {
        if (action.SourceWasModified)
        {
            throw new InvalidOperationException(
                "The interrupted commit already finalized; the source was already replaced. Restoring the backup would discard a completed edit. Use Discard instead.");
        }
        if (!File.Exists(action.BackupPath))
        {
            throw new FileNotFoundException("No backup is available; the source was never modified by this interrupted commit.", action.BackupPath);
        }
        CommitBackup.Restore(action.BackupPath, action.SourcePath);
        Cleanup(action);
    }

    /// <summary>Discards transaction artifacts without touching the source: safe
    /// whenever the commit already finalized, or when the caller has independently
    /// verified the source is otherwise acceptable.</summary>
    public static void Discard(RecoveryAction action) => Cleanup(action);

    private static void Cleanup(RecoveryAction action)
    {
        if (File.Exists(action.BackupPath)) { File.Delete(action.BackupPath); }
        if (File.Exists(action.StagingPath)) { File.Delete(action.StagingPath); }
        CommitJournal.Delete(action.JournalPath);
    }
}
