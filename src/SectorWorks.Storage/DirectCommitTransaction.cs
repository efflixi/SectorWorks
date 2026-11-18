using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;

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

// Audit information only. Recovery never replays these operations or treats the
// digest/preview as authority to restore a source or resolve an artifact path.
public sealed record CommitIntentOperation(ImageChangeKind Kind, string VolumeId, string ImagePath,
    string? NewName, long ContentLength, string? ContentSha256);
public sealed record CommitIntent(int OperationCount, string PlanSha256, CommitIntentOperation[] Preview);

public sealed record TransactionRecord(string TransactionId, string SourcePath, string BackupPath,
    string StagingPath, CommitStage Stage, DateTime CreatedUtc)
{
    public string? SourceSha256 { get; init; }
    public long? SourceLength { get; init; }
    public CommitIntent? Intent { get; init; }
}

/// <summary>Cross-platform advisory exclusive lock on a source image, held for the
/// duration of a direct-commit transaction. Windows sharing denies other write
/// handles while allowing the final atomic replacement; Linux uses an advisory
/// open-file-description fcntl byte-range lock. Readers of the original must be
/// closed before acquisition. Other operating systems are not supported.</summary>
public sealed class SourceWriteLock : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> ActivePaths = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string path;
    private readonly FileStream handle;
    private bool locked;
    private bool disposed;

    private SourceWriteLock(string path, FileStream handle)
    {
        this.path = path;
        this.handle = handle;
    }

    public static SourceWriteLock Acquire(string path)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Direct commit is supported only on Windows and Linux.");
        }
        path = Path.GetFullPath(path);
        if (!ActivePaths.TryAdd(path, 0))
        {
            throw new IOException("The source image is locked by another writer in this process.");
        }
        FileStream handle;
        try
        {
            handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                OperatingSystem.IsWindows() ? FileShare.Read | FileShare.Delete : FileShare.ReadWrite | FileShare.Delete,
                1, FileOptions.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ActivePaths.TryRemove(path, out _);
            throw new IOException("Cannot open the source image for direct-commit locking: " + error.Message, error);
        }
        var result = new SourceWriteLock(path, handle);
        if (OperatingSystem.IsWindows()) { return result; }
        try
        {
            long length = Math.Max(handle.Length, 1);
            LinuxSourceLock.Set(handle.SafeFileHandle, length, acquire: true);
            result.locked = true;
        }
        catch (Exception error) when (error is IOException or NotSupportedException)
        {
            handle.Dispose();
            ActivePaths.TryRemove(path, out _);
            throw new IOException(
                "The source image is locked by another writer, or exclusive locking is unsupported: " + error.Message,
                error);
        }
        return result;
    }

    public void VerifySource(string expectedHash, long expectedLength, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536);
        if (Hash(source, expectedLength, token) != expectedHash)
        {
            throw new IOException("Source changed since opening; direct commit was aborted. Reopen the source image.");
        }
        handle.Position = 0;
        if (Hash(handle, expectedLength, token) != expectedHash)
        {
            throw new IOException("Locked source changed since opening; direct commit was aborted.");
        }
    }

    public void CreateBackup(string backupPath, string expectedHash, long expectedLength, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536);
        try
        {
            handle.Position = 0;
            if (Hash(handle, expectedLength, token, backup) != expectedHash)
            {
                throw new IOException("Source changed while creating the direct-commit backup.");
            }
            backup.Flush(true);
        }
        catch (Exception error)
        {
            backup.Dispose();
            try { File.Delete(backupPath); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Failed backup could not be removed; retained at " + backupPath,
                    new AggregateException(error, cleanup));
            }
            throw;
        }
    }

    public void ReplaceSource(string staging)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string fullPath = Path.GetFullPath(staging);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (fullPath.Equals(path, comparison)
            || !string.Equals(Path.GetDirectoryName(fullPath), Path.GetDirectoryName(path), comparison))
        {
            throw new IOException("Validated direct-commit staging must be a distinct sibling of the source image.");
        }
        if (OperatingSystem.IsWindows()) { WindowsAtomicReplacement.Replace(fullPath, path); }
        else { File.Move(fullPath, path, overwrite: true); }
    }

    private static string Hash(Stream source, long expectedLength, CancellationToken token, Stream? copy = null)
    {
        if (source.Length != expectedLength) { throw new IOException("Source length changed since opening."); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long count = 0;
        int read;
        while ((read = source.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            count = checked(count + read);
            if (count > expectedLength) { throw new IOException("Source grew during direct-commit verification."); }
            hash.AppendData(buffer, 0, read);
            copy?.Write(buffer, 0, read);
        }
        token.ThrowIfCancellationRequested();
        if (count != expectedLength || source.Length != expectedLength) { throw new IOException("Source length changed during direct-commit verification."); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (locked && OperatingSystem.IsLinux())
        {
            try
            {
                LinuxSourceLock.Set(handle.SafeFileHandle, Math.Max(handle.Length, 1), acquire: false);
            }
            catch (IOException) { /* best-effort; handle close below always releases OS-level locks. */ }
        }
        handle.Dispose();
        ActivePaths.TryRemove(path, out _);
    }
}

/// <summary>Durable, flushed JSON transaction journal recording the current stage of
/// a direct-commit transaction so an interrupted commit can be safely detected and
/// recovered at next startup.</summary>
public static class CommitJournal
{
    public const int MaximumJournalBytes = 32768;

    public static string PathFor(string sourcePath, string transactionId) =>
        sourcePath + ".sectorworks-journal-" + transactionId;

    public static string BackupPathFor(string sourcePath, string transactionId) =>
        sourcePath + ".sectorworks-backup-" + transactionId;

    public static string StagingPathFor(string sourcePath, string transactionId) =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, ".sectorworks-commit-" + transactionId + ".img");

    public static void Validate(string journalPath, TransactionRecord record)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (record is null || !Guid.TryParseExact(record.TransactionId, "N", out _)
            || !Enum.IsDefined(record.Stage) || record.CreatedUtc == default
            || string.IsNullOrWhiteSpace(record.SourcePath) || !Path.IsPathFullyQualified(record.SourcePath)
            || !string.Equals(Path.GetFullPath(record.SourcePath), record.SourcePath, comparison)
            || !string.Equals(journalPath, PathFor(record.SourcePath, record.TransactionId), comparison)
            || !string.Equals(record.BackupPath, BackupPathFor(record.SourcePath, record.TransactionId), comparison)
            || !string.Equals(record.StagingPath, StagingPathFor(record.SourcePath, record.TransactionId), comparison)
            || (OperatingSystem.IsWindows() && (record.SourcePath.Length < 4
                || !char.IsAsciiLetter(record.SourcePath[0]) || record.SourcePath[1] != ':'
                || record.SourcePath[2] != '\\' || record.SourcePath[2..].Contains(':')))
            || record.SourceSha256 is not null && (record.SourceSha256.Length != 64 || !record.SourceSha256.All(Uri.IsHexDigit))
            || record.SourceLength is < 0)
        {
            throw new InvalidDataException("Recovery journal contains invalid identities, stages or non-sibling artifact paths.");
        }
        if (record.Intent is { } intent)
        {
            bool Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
            if (intent.OperationCount <= 0 || !Digest(intent.PlanSha256) || intent.Preview is null
                || intent.Preview.Length != Math.Min(8, intent.OperationCount)
                || intent.Preview.Any(operation => operation is null || !Enum.IsDefined(operation.Kind)
                    || operation.Kind == ImageChangeKind.Metadata
                    || string.IsNullOrWhiteSpace(operation.VolumeId) || operation.VolumeId.Length > 256
                    || operation.VolumeId.Any(char.IsControl)
                    || operation.ImagePath is not { Length: > 1 and <= 240 } || !operation.ImagePath.StartsWith('/')
                    || !operation.ImagePath.Split('/').Skip(1).All(ImageEditNames.IsSupportedName)
                    || (operation.Kind == ImageChangeKind.Rename ? !ImageEditNames.IsSupportedName(operation.NewName ?? "") : operation.NewName is not null)
                    || (operation.Kind is ImageChangeKind.AddFile or ImageChangeKind.ReplaceFile
                        ? operation.ContentLength < 0 || !Digest(operation.ContentSha256)
                        : operation.ContentLength != 0 || operation.ContentSha256 is not null)))
            { throw new InvalidDataException("Recovery journal contains an invalid bounded commit-intent summary."); }
        }
    }

    public static string Serialize(string journalPath, TransactionRecord record)
    {
        Validate(journalPath, record);
        string json = JsonSerializer.Serialize(record);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumJournalBytes)
        { throw new InvalidDataException("Recovery journal exceeds its 32 KiB bound; no journal was written."); }
        return json;
    }

    public static void Write(string journalPath, TransactionRecord record)
    {
        string json = Serialize(journalPath, record);
        // Never truncate/follow a pre-existing journal or leave a torn JSON stage
        // after process termination. The temporary is exclusively created and
        // flushed before its directory entry replaces the previous journal.
        string temporary = journalPath + ".write-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096))
            {
                using var writer = new StreamWriter(file, leaveOpen: true);
                writer.Write(json);
                writer.Flush();
                file.Flush(true);
            }
            File.Move(temporary, journalPath, overwrite: true);
        }
        catch
        {
            // A failed cleanup must identify its residual, rather than hide it.
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { throw new IOException("Journal update failed; retain the temporary journal at " + temporary, cleanup); }
            throw;
        }
    }

    public static TransactionRecord? Read(string journalPath)
    {
        if (!File.Exists(journalPath)) { return null; }
        RejectLinks(journalPath);
        using var stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumJournalBytes) { throw new InvalidDataException("Recovery journal exceeds the 32 KiB bound."); }
        try
        {
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) { throw new IOException("Recovery journal changed while reading."); }
            TransactionRecord? record = JsonSerializer.Deserialize<TransactionRecord>(bytes);
            if (record is not null) { Validate(journalPath, record); }
            return record;
        }
        catch (JsonException) { return null; }
    }

    public static void RejectLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { current = Path.GetDirectoryName(current); continue; }
            catch (DirectoryNotFoundException) { current = Path.GetDirectoryName(current); continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Recovery refuses linked/reparse paths: " + current);
            }
            current = Path.GetDirectoryName(current);
        }
    }

    public static void Delete(string journalPath) => File.Delete(journalPath);

    /// <summary>Scans a directory for journals left by interrupted direct commits.</summary>
    public static IReadOnlyList<(string JournalPath, TransactionRecord Record)> ScanDirectory(string directory)
    {
        var results = new List<(string, TransactionRecord)>();
        if (!Directory.Exists(directory)) { return results; }
        int count = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "*.sectorworks-journal-*").Take(257))
        {
            if (++count > 256) { throw new InvalidDataException("Recovery discovery exceeds the 256-journal bound."); }
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
/// backup, only clean up). An earlier journal can also survive an atomic replace
/// if the final journal write failed, so recovery must independently check the
/// source rather than infer its contents from the journal stage.</summary>
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
        ValidateAction(action);
        if (!File.Exists(action.BackupPath))
        {
            throw new FileNotFoundException("No backup is available; the source was never modified by this interrupted commit.", action.BackupPath);
        }
        string backupHash;
        long backupLength;
        using (var backup = File.OpenRead(action.BackupPath))
        {
            backupLength = backup.Length;
            backupHash = Convert.ToHexString(SHA256.HashData(backup));
        }
        using SourceWriteLock sourceLock = SourceWriteLock.Acquire(action.SourcePath);
        try { sourceLock.VerifySource(backupHash, backupLength); }
        catch (IOException error)
        {
            throw new IOException("Source differs from the recovery backup; it may already have finalized or changed externally. "
                + "Refusing to overwrite it. Review the source, backup and journal independently.", error);
        }
        // Before atomic finalize the source is already the backup, so no rewrite
        // is needed. Never roll back a possibly completed replacement.
        Cleanup(action);
    }

    /// <summary>Discards transaction artifacts without touching the source: safe
    /// whenever the commit already finalized, or when the caller has independently
    /// verified the source is otherwise acceptable.</summary>
    public static void Discard(RecoveryAction action)
    {
        ValidateAction(action);
        Cleanup(action);
    }

    private static void ValidateAction(RecoveryAction action)
    {
        TransactionRecord record = CommitJournal.Read(action.JournalPath)
            ?? throw new InvalidDataException("Recovery journal is missing or invalid; artifacts retained.");
        if (record.SourcePath != action.SourcePath || record.BackupPath != action.BackupPath
            || record.StagingPath != action.StagingPath || record.Stage != action.Stage)
        {
            throw new InvalidDataException("Recovery journal changed since discovery; artifacts retained.");
        }
        foreach (string path in new[] { action.SourcePath, action.BackupPath, action.StagingPath })
        {
            CommitJournal.RejectLinks(path);
            if (Directory.Exists(path)) { throw new IOException("Recovery artifact is a directory; nothing was removed: " + path); }
        }
    }

    private static void Cleanup(RecoveryAction action)
    {
        if (File.Exists(action.BackupPath)) { File.Delete(action.BackupPath); }
        if (File.Exists(action.StagingPath)) { File.Delete(action.StagingPath); }
        CommitJournal.Delete(action.JournalPath);
    }
}
