using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Storage;

namespace SectorWorks.Services;

/// <summary>Durable per-transaction pointers, never a whole-disk scan. Journal
/// data and paths remain untrusted even in the private application directory.</summary>
public sealed class CommitRecoveryCatalog : ICommitRecoveryService
{
    private const int MaximumRegistrations = 256;
    private readonly string directory;
    private sealed record Registration(string Id, string JournalPath);

    public CommitRecoveryCatalog(string? directory = null)
    {
        if (directory is null)
        {
            directory = Path.Combine(WorkspaceDataFiles.ApplicationDataDirectory(), "CommitRecovery");
        }
        this.directory = Path.GetFullPath(directory);
    }

    public void Track(string id, string journalPath)
    {
        ValidateId(id);
        WorkspaceDataFiles.CreatePrivateDirectory(directory);
        using var target = new SafeExtractionTarget(directory);
        using ExtractionFile file = target.CreateFile(id + ".json");
        JsonSerializer.Serialize(file.Stream, new Registration(id, journalPath));
        file.Complete();
    }

    public void Forget(string id)
    {
        ValidateId(id);
        using var target = new SafeExtractionTarget(directory);
        using RecoveryFile pointer = target.OpenRecoveryFile(id + ".json", allowDelete: true);
        pointer.Delete();
    }

