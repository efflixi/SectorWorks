using SectorWorks.Core;

namespace SectorWorks.Storage;

internal sealed partial class FatFileSystem
{
    internal byte[] CaptureRootLabel(CancellationToken token)
    {
        var labels = new List<byte[]>();
        DirectoryEntries("/", _rootCluster, true, token, strictNames: true, volumeLabels: labels);
        if (labels.Count > 1) { throw new ImageFormatException("Multiple FAT root volume-label entries are not qualified."); }
        return labels.Count == 0 ? [] : labels[0];
    }

    internal void ValidateRootLabel(byte[] expected, CancellationToken token)
    {
        if (!expected.AsSpan().SequenceEqual(CaptureRootLabel(token)))
        { throw new ImageFormatException("FAT output changed its root volume-label entry."); }
    }

    internal sealed record PreservedEntry(string Path, byte[] ShortEntry, byte[][] LongEntries, byte[]? SpecialEntries,
        bool Renamed = false, bool Replaced = false, bool AliasMayChange = false);

    internal Dictionary<string, PreservedEntry> CapturePreservation(CancellationToken token)
    {
        var result = new Dictionary<string, PreservedEntry>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, uint Cluster, bool Root, int Depth)>();
        pending.Push(("/", _rootCluster, true, 0));
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (directory.Depth > 128) { throw new ImageFormatException("FAT preservation traversal exceeds depth limits."); }
            foreach (Entry entry in DirectoryEntries(directory.Path, directory.Cluster, directory.Root, token, strictNames: true))
            {
                if (result.Count >= 100000) { throw new ImageFormatException("FAT preservation traversal exceeds entry limits."); }
                result.Add(entry.File.Path, CaptureEntry(entry));
                if (entry.File.IsDirectory) { pending.Push((entry.File.Path, entry.Cluster, false, directory.Depth + 1)); }
            }
        }
        return result;
    }

    private PreservedEntry CaptureEntry(Entry entry) =>
        new(entry.File.Path, Bounds.Read(_device, entry.DirectoryOffset, 32),
            entry.LongOffsets.Select(offset => Bounds.Read(_device, offset, 32)).ToArray(),
            entry.File.IsDirectory ? Bounds.Read(_device, ClusterOffset(entry.Cluster), 64) : null);

    internal void RecordExpectedChange(Dictionary<string, PreservedEntry> expected, ImageChangeRequest request, CancellationToken token)
    {
        if (request.Kind == ImageChangeKind.Delete)
        {
            foreach (string path in expected.Keys.Where(path => path.Equals(request.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(request.Path + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                expected.Remove(path);
            }
            return;
        }
        if (request.Kind == ImageChangeKind.Rename)
        {
            PreservedEntry original = expected[request.Path];
            string renamed = Parent(original.Path) + "/" + request.NewName;
            PreservedEntry actual = CaptureEntry(Resolve(renamed, token) ?? throw new ImageFormatException("Renamed FAT entry is missing."));
            if (!actual.Path.Equals(renamed, StringComparison.Ordinal))
            {
                throw new ImageFormatException("FAT rename lost the requested exact spelling.");
            }
            byte[] shortEntry = original.ShortEntry.ToArray();
            bool caseOnly = original.Path[(original.Path.LastIndexOf('/') + 1)..]
                .Equals(request.NewName, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly) { actual.ShortEntry.AsSpan(0, 11).CopyTo(shortEntry); }
            if (!caseOnly || actual.LongEntries.Length == 0) { shortEntry[12] = actual.ShortEntry[12]; }
            foreach (string oldPath in expected.Keys.Where(path => path.Equals(original.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(original.Path + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                PreservedEntry previous = expected[oldPath];
                expected.Remove(oldPath);
                string newPath = renamed + previous.Path[original.Path.Length..];
                expected.Add(newPath, previous with { Path = newPath });
            }
            expected[renamed] = original with { Path = renamed, ShortEntry = shortEntry, LongEntries = actual.LongEntries };
            return;
        }
        Entry entry = Resolve(request.Path, token) ?? throw new ImageFormatException("Changed FAT entry is missing.");
        PreservedEntry changed = CaptureEntry(entry);
        if (request.Kind == ImageChangeKind.ReplaceFile)
        {
            PreservedEntry original = expected[request.Path];
            byte[] shortEntry = original.ShortEntry.ToArray();
            if (_bits == 32) { changed.ShortEntry.AsSpan(20, 2).CopyTo(shortEntry.AsSpan(20)); }
            changed.ShortEntry.AsSpan(26, 6).CopyTo(shortEntry.AsSpan(26));
            expected[request.Path] = original with { ShortEntry = shortEntry };
        }
        else
        {
            if (!entry.File.Name.Equals(request.Path[(request.Path.LastIndexOf('/') + 1)..], StringComparison.Ordinal))
            {
                throw new ImageFormatException("FAT creation lost the requested exact spelling.");
            }
            expected.Add(changed.Path, changed);
        }
    }

    internal void ValidatePreservation(Dictionary<string, PreservedEntry> expected, CancellationToken token)
    {
        Dictionary<string, PreservedEntry> actual = CapturePreservation(token);
        if (actual.Count != expected.Count) { throw new ImageFormatException("FAT preservation validation found an unexpected tree change."); }
        foreach (PreservedEntry original in expected.Values)
        {
            token.ThrowIfCancellationRequested();
            if (!actual.TryGetValue(original.Path, out PreservedEntry? current)
                || !current.Path.Equals(original.Path, StringComparison.Ordinal)
                || !current.ShortEntry.AsSpan().SequenceEqual(original.ShortEntry)
                || current.LongEntries.Length != original.LongEntries.Length
                || current.LongEntries.Where((entry, index) => !entry.AsSpan().SequenceEqual(original.LongEntries[index])).Any()
                || !(current.SpecialEntries ?? []).AsSpan().SequenceEqual(original.SpecialEntries ?? []))
            {
                throw new ImageFormatException($"FAT entry metadata, alias, timestamps or long-name records were not preserved: {original.Path}");
            }
        }

    }

    internal void ValidateSourcePreservation(FatFileSystem source, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
    {
        ValidateRootLabel(source.CaptureRootLabel(token), token);
        Dictionary<string, PreservedEntry> expected = source.CapturePreservation(token);
        foreach (StagedWriteOperation operation in operations)
        {
            token.ThrowIfCancellationRequested();
            ImageChangeRequest request = operation.Request;
            if (request.Kind == ImageChangeKind.Delete)
            {
                foreach (string path in expected.Keys.Where(path => path.Equals(request.Path, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(request.Path + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    expected.Remove(path);
                }
            }
            else if (request.Kind == ImageChangeKind.Rename && expected.TryGetValue(request.Path, out PreservedEntry? original))
            {
                string renamed = Parent(original.Path) + "/" + request.NewName;
                bool caseOnly = original.Path[(original.Path.LastIndexOf('/') + 1)..]
                    .Equals(request.NewName, StringComparison.OrdinalIgnoreCase);
                foreach (string oldPath in expected.Keys.Where(path => path.Equals(original.Path, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(original.Path + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    PreservedEntry previous = expected[oldPath];
                    expected.Remove(oldPath);
                    string newPath = renamed + previous.Path[original.Path.Length..];
                    bool renamedEntry = oldPath.Equals(original.Path, StringComparison.OrdinalIgnoreCase);
                    expected.Add(newPath, previous with
                    {
                        Path = newPath,
                        Renamed = previous.Renamed || renamedEntry,
                        AliasMayChange = previous.AliasMayChange || renamedEntry && !caseOnly
                    });
                }
            }
            else if (request.Kind == ImageChangeKind.ReplaceFile && expected.TryGetValue(request.Path, out PreservedEntry? replaced))
            {
                expected[request.Path] = replaced with { Replaced = true };
            }
        }
        Dictionary<string, PreservedEntry> actual = CapturePreservation(token);
        foreach (PreservedEntry original in expected.Values)
        {
            token.ThrowIfCancellationRequested();
            if (!actual.TryGetValue(original.Path, out PreservedEntry? current)
                || !current.Path.Equals(original.Path, StringComparison.Ordinal))
            {
                throw new ImageFormatException("An original FAT entry or its exact spelling was lost.");
            }
            byte[] shortEntry = original.ShortEntry.ToArray();
            if (original.Renamed)
            {
                if (original.AliasMayChange) { current.ShortEntry.AsSpan(0, 11).CopyTo(shortEntry); }
                if (original.AliasMayChange || current.LongEntries.Length == 0) { shortEntry[12] = current.ShortEntry[12]; }
            }
            if (original.Replaced)
            {
                if (_bits == 32) { current.ShortEntry.AsSpan(20, 2).CopyTo(shortEntry.AsSpan(20)); }
                current.ShortEntry.AsSpan(26, 6).CopyTo(shortEntry.AsSpan(26));
            }
            if (!shortEntry.AsSpan().SequenceEqual(current.ShortEntry)
                || !original.Renamed && (current.LongEntries.Length != original.LongEntries.Length
                    || current.LongEntries.Where((entry, index) => !entry.AsSpan().SequenceEqual(original.LongEntries[index])).Any())
                || !(current.SpecialEntries ?? []).AsSpan().SequenceEqual(original.SpecialEntries ?? []))
            {
                throw new ImageFormatException($"Reopened FAT output lost original metadata, aliases or long-name records: {original.Path}");
            }
        }
    }
}
