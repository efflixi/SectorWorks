using System.Security.Cryptography;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.ServicesTests.EditingTests;

namespace SectorWorks.ServicesTests;

public sealed class CommitEligibilityTests
{
    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    [Fact]
    public async Task SaveAsQualificationDoesNotImplicitlyGrantCommitToAnUnknownWriter()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string original = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new SaveOnlyWriter(), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.True(info.Capabilities.Modify.Available);
        Assert.True(info.Capabilities.SaveAs.Available);
        Assert.False(info.Capabilities.CommitInPlace.Available);
        Assert.False(Assert.Single(info.Volumes).Capabilities!.CommitInPlace.Available);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/PENDING"));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.CommitAsync(info.Id));
        Assert.Equal(original, Hash(source));
        Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        Assert.Empty(CommitRecovery.Discover(work.Path));
        await service.SaveAsAsync(info.Id, Path.Combine(work.Path, "saved.img"));
        Assert.Equal(original, Hash(source));
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("ntfs.img")]
    public async Task ReadOnlyHostSourceKeepsSaveAsAvailableAndDisablesCommit(string fixture)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        string original = Hash(source);
        FileAttributes attributes = File.GetAttributes(source);
        File.SetAttributes(source, attributes | FileAttributes.ReadOnly);
        try
        {
            using var service = new ExplorerService(stagingDirectory: work.Path);
            WorkspaceInfo info = await service.OpenAsync(source);
            Assert.True(info.Capabilities.SaveAs.Available);
            Assert.False(info.Capabilities.CommitInPlace.Available);
            Assert.Contains("read-only", info.Capabilities.CommitInPlace.Reason);
            await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/NEW"));
            await service.SaveAsAsync(info.Id, Path.Combine(work.Path, "saved.img"));
            Assert.Equal(original, Hash(source));
            Assert.True((File.GetAttributes(source) & FileAttributes.ReadOnly) != 0);
        }
        finally { File.SetAttributes(source, attributes); }
    }

    private sealed class SaveOnlyWriter : IStagedImageWriter
    {
        private readonly DiskImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public Capability QualifyVolume(IImageSession image, string volumeId) => inner.QualifyVolume(image, volumeId);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) => inner.ValidateCapacity(image, operations, token);
        public void Apply(string path, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) => inner.Apply(path, operations, token);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) => inner.ValidateOutput(source, output, operations, token);
    }
}
