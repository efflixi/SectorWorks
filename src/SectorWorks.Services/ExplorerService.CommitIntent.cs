using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Storage;

namespace SectorWorks.Services;

public sealed partial class ExplorerService
{
    private static CommitIntent CaptureCommitIntent(EditingState state, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("SectorWorksCommitIntent/v1\0"u8);
        var preview = new List<CommitIntentOperation>(8);
        Span<byte> size = stackalloc byte[4];
        for (int index = 0; index < state.Position; index++)
        {
            token.ThrowIfCancellationRequested();
            var request = state.Operations[index].Request;
            StagedContent? content = state.OperationContents[index];
            var operation = new CommitIntentOperation(request.Kind, request.VolumeId, request.Path,
                request.NewName, content?.Length ?? 0, content?.Sha256);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(operation);
            BinaryPrimitives.WriteInt32LittleEndian(size, bytes.Length);
            hash.AppendData(size);
            hash.AppendData(bytes);
            if (preview.Count < 8) { preview.Add(operation); }
        }
        return new(state.Position, Convert.ToHexString(hash.GetHashAndReset()), preview.ToArray());
    }
}
