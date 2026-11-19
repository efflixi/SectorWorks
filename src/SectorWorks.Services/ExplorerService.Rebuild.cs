using SectorWorks.Core;

namespace SectorWorks.Services;

public sealed partial class ExplorerService
{
    private static bool IsOptical(IImageSession image) => image.Metadata.Format is "ISO" or "UDF";

    private static IImageRebuildSource? RebuildSource(IImageSession image, string? volumeId = null)
    {
        if (!IsOptical(image) || image.Metadata.HasOpticalPartitionLayout || image.Partitions.Count != 0 || image.Volumes.Count != 1
            || volumeId is not null && volumeId != image.Volumes[0].Id)
        { return null; }
        return image.Volumes[0].FileSystem as IImageRebuildSource;
    }

    private static Capability? RebuildCapability(IImageSession image, string? volumeId = null)
    {
        if (image is IImageSetRebuildSource set) { return set.QualifyRebuild(volumeId); }
        if (!IsOptical(image)) { return null; }
        return RebuildSource(image, volumeId)?.RebuildCapability
            ?? new(false, image.Metadata.HasOpticalPartitionLayout || image.Partitions.Count != 0
                ? "Hybrid or partitioned optical images are not qualified for rebuild."
                : image.Warnings.FirstOrDefault(w => w.StartsWith("Optical rebuild unavailable", StringComparison.Ordinal)
                    || w.StartsWith("Read-only optical layout", StringComparison.Ordinal)
                    || w.StartsWith("Unqualified UDF layout", StringComparison.Ordinal))
                    ?? "The image has no optical filesystem layout that SectorWorks can safely reconstruct.");
    }

    private Capability QualifyEdit(IImageSession image, string? volumeId = null) =>
        RebuildCapability(image, volumeId) ?? (volumeId is null ? writer.Qualify(image) : writer.QualifyVolume(image, volumeId));

    private Capability QualifySourceCommit(IImageSession image, string volumeId) => IsOptical(image) || image is IImageSetRebuildSource
        ? new(false, "Optical changes require a rebuilt new image; source replacement is unavailable.")
        : CommitCapability(writer.QualifyCommitVolume(image, volumeId), image.Metadata.Path);

    private SaveImageResult Rebuild(SessionLease lease, IImageRebuildSource source, string target, string directory,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        EditingState state = EnsureEditing(lease, token);
        state.VerifySpool();
        if (state.Position == 0)
        { throw new InvalidOperationException("Stage at least one change before rebuilding an optical image."); }
        StagedWriteOperation[] operations = state.Operations.Take(state.Position).ToArray();
        string volumeId = lease.Image.Volumes.Single().Id;
        IImageRebuildPlan plan = source.PlanRebuild(operations, token);
        if (storageSpace.AvailableBytes(directory) < plan.OutputLength)
        { throw new IOException("Insufficient destination space for the rebuilt optical image."); }
        progress?.Report(new("Checking staged tree and content", target, 0, 0));
        Dictionary<string, string> expected = Fingerprint(GetVolume(lease, volumeId).FileSystem, token);
        string temporary = Path.Combine(directory, ".sectorworks-rebuild-" + Guid.NewGuid().ToString("N") + ".iso");
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            {
                created = true;
                progress?.Report(new("Writing rebuilt optical image", target, 0, 0));
                plan.Write(output, progress, token);
                token.ThrowIfCancellationRequested();
                output.Flush(true);
            }
            progress?.Report(new("Reopening and validating rebuilt image", target, plan.OutputLength, 0));
            using (IImageSession reopened = opener.OpenReadOnly(temporary, token))
            {
                if (reopened.Metadata.Format != lease.Image.Metadata.Format
                    || reopened.Metadata.ContainerLength != plan.OutputLength || reopened.Partitions.Count != 0
                    || reopened.Volumes.Count != 1 || reopened.Volumes[0].Id != volumeId)
                { throw new InvalidDataException("Rebuilt image did not reopen with its expected optical layout."); }
                IReadOnlyFileSystem output = reopened.Volumes[0].FileSystem;
                plan.Validate(output, token);
                IReadOnlyDictionary<string, string> actual = Fingerprint(output, token);
                if (expected.Count != actual.Count || expected.Any(row => !actual.TryGetValue(row.Key, out string? value) || row.Value != value))
                { throw new InvalidDataException("Rebuilt optical tree, file lengths or SHA-256 content hashes differ from staging."); }
            }
            progress?.Report(new("Rechecking source baseline", target, plan.OutputLength, 0));
            VerifySource(lease, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, false);
            created = false;
            int count = state.Current.Volumes[volumeId].Values.Count(n => !n.Entry.IsDirectory);
            log.Write("RebuildValidated", target, $"Validated {count} files and qualified optical metadata; source untouched.");
            return new(target, plan.OutputLength, count,
                "Rebuilt and reopened a new optical image. Namespace, retained volume/entry metadata, allocation and file SHA-256 verified. Source and staged changes retained. Bootability and exact layout are not guaranteed.");
        }
        catch (Exception error)
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    string message = "Rebuild failed. Incomplete output could not be removed: " + temporary;
                    log.Write("ResidualSaveOutput", temporary, message, "Warning");
                    throw new IOException(message, new AggregateException(error, cleanup));
                }
            }
            throw;
        }
    }
}