    public Task<RecoveryScan> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var items = new List<RecoveryItem>();
        var warnings = new List<string>();
        if (!Directory.Exists(directory)) { return new RecoveryScan(items, warnings); }
        using var catalog = new SafeExtractionTarget(directory);
        int count = 0;
        foreach (string pointer in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > MaximumRegistrations)
            {
                warnings.Add("Recovery catalog exceeds 256 registrations; additional entries were not scanned. Retain and inspect the catalog: " + directory);
                break;
            }
            try
            {
                Registration registration = ReadRegistration(catalog, Path.GetFileName(pointer), out string pointerHash);
                if (DefinitelyMissing(registration.JournalPath))
                {
                    items.Add(new(registration.Id, SourceFor(registration), registration.JournalPath,
                        "RegistrationOnly", pointerHash,
                        "No journal exists at the registered path. After acquiring the source lock, cleanup can remove this stale catalog entry only. All source/backup/staging files remain untouched.",
                        true, CatalogOnly: true));
                    continue;
                }
                using var parent = new SafeExtractionTarget(Path.GetDirectoryName(registration.JournalPath)!);
                using RecoveryFile journal = parent.OpenRecoveryFile(Path.GetFileName(registration.JournalPath));
                byte[] bytes = ReadBounded(journal.Stream);
                TransactionRecord record = Parse(registration, bytes);
                items.Add(new RecoveryItem(record.TransactionId, record.SourcePath, registration.JournalPath,
                    record.Stage.ToString(), Convert.ToHexString(SHA256.HashData(bytes)),
                    (record.Stage == CommitStage.Finalized
                        ? "Replacement was recorded as finalized. Cleanup removes retained artifacts only; the source is never restored."
                        : "Commit was interrupted. Verify the source against its recorded baseline and backup before cleanup. A stale stage can also survive a completed replacement; mismatches are retained for inspection.")
                    + (record.Intent is { } intent
                        ? $" Intended changes: {intent.OperationCount}; plan SHA-256: {intent.PlanSha256}. The journal contains a bounded operation preview, not replay instructions."
                        : ""),
                    record.Stage == CommitStage.Finalized || record.SourceSha256 is not null && record.SourceLength is not null));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                warnings.Add(pointer + ": " + error.Message + " Artifacts retained.");
            }
        }
        return new RecoveryScan(items, warnings);
    }, cancellationToken);

    public Task CleanAsync(RecoveryItem item, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ValidateId(item.Id);
        using var catalog = new SafeExtractionTarget(directory);
        using RecoveryFile pointer = catalog.OpenRecoveryFile(item.Id + ".json", allowDelete: true);
        byte[] pointerBytes = ReadBounded(pointer.Stream);
        Registration registration = ParseRegistration(pointerBytes, item.Id + ".json");
        if (registration.JournalPath != item.JournalPath) { throw new IOException("Recovery registration changed; artifacts retained."); }
        if (item.CatalogOnly)
        {
            if (item.Stage != "RegistrationOnly" || item.SourcePath != SourceFor(registration)
                || Convert.ToHexString(SHA256.HashData(pointerBytes)) != item.JournalSha256)
            { throw new IOException("Recovery catalog entry changed since discovery; retained."); }
            CommitJournal.RejectLinks(item.SourcePath);
            using SourceWriteLock catalogLock = SourceWriteLock.Acquire(item.SourcePath);
            if (!DefinitelyMissing(registration.JournalPath))
            { throw new IOException("A journal now exists. Refresh recovery; the catalog entry was retained."); }
            cancellationToken.ThrowIfCancellationRequested();
            pointer.Delete();
            return;
        }
        using var parent = new SafeExtractionTarget(Path.GetDirectoryName(registration.JournalPath)!);
        using RecoveryFile journal = parent.OpenRecoveryFile(Path.GetFileName(registration.JournalPath), allowDelete: true);
        byte[] bytes = ReadBounded(journal.Stream);
        TransactionRecord record = Parse(registration, bytes);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != item.JournalSha256 || record.SourcePath != item.SourcePath
            || record.Stage.ToString() != item.Stage)
        {
            throw new IOException("Recovery journal changed since discovery; refresh and inspect it. Nothing removed.");
        }
        using SourceWriteLock sourceLock = SourceWriteLock.Acquire(record.SourcePath);
        var artifacts = new List<RecoveryFile>();
        try
        {
            RecoveryFile? backup = OpenOptional(parent, record.BackupPath);
            if (backup is not null) { artifacts.Add(backup); }
            RecoveryFile? staging = OpenOptional(parent, record.StagingPath);
            if (staging is not null) { artifacts.Add(staging); }
            cancellationToken.ThrowIfCancellationRequested();
            if (record.Stage != CommitStage.Finalized)
            {
                if (record.SourceSha256 is null || record.SourceLength is null || backup is null)
                {
                    throw new IOException("No trustworthy original baseline/backup is available. Retain artifacts for manual inspection.");
                }
                if (backup.Stream.Length != record.SourceLength || Hash(backup.Stream, cancellationToken) != record.SourceSha256)
                {
                    throw new IOException("Recovery backup differs from the recorded original; artifacts retained.");
                }
                sourceLock.VerifySource(record.SourceSha256, record.SourceLength.Value, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // From here cleanup is finite and not cancellable. Never rewrite the image.
            // Delete the journal last so partial cleanup stays discoverable.
            foreach (RecoveryFile file in artifacts) { file.Delete(); }
            journal.Delete();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Recovery cleanup did not complete. The source was never overwritten. Inspect retained journal/backup/staging paths: "
                + record.SourcePath + " | " + registration.JournalPath + " | " + record.BackupPath + " | " + record.StagingPath + ". " + error.Message, error);
        }
        finally { foreach (RecoveryFile file in artifacts) { file.Dispose(); } }
        journal.Dispose();
        pointer.Delete();
    }, cancellationToken);

    private static RecoveryFile? OpenOptional(SafeExtractionTarget parent, string path)
    {
        // Missing is distinct from inaccessible or nonregular; the latter must
        // fail before deleting any artifact, not be silently called absent.
        try { File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        return parent.OpenRecoveryFile(Path.GetFileName(path), allowDelete: true);
    }

    private static Registration ReadRegistration(SafeExtractionTarget catalog, string name, out string hash)
    {
        using RecoveryFile file = catalog.OpenRecoveryFile(name);
        byte[] bytes = ReadBounded(file.Stream);
        hash = Convert.ToHexString(SHA256.HashData(bytes));
        return ParseRegistration(bytes, name);
    }

    private static Registration ParseRegistration(byte[] bytes, string name)
    {
        ValidateId(Path.GetFileNameWithoutExtension(name));
        Registration registration = JsonSerializer.Deserialize<Registration>(bytes)
            ?? throw new InvalidDataException("Recovery registration is empty.");
        ValidateId(registration.Id);
        if (name != registration.Id + ".json" || string.IsNullOrWhiteSpace(registration.JournalPath)
            || !Path.IsPathFullyQualified(registration.JournalPath)
            || Path.GetFullPath(registration.JournalPath) != registration.JournalPath)
        {
            throw new InvalidDataException("Invalid recovery registration path/identity.");
        }
        _ = SourceFor(registration);
        CommitJournal.RejectLinks(registration.JournalPath);
        return registration;
    }

    private static string SourceFor(Registration registration)
    {
        string suffix = ".sectorworks-journal-" + registration.Id;
        if (!registration.JournalPath.EndsWith(suffix, StringComparison.Ordinal))
        { throw new InvalidDataException("Recovery registration does not identify its transaction journal."); }
        string source = registration.JournalPath[..^suffix.Length];
        CommitJournal.Validate(registration.JournalPath, new(registration.Id, source,
            CommitJournal.BackupPathFor(source, registration.Id), CommitJournal.StagingPathFor(source, registration.Id),
            CommitStage.BackupTaken, DateTime.UtcNow));
        return source;
    }

    private static bool DefinitelyMissing(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    private static TransactionRecord Parse(Registration registration, byte[] bytes)
    {
        TransactionRecord record;
        try
        {
            record = JsonSerializer.Deserialize<TransactionRecord>(bytes)
                ?? throw new InvalidDataException("Recovery journal is empty.");
        }
        catch (JsonException error) { throw new IOException("Recovery journal changed or contains invalid JSON; artifacts retained.", error); }
        CommitJournal.Validate(registration.JournalPath, record);
        if (record.TransactionId != registration.Id) { throw new InvalidDataException("Recovery transaction identity differs from its registration."); }
        foreach (string path in new[] { record.SourcePath, record.BackupPath, record.StagingPath }) { CommitJournal.RejectLinks(path); }
        return record;
    }

    private static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) { throw new InvalidDataException("Invalid recovery transaction identity."); }
    }

    private static byte[] ReadBounded(Stream stream)
    {
        if (stream.Length is < 1 or > CommitJournal.MaximumJournalBytes) { throw new InvalidDataException("Recovery JSON exceeds its 32 KiB bound or is empty."); }
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) { throw new IOException("Recovery JSON changed while reading."); }
        return bytes;
    }

    private static string Hash(Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long remaining = stream.Length;
        int read;
        while (remaining > 0 && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) != 0)
        { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); remaining -= read; }
        if (remaining != 0 || stream.ReadByte() != -1) { throw new IOException("Recovery backup changed while hashing."); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
