using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.Services;

public sealed partial class ExplorerService
{
    public Task<Stream> OpenAudioTrackAsync(Guid workspaceId, int trackNumber, CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "OpenAudioTrack", lease => lease.Image is ICdImageSession disc
            ? disc.OpenAudioTrack(trackNumber) : throw new NotSupportedException("This image has no CD audio tracks."),
            cancellationToken);

    public Task<PreviewResult> ReadTrackHexAsync(Guid workspaceId, int trackNumber, long offset,
        CancellationToken cancellationToken = default) =>
        RunAsync(workspaceId, "ReadTrackHex", lease =>
        {
            if (lease.Image is not ICdImageSession disc) { throw new NotSupportedException("This image has no CD tracks."); }
            using Stream input = disc.OpenRawTrack(trackNumber);
            ValidateHexOffset(offset, input.Length);
            input.Position = offset;
            byte[] bytes = new byte[checked((int)Math.Min(4096, input.Length - offset))];
            input.ReadExactly(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            return new PreviewResult(FormatHex(bytes, offset), offset + bytes.Length < input.Length,
                $"Track {trackNumber:D2} raw program area: {offset:N0} / {input.Length:N0} bytes.");
        }, cancellationToken);

    private static string HashSourceImage(IImageSession image, CancellationToken token) =>
        image is IImageSetRebuildSource set ? CombineMemberHashes(HashMembers(set, token))
            : HashSourceFile(image.Metadata.Path, image.Metadata.ContainerLength, token);

    private static Dictionary<string, string> HashMembers(IImageSetRebuildSource set, CancellationToken token)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ImageSetMember member in set.Members)
        {
            using Stream input = member.OpenRead();
            hashes.Add(member.RelativeName, TransferMember(input, null, member.Length, token));
        }
        return hashes;
    }

    private static string CombineMemberHashes(Dictionary<string, string> hashes)
    {
        var lines = new StringBuilder();
        foreach ((string name, string hash) in hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        { lines.Append(name).Append('\0').Append(hash).Append('\n'); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString())));
    }

    private SaveImageResult RebuildCue(SessionLease lease, IImageSetRebuildSource source, string destination,
        string directory, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        string cueName = Path.GetFileName(destination);
        SafeExtractionTarget.ValidateRelativePath(cueName);
        if (!Path.GetExtension(cueName).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        { throw new NotSupportedException("Name the rebuilt BIN/CUE set with a .cue extension."); }
        string final = Path.Combine(directory, Path.GetFileNameWithoutExtension(cueName) + ".cue-set");
        if (Directory.Exists(final) || File.Exists(final))
        { throw new IOException("The BIN/CUE destination folder already exists: " + final); }
        EditingState state = EnsureEditing(lease, token);
        state.VerifySpool();
        if (state.Position == 0) { throw new InvalidOperationException("Stage changes before rebuilding a BIN/CUE set."); }
        if (storageSpace.AvailableBytes(directory) < lease.Image.Metadata.ContainerLength)
        { throw new IOException("Insufficient destination space for the complete BIN/CUE set."); }

        var plans = state.Operations.Take(state.Position).GroupBy(o => o.Request.VolumeId, StringComparer.Ordinal)
            .ToDictionary(group => source.MemberName(group.Key), group => (Volume: group.Key,
                Plan: source.PlanTrack(group.Key, group.ToArray(), token),
                Expected: Fingerprint(GetVolume(lease, group.Key).FileSystem, token)), StringComparer.Ordinal);
        long scratch = plans.Values.Max(x => x.Plan.AdditionalScratchBytes);
        if (scratch > 0 && storageSpace.AvailableBytes(directory) < checked(lease.Image.Metadata.ContainerLength + scratch))
        { throw new IOException("Insufficient destination space for the BIN/CUE set and cooked track scratch."); }
        Dictionary<string, string> baseline = HashMembers(source, token);
        if (CombineMemberHashes(baseline) != lease.SourceHash)
        { throw new IOException("A CUE source member changed; rebuild was aborted. Reopen the set."); }

        string temporary = Path.Combine(directory, ".sectorworks-cue-" + Guid.NewGuid().ToString("N"));
        WorkspaceDataFiles.CreatePrivateDirectory(temporary);
        try
        {
            using (var root = new SafeExtractionTarget(temporary))
            {
                foreach (ImageSetMember member in source.Members)
                {
                    token.ThrowIfCancellationRequested();
                    string name = member.RelativeName == source.CatalogName ? cueName : member.RelativeName;
                    using ExtractionFile output = root.CreateFile(name);
                    progress?.Report(new("Writing BIN/CUE member", name, 0, 0));
                    if (plans.TryGetValue(member.RelativeName, out var rebuild))
                    {
                        rebuild.Plan.Write(output.Stream, progress, token, temporary);
                        if (output.Stream.Length != member.Length) { throw new InvalidDataException("The rebuilt track changed length."); }
                        if (rebuild.Plan.MemberHashes is { } written && written.Source != baseline[member.RelativeName])
                        { throw new IOException("A CUE member changed while rebuilding: " + member.RelativeName); }
                    }
                    else
                    {
                        using Stream input = member.OpenRead();
                        if (TransferMember(input, output.Stream, member.Length, token) != baseline[member.RelativeName])
                        { throw new IOException("A CUE member changed while copying: " + member.RelativeName); }
                    }
                    output.Complete();
                }
            }

            progress?.Report(new("Reopening and validating rebuilt BIN/CUE set", cueName, 0, 0));
            using (IImageSession reopened = opener.OpenReadOnly(Path.Combine(temporary, cueName), token))
            {
                if (reopened is not IImageSetRebuildSource output || output.Members.Count != source.Members.Count)
                { throw new InvalidDataException("The rebuilt CUE lost an expected member."); }
                Dictionary<string, string> hashes = HashMembers(output, token);
                foreach (ImageSetMember member in source.Members)
                {
                    if (plans.TryGetValue(member.RelativeName, out var rebuild))
                    {
                        if (rebuild.Plan.MemberHashes is { } written
                            && (hashes.GetValueOrDefault(member.RelativeName) != written.Output || !output.QualifyRebuild(rebuild.Volume).Available))
                        { throw new InvalidDataException("Reopened raw CUE member failed exact byte or sector-format validation."); }
                        IReadOnlyFileSystem fs = reopened.Volumes.Single(v => v.Id == rebuild.Volume).FileSystem;
                        rebuild.Plan.Validate(fs, token);
                        var actual = Fingerprint(fs, token);
                        if (actual.Count != rebuild.Expected.Count
                            || rebuild.Expected.Any(row => !actual.TryGetValue(row.Key, out string? value) || value != row.Value))
                        { throw new InvalidDataException("The rebuilt CUE track does not match staged contents."); }
                    }
                    else
                    {
                        string name = member.RelativeName == source.CatalogName ? cueName : member.RelativeName;
                        if (!hashes.TryGetValue(name, out string? hash) || baseline[member.RelativeName] != hash)
                        { throw new InvalidDataException("An untouched CUE member changed: " + name); }
                    }
                }
            }

            VerifySource(lease, token);
            token.ThrowIfCancellationRequested();
            int count = plans.Values.Sum(x => state.Current.Volumes[x.Volume].Values.Count(n => !n.Entry.IsDirectory));
            string target = Path.Combine(final, cueName);
            AtomicOutputDirectory.Publish(temporary, final);
            log.Write("CueRebuildValidated", target, $"Validated {count} files and the complete CUE set; source untouched.");
            return new(target, lease.Image.Metadata.ContainerLength, count,
                "Rebuilt and validated a new BIN/CUE folder. CUE layout, track lengths and untouched files were preserved. Source and staged changes retained.");
        }
        catch (Exception error)
        {
            try { Directory.Delete(temporary, true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                throw new IOException("The BIN/CUE rebuild failed; its unpublished staging folder could not be removed: " + temporary,
                    new AggregateException(error, cleanup));
            }
            throw;
        }
    }

    private static string TransferMember(Stream input, Stream? output, long length, CancellationToken token)
    {
        if (input.Length != length) { throw new IOException("CUE source member length changed."); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] bytes = new byte[65536];
        long remaining = length;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            int count = checked((int)Math.Min(remaining, bytes.Length));
            input.ReadExactly(bytes.AsSpan(0, count));
            hash.AppendData(bytes.AsSpan(0, count));
            output?.Write(bytes, 0, count);
            remaining -= count;
        }
        if (input.ReadByte() != -1 || input.Length != length) { throw new IOException("CUE source member length changed."); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
