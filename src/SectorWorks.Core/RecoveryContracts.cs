namespace SectorWorks.Core;

public sealed record RecoveryItem(string Id, string SourcePath, string JournalPath, string Stage,
    string JournalSha256, string Description, bool CanClean, bool CatalogOnly = false);

public sealed record RecoveryScan(IReadOnlyList<RecoveryItem> Items, IReadOnlyList<string> Warnings);

public interface ICommitRecoveryService
{
    Task<RecoveryScan> DiscoverAsync(CancellationToken cancellationToken = default);
    Task CleanAsync(RecoveryItem item, CancellationToken cancellationToken = default);
}
