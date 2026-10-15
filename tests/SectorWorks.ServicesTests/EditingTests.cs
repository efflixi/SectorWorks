using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SectorWorks.Core;
using SectorWorks.DependencyProbe;
using SectorWorks.Services;
using SectorWorks.Storage;

namespace SectorWorks.ServicesTests;

public sealed class EditingTests
{
    [Theory]
    [InlineData("fat12.img", "FAT12")]
    [InlineData("fat16.img", "FAT16")]
    [InlineData("fat32.img", "FAT32")]
    public async Task EveryAdvertisedRawFatCombinationStagesAllOperationsAndValidatesSave(string fixture, string name)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        string originalHash = Hash(source);
        byte[] originalBoot = ReadBoot(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = Assert.Single(info.Volumes).Id;
        Assert.Equal(name, info.Volumes[0].FileSystem);
        Assert.True(info.Capabilities.Modify.Available, info.Capabilities.Modify.Reason);
        Assert.True(info.Volumes[0].Capabilities!.SaveAs.Available);
        Assert.False(info.Capabilities.CommitInPlace.Available);
        Assert.False(info.Capabilities.EditMetadata.Available);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/NEW"));
        string import = work.File("input.txt", "overlay original\n");
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/NEW/ADDED.TXT", import));
        File.WriteAllText(import, "host changed after staging");
        Assert.Equal("overlay original\n", (await service.PreviewAsync(info.Id, volume, "/NEW/ADDED.TXT")).Text);
        import = work.File("replacement.txt", "replacement content\n");
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume, "/NEW/ADDED.TXT", import));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/NEW", NewName: "RENAMED"));
        Assert.Equal("replacement content\n", (await service.PreviewAsync(info.Id, volume, "/RENAMED/ADDED.TXT")).Text);
        Assert.Contains("72 65 70 6C", (await service.ReadHexAsync(info.Id, volume, "/RENAMED/ADDED.TXT", 0)).Text, StringComparison.Ordinal);
        SearchHit hit = Assert.Single(await service.SearchAsync(info.Id, "ADDED"));
        Assert.True(hit.Entry.IsChanged);
        Assert.Equal("/RENAMED/ADDED.TXT", hit.Entry.Path);
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/DELETE.TXT", import));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/DELETE.TXT"));
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, volume, "/"), e => e.Path == "/DELETE.TXT");
        var extraction = Path.Combine(work.Path, "extraction");
        Directory.CreateDirectory(extraction);
        ExtractionResult extracted = await service.ExtractAsync(info.Id, volume, [hit.Entry], extraction);
        Assert.Empty(extracted.Failures);
        Assert.Equal("replacement content\n", File.ReadAllText(Path.Combine(extraction, "ADDED.TXT")));
        Assert.Equal(originalHash, Hash(source));
        string output = Path.Combine(work.Path, "saved.img");
        SaveImageResult result = await service.SaveAsAsync(info.Id, output);
        Assert.Equal(originalBoot, ReadBoot(output));
        Assert.Equal(ReadTail(source), ReadTail(output));
        Assert.True(result.FilesValidated > 0);
        Assert.Contains("SHA-256", result.ValidationSummary, StringComparison.Ordinal);
        Assert.Equal(originalHash, Hash(source));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(output);
        Assert.Equal(name, Assert.Single(reopened.Volumes).FileSystem.Name);
        using var text = new StreamReader(reopened.Volumes[0].FileSystem.OpenRead("/RENAMED/ADDED.TXT"));
        Assert.Equal("replacement content\n", text.ReadToEnd());
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
        await AssertIndependentFatToolsAsync(output, "/RENAMED", "ADDED");
    }

    private static async Task AssertIndependentFatToolsAsync(string output, string directory, string expectedName)
    {
        string? mdir = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "mdir")).FirstOrDefault(File.Exists);
        if (mdir is not null)
        {
            var start = new ProcessStartInfo(mdir) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-i", output, "::" + directory }) { start.ArgumentList.Add(argument); }
            using Process process = Process.Start(start)!;
            string listing = await process.StandardOutput.ReadToEndAsync();
            string errors = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, errors);
            Assert.Contains(expectedName, listing, StringComparison.Ordinal);
        }
        const string fsck = "/usr/sbin/fsck.fat";
        if (File.Exists(fsck))
        {
            var start = new ProcessStartInfo(fsck) { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add(output);
            using Process process = Process.Start(start)!;
            string report = await process.StandardOutput.ReadToEndAsync();
            string errors = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, report + errors);
        }
    }

    [Fact]
    public async Task UndoRedoDiscardAndBranchingKeepOrderedOverlayAndBaseConsistent()
    {
        using var work = new EditDirectory();
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        string volume = info.Volumes[0].Id;
        int originalCount = (await service.ReadDirectoryAsync(info.Id, volume, "/")).Count;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/ONE"));
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/TWO"));
        EditingSnapshot undo = await service.UndoAsync(info.Id);
        Assert.Single(undo.Changes);
        Assert.True(undo.CanRedo);
        Assert.DoesNotContain(await service.ReadDirectoryAsync(info.Id, volume, "/"), e => e.Name == "TWO");
        EditingSnapshot redo = await service.RedoAsync(info.Id);
        Assert.Equal(2, redo.Changes.Count);
        await service.UndoAsync(info.Id);
        EditingSnapshot branch = await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/THREE"));
        Assert.False(branch.CanRedo);
        Assert.Equal(new long[] { 1, 2 }, branch.Changes.Select(c => c.Sequence));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/ONE"));
        Assert.DoesNotContain(await service.SearchAsync(info.Id, "ONE"), h => h.Entry.Path == "/ONE");
        await service.UndoAsync(info.Id);
        Assert.Contains(await service.SearchAsync(info.Id, "ONE"), h => h.Entry.Path == "/ONE");
        EditingSnapshot discarded = await service.DiscardAsync(info.Id);
        Assert.False(discarded.IsModified);
        Assert.False(discarded.CanUndo);
        Assert.False(discarded.CanRedo);
        Assert.Equal(originalCount, (await service.ReadDirectoryAsync(info.Id, volume, "/")).Count);
    }

    [Fact]
    public async Task WorkspaceSpoolsAreIsolatedReadOnlyToOtherHandlesAndRemovedOnDiscardOrClose()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string staging = Path.Combine(work.Path, "managed-workspace-data");
        using var service = new ExplorerService(stagingDirectory: staging);
        WorkspaceInfo first = await service.OpenAsync(source);
        WorkspaceInfo second = await service.OpenAsync(source);
        string input = work.File("input", "immutable staged snapshot");
        await service.StageAsync(first.Id, new(ImageChangeKind.AddFile, first.Volumes[0].Id, "/FIRST", input));
        await service.StageAsync(second.Id, new(ImageChangeKind.AddFile, second.Volumes[0].Id, "/SECOND", input));
        string firstDirectory = Path.Combine(staging, first.Id.ToString("N"));
        string secondDirectory = Path.Combine(staging, second.Id.ToString("N"));
        string payload = Assert.Single(Directory.GetFiles(firstDirectory));
        Assert.Single(Directory.GetFiles(secondDirectory));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(firstDirectory));
            Assert.Equal(UnixFileMode.UserRead, File.GetUnixFileMode(payload));
        }
        Assert.True(File.GetAttributes(payload).HasFlag(FileAttributes.ReadOnly));
        Exception? denied = Record.Exception(() =>
        {
            using var unexpected = new FileStream(payload, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        });
        Assert.True(denied is IOException or UnauthorizedAccessException, "Other handles must not open staged payloads for writing.");
        await service.DiscardAsync(first.Id);
        Assert.False(Directory.Exists(firstDirectory));
        Assert.Equal("immutable staged snapshot", (await service.PreviewAsync(second.Id, second.Volumes[0].Id, "/SECOND")).Text);
        await service.CloseAsync(second.Id);
        Assert.False(Directory.Exists(secondDirectory));
        Assert.Empty(Directory.GetFileSystemEntries(staging));
    }

    [Fact]
    public async Task RepeatedUndoAndBranchingReleasesOrphanedRedoPayloads()
    {
        using var work = new EditDirectory();
        string staging = Path.Combine(work.Path, "branching-data");
        using var service = new ExplorerService(stagingDirectory: staging);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        string volume = info.Volumes[0].Id;
        string input = work.File("input", new string('x', 65536));
        string directory = Path.Combine(staging, info.Id.ToString("N"));
        string? previousPayload = null;
        for (int branch = 0; branch < 16; branch++)
        {
            if (branch != 0) { await service.UndoAsync(info.Id); }
            EditingSnapshot snapshot = await service.StageAsync(info.Id,
                new(ImageChangeKind.AddFile, volume, $"/F{branch:D7}", input));
            Assert.Single(snapshot.Changes);
            Assert.False(snapshot.CanRedo);
            string payload = Assert.Single(Directory.GetFiles(directory));
            if (previousPayload is not null) { Assert.False(File.Exists(previousPayload)); }
            Assert.Equal(65536, new FileInfo(payload).Length);
            previousPayload = payload;
        }
        await service.CloseAsync(info.Id);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task UnrecognizedResidualSpoolFilesBlockFurtherStagingAndAreNotDeleted()
    {
        using var work = new EditDirectory();
        string staging = Path.Combine(work.Path, "residual-data");
        using var service = new ExplorerService(stagingDirectory: staging);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        string volume = info.Volumes[0].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/FILE", work.File("input", "payload")));
        string directory = Path.Combine(staging, info.Id.ToString("N"));
        string payload = Assert.Single(Directory.GetFiles(directory));
        string residual = Path.Combine(directory, "unrecognized");
        File.WriteAllText(residual, "preserve me");
        IOException error = await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.CreateDirectory, volume, "/BLOCKED")));
        Assert.Contains(directory, error.Message, StringComparison.Ordinal);
        Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        await service.DiscardAsync(info.Id);
        Assert.False(File.Exists(payload));
        Assert.Equal("preserve me", File.ReadAllText(residual));
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.CreateDirectory, volume, "/BLOCKED")));
        File.Delete(residual);
    }

    [Theory]
    [InlineData("/../escape")]
    [InlineData("/bad?.txt")]
    [InlineData("/bad:stream")]
    [InlineData("/CON.txt")]
    [InlineData("/trailing.")]
    [InlineData("/trailing ")]
    [InlineData("/decomposed-e\u0301")]
    [InlineData("/surrogate-\ud83d\ude00")]
    [InlineData("/ leading")]
    [InlineData("/nul.txt")]
    [InlineData("/CLOCK$")]
    [InlineData("/")]
    public async Task InvalidNamesAreRejectedWithoutPendingChanges(string path)
    {
        using var work = new EditDirectory();
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, path)));
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
    }

    [Fact]
    public async Task ConflictsWrongVolumeMissingParentMetadataAndCapacityFailAtomically()
    {
        using var work = new EditDirectory();
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        string volume = info.Volumes[0].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/FIRST"));
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/SECOND"));
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/FIRST")));
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/FIRST", NewName: "SECOND")));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/MISSING/CHILD")));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id, new(ImageChangeKind.Metadata, volume, "/FIRST")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StageAsync(info.Id, new(ImageChangeKind.Delete, "foreign", "/FIRST")));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume, "/FIRST", work.File("input", "data"))));
        string huge = Path.Combine(work.Path, "huge");
        using (FileStream file = File.Create(huge)) { file.SetLength(info.Metadata.VirtualLength); }
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/HUGE.BIN", huge)));
        Assert.Equal(2, (await service.GetEditingSnapshotAsync(info.Id)).Changes.Count);
    }

    [Theory]
    [InlineData("Copying image")]
    [InlineData("Applying staged changes")]
    [InlineData("Validating tree and SHA-256")]
    public async Task SaveNeverOverwritesSourceExistingOutputOrDirectoryAndCancellationCleansUp(string cancelStage)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string hash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/CHANGED"));
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, source));
        string existing = work.File("exists.img", "keep me");
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, existing));
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, work.Path));
        Assert.Equal("keep me", File.ReadAllText(existing));
        string output = Path.Combine(work.Path, "cancelled.img");
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(p => { if (p.Stage == cancelStage) { cts.Cancel(); } });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsAsync(info.Id, output, progress, cts.Token));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
        Assert.Equal(hash, Hash(source));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
    }

    [Fact]
    public async Task DestinationCreatedDuringSaveIsNeverOverwritten()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string hash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string output = Path.Combine(work.Path, "raced.img");
        var progress = new InlineProgress(p =>
        {
            if (p.Stage == "Validating tree and SHA-256") { File.WriteAllText(output, "concurrent owner"); }
        });
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, output, progress));
        Assert.Equal("concurrent owner", File.ReadAllText(output));
        Assert.Equal(hash, Hash(source));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("Copying image")]
    [InlineData("Validating tree and SHA-256")]
    public async Task ExternalSourceMutationCannotSilentlyPublishAStagedSave(string mutationStage)
    {
        // Windows sharing excludes an independent writer; exercise non-cooperating POSIX writers when Python is present.
        if (OperatingSystem.IsWindows()) { return; }
        string? python = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "python3")).FirstOrDefault(File.Exists);
        if (python is null) { return; }
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/STAGED"));
        bool mutated = false;
        void Mutate()
        {
            if (mutated) { return; }
            mutated = true;
            var start = new ProcessStartInfo(python) { RedirectStandardError = true };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import sys; f=open(sys.argv[1],'r+b'); f.seek(512000); f.write(b'X'); f.flush(); f.close()");
            start.ArgumentList.Add(source);
            using Process process = Process.Start(start)!;
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }
        if (mutationStage == "before") { Mutate(); }
        var progress = new InlineProgress(p => { if (p.Stage == mutationStage) { Mutate(); } });
        string output = Path.Combine(work.Path, "mutated.img");
        IOException failure = await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, output, progress));
        Assert.Contains("Source changed", failure.Message, StringComparison.Ordinal);
        Assert.NotEqual(baseline, Hash(source));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
    }

    [Fact]
    public async Task FixedFatRootDirectoryCapacityIsCheckedBeforeSave()
    {
        using var work = new EditDirectory();
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        string empty = work.File("empty", "");
        string volume = info.Volumes[0].Id;
        // Standard 1,474,560-byte FAT12 has 224 root slots: DOCS, SENTINEL and a volume label already occupy three.
        for (int i = 0; i < 221; i++)
        {
            await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, $"/F{i:D7}", empty));
        }
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/OVERFLOW", empty)));
        Assert.Equal(221, (await service.GetEditingSnapshotAsync(info.Id)).Changes.Count);
    }

    [Fact]
    public async Task ReadOnlyFilesDirectoriesAndAncestorsRejectChangesBeforeStaging()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        MutateRootEntry(source, "SENTINEL.TXT", (bytes, offset) => bytes[offset + 11] |= 1);
        MutateRootEntry(source, "DOCS", (bytes, offset) => bytes[offset + 11] |= 1);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.True(info.Capabilities.Modify.Available);
        string volume = info.Volumes[0].Id;
        string input = work.File("input", "replacement");
        foreach (ImageChangeRequest request in new ImageChangeRequest[]
        {
            new(ImageChangeKind.ReplaceFile, volume, "/SENTINEL.TXT", input),
            new(ImageChangeKind.Delete, volume, "/SENTINEL.TXT"),
            new(ImageChangeKind.Rename, volume, "/SENTINEL.TXT", NewName: "NEW.TXT"),
            new(ImageChangeKind.Delete, volume, "/DOCS"),
            new(ImageChangeKind.AddFile, volume, "/DOCS/NEW.TXT", input),
            new(ImageChangeKind.CreateDirectory, volume, "/DOCS/CHILD")
        })
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id, request));
        }
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllocationConflictsAndMirrorDisagreementDisableSourceWrites(bool crossLinked)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        if (crossLinked)
        {
            ushort directoryCluster = 0;
            MutateRootEntry(source, "DOCS", (bytes, offset) =>
                directoryCluster = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 26)));
            MutateRootEntry(source, "SENTINEL.TXT", (bytes, offset) =>
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 26), directoryCluster));
        }
        else
        {
            using var file = new FileStream(source, FileMode.Open, FileAccess.Write);
            file.Position = 512 + 150;
            file.WriteByte(1);
        }
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.False(info.Capabilities.Modify.Available);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id,
            new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/REJECTED")));
        Assert.False((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Equal(baseline, Hash(source));
    }

    [Fact]
    public async Task RenamedDirectoryDescendantsRemainStableForReplaceDeleteUndoAndSave()
    {
        using var work = new EditDirectory();
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(work.Golden("fat12.img"));
        string volume = info.Volumes[0].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/FIRST"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/FIRST/CHILD", work.File("input", "first")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/FIRST", NewName: "SECOND"));
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume, "/SECOND/CHILD", work.File("new", "replaced")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/SECOND/CHILD"));
        await service.UndoAsync(info.Id);
        Assert.Equal("replaced", (await service.PreviewAsync(info.Id, volume, "/SECOND/CHILD")).Text);
        await service.RedoAsync(info.Id);
        Assert.Empty(await service.ReadDirectoryAsync(info.Id, volume, "/SECOND"));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/SECOND"));
        string output = Path.Combine(work.Path, "saved.img");
        await service.SaveAsAsync(info.Id, output);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(output);
        Assert.DoesNotContain(reopened.Volumes[0].FileSystem.ReadDirectory("/"), entry => entry.Name is "FIRST" or "SECOND");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InjectedWriterFailureOrIncorrectContentCannotPublishOutput(bool corrupt)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string hash = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new FailingWriter(corrupt), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, info.Volumes[0].Id, "/ADDED.TXT", work.File("input", "expected")));
        string output = Path.Combine(work.Path, "failed.img");
        if (corrupt)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsAsync(info.Id, output));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => service.SaveAsAsync(info.Id, output));
        }
        Assert.False(File.Exists(output));
        Assert.Equal(hash, Hash(source));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
    }

    [Fact]
    public async Task LostClustersAreRejectedEvenWhenEveryFileHashIsCorrect()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string hash = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new LostClusterWriter(), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string output = Path.Combine(work.Path, "lost.img");
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsAsync(info.Id, output));
        Assert.Contains("structural validation", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Equal(hash, Hash(source));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
    }

    [Fact]
    public async Task InvalidChangedFileDatesCannotPublishEvenWhenContentsAndAllocationMatch()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string baseline = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new InvalidDateWriter(), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, info.Volumes[0].Id, "/ADDED.TXT", work.File("input", "unchanged bytes")));
        string output = Path.Combine(work.Path, "invalid-date.img");
        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsAsync(info.Id, output));
        Assert.Contains("structural validation", failure.Message, StringComparison.Ordinal);
        Assert.Contains("timestamps", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Equal(baseline, Hash(source));
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(24)]
    public async Task ValidButChangedUntouchedMetadataCannotPublishWithMatchingTreeAndHashes(int field)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string baseline = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new MetadataCorruptingWriter(field), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/NEW"));
        string output = Path.Combine(work.Path, "metadata-corrupt.img");
        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsAsync(info.Id, output));
        Assert.Contains("metadata/preservation validation", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output));
        Assert.Equal(baseline, Hash(source));
        Assert.Single((await service.GetEditingSnapshotAsync(info.Id)).Changes);
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task IncompleteReadGoldenMetadataDoesNotInheritWriteQualification(string name)
    {
        using var work = new EditDirectory();
        string source = work.Golden(name, false);
        string hash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.False(info.Capabilities.Modify.Available);
        Assert.Contains("metadata", info.Capabilities.Modify.Reason, StringComparison.Ordinal);
        Assert.NotEmpty(await service.ReadDirectoryAsync(info.Id, info.Volumes[0].Id, "/"));
        Assert.Equal(hash, Hash(source));
    }

    [Theory]
    [InlineData(488)]
    [InlineData(492)]
    public async Task OutOfRangeFat32AllocationHintsDisableWritingBeforeChanges(int fieldOffset)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat32.img");
        using (var file = new FileStream(source, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] boot = new byte[512];
            file.ReadExactly(boot);
            int info = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(48));
            int backup = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(50));
            byte[] invalid = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(invalid, uint.MaxValue - 1);
            foreach (int sector in new[] { info, backup + info })
            {
                file.Position = sector * 512L + fieldOffset;
                file.Write(invalid);
            }
        }
        string baseline = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo image = await service.OpenAsync(source);
        Assert.False(image.Capabilities.Modify.Available);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(image.Id,
            new(ImageChangeKind.CreateDirectory, image.Volumes[0].Id, "/REJECTED")));
        Assert.False((await service.GetEditingSnapshotAsync(image.Id)).IsModified);
        Assert.Equal(baseline, Hash(source));
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task BaseFilesRenameReplaceDeleteAndEmptyFileRoundTrip(string fixture)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        string hash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = info.Volumes[0].Id;
        string original = (await service.PreviewAsync(info.Id, volume, "/SENTINEL.TXT")).Text;
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/SENTINEL.TXT", NewName: "RENAMED.TXT"));
        Assert.Equal(original, (await service.PreviewAsync(info.Id, volume, "/RENAMED.TXT")).Text);
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume, "/RENAMED.TXT", work.File("new", "changed base")));
        await service.UndoAsync(info.Id);
        Assert.Equal(original, (await service.PreviewAsync(info.Id, volume, "/RENAMED.TXT")).Text);
        await service.RedoAsync(info.Id);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/EMPTYDIR"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/EMPTYDIR/EMPTY.TXT", work.File("empty", "")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/DOCS"));
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/REMOVE"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/REMOVE/CHILD", work.File("transient", "recursive deletion")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/REMOVE"));
        string output = Path.Combine(work.Path, "saved.img");
        await service.SaveAsAsync(info.Id, output);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(output);
        IReadOnlyFileSystem fs = reopened.Volumes[0].FileSystem;
        Assert.DoesNotContain(fs.ReadDirectory("/"), e => e.Path is "/DOCS" or "/SENTINEL.TXT");
        using var text = new StreamReader(fs.OpenRead("/RENAMED.TXT"));
        Assert.Equal("changed base", text.ReadToEnd());
        using Stream empty = fs.OpenRead("/EMPTYDIR/EMPTY.TXT");
        Assert.Equal(0, empty.Length);
        Assert.Equal(hash, Hash(source));
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task RenamesPreserveModificationDatesAndNewDirectorySpecialEntriesHaveValidDates(string fixture)
    {
        using var work = new EditDirectory();
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(work.Golden(fixture));
        string volume = info.Volumes[0].Id;
        DateTime? original = (await service.ReadDirectoryAsync(info.Id, volume, "/")).Single(entry => entry.Name == "SENTINEL.TXT").ModifiedUtc;
        Assert.NotNull(original);
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/SENTINEL.TXT", NewName: "RENAMED.TXT"));
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/EMPTY"));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/EMPTY", NewName: "MOVED"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/MOVED/ADDED.TXT", work.File("input", "date regression")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/MOVED/ADDED.TXT", NewName: "RENAMED.TXT"));
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/EMPTY2"));
        string output = Path.Combine(work.Path, "dates.img");
        await service.SaveAsAsync(info.Id, output);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(output);
        IReadOnlyFileSystem fs = reopened.Volumes[0].FileSystem;
        Assert.Equal(original, fs.ReadDirectory("/").Single(entry => entry.Name == "RENAMED.TXT").ModifiedUtc);
        Assert.NotNull(fs.ReadDirectory("/MOVED").Single().ModifiedUtc);
        Assert.NotNull(fs.ReadDirectory("/").Single(entry => entry.Name == "EMPTY2").ModifiedUtc);
        AssertSpecialDirectoryDates(output, "MOVED");
        AssertSpecialDirectoryDates(output, "EMPTY2");
    }

    [Theory]
    [InlineData("ntfs.img")]
    [InlineData("dynamic.vhd")]
    [InlineData("dynamic.vhdx")]
    [InlineData("joliet.iso")]
    [InlineData("mbr.img")]
    [InlineData("gpt.img")]
    public async Task UnqualifiedStacksRemainReadOnly(string fixture)
    {
        using var work = new EditDirectory();
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(work.Golden(fixture));
        Assert.False(info.Capabilities.Modify.Available);
        Assert.False(info.Capabilities.SaveAs.Available);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/NO")));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.SaveAsAsync(info.Id, Path.Combine(work.Path, "no.img")));
    }

    [Fact]
    public async Task MissingExtendedBpbRemainsReadOnlyDespiteFatReadSupport()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        using (var file = new FileStream(source, FileMode.Open, FileAccess.Write)) { file.Position = 38; file.WriteByte(0); }
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.Equal("FAT12", info.Volumes[0].FileSystem);
        Assert.False(info.Capabilities.Modify.Available);
        Assert.NotEmpty(await service.ReadDirectoryAsync(info.Id, info.Volumes[0].Id, "/"));
    }

    [Fact]
    public async Task IndependentlyAuthoredLongNamesQualifyAndPreserveAliasesWhenMtoolsAvailable()
    {
        string? mcopy = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "mcopy")).FirstOrDefault(File.Exists);
        if (mcopy is null) { return; }
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        string input = work.File("input", "long filename content");
        var start = new ProcessStartInfo(mcopy) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-i", source, input, "::/Long file.txt" }) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start)!;
        string errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, errors);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.True(info.Capabilities.Modify.Available, info.Capabilities.Modify.Reason);
        Assert.Equal("long filename content", (await service.PreviewAsync(info.Id, info.Volumes[0].Id, "/Long file.txt")).Text);
        FileEntry before = (await service.ReadDirectoryAsync(info.Id, info.Volumes[0].Id, "/")).Single(entry => entry.Name == "Long file.txt");
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/Mixed directory"));
        string output = Path.Combine(work.Path, "long-name.img");
        await service.SaveAsAsync(info.Id, output);
        using IImageSession saved = new ImageOpener().OpenReadOnly(output);
        FileEntry after = saved.Volumes[0].FileSystem.ReadDirectory("/").Single(entry => entry.Name == before.Name);
        Assert.Equal(before.ShortName, after.ShortName);
        Assert.Equal(before.ModifiedUtc, after.ModifiedUtc);
        await AssertIndependentFatToolsAsync(output, "/", "Long file.txt");
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task LowercaseBaseTreeAndNewNamesPreserveCaseAcrossAllEditingOperations(string fixture)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        EditDirectory.AddRootDirectory(source, "EMPTY");
        EditDirectory.SetRootCase(source, "SENTINEL.TXT", 0x18);
        EditDirectory.SetRootCase(source, "EMPTY", 0x08);
        string originalHash = Hash(source);
        byte[] originalEntry = EditDirectory.RootEntry(source, "SENTINEL.TXT");
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = info.Volumes[0].Id;
        Assert.True(info.Capabilities.Modify.Available, info.Capabilities.Modify.Reason);
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume, "/"), entry => entry.Name == "sentinel.txt");
        string input = work.File("input", "initial contents");
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/EmPtY/child"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/EMPTY/ChIlD/UPPER.txt", input));
        Assert.Equal("/empty/child/UPPER.txt", Assert.Single(await service.ReadDirectoryAsync(info.Id, volume, "/EMPTY/CHILD")).Path);
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/empty/child/upper.TXT", input)));
        string replacement = work.File("replacement", "replacement contents");
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume, "/EMPTY/CHILD/uPpEr.TxT", replacement));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/EMPTY/CHILD", NewName: "renamed"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/empty/RENAMED/delete.txt", input));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/EMPTY/renamed/DeLeTe.TxT"));
        Assert.Equal("replacement contents", (await service.PreviewAsync(info.Id, volume, "/EMPTY/RENAMED/upper.TXT")).Text);
        foreach (string name in new[] { "CAPS.TXT", "lower.TXT", "UPPER.txt", "both.txt" })
        {
            await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/" + name, input));
        }
        string output = Path.Combine(work.Path, "case-preserved.img");
        await service.SaveAsAsync(info.Id, output);
        Assert.Equal(originalHash, Hash(source));
        Assert.Equal(originalEntry[12], EditDirectory.RootEntry(output, "SENTINEL.TXT")[12]);
        Assert.Equal(originalEntry.AsSpan(22, 10).ToArray(), EditDirectory.RootEntry(output, "SENTINEL.TXT").AsSpan(22, 10).ToArray());
        Assert.Equal(0x00, EditDirectory.RootEntry(output, "CAPS.TXT")[12]);
        Assert.Equal(0x08, EditDirectory.RootEntry(output, "LOWER.TXT")[12]);
        Assert.Equal(0x10, EditDirectory.RootEntry(output, "UPPER.TXT")[12]);
        Assert.Equal(0x18, EditDirectory.RootEntry(output, "BOTH.TXT")[12]);
        Assert.Equal(0x08, EditDirectory.RootEntry(output, "EMPTY")[12]);
        using IImageSession reopened = new ImageOpener().OpenReadOnly(output);
        Assert.True(new RawFatImageWriter().Qualify(reopened).Available);
        var fs = reopened.Volumes[0].FileSystem;
        Assert.Contains(fs.ReadDirectory("/"), entry => entry.Name == "sentinel.txt");
        Assert.Equal("/empty/renamed/UPPER.txt", Assert.Single(fs.ReadDirectory("/empty/renamed")).Path);
        using var reader = new StreamReader(fs.OpenRead("/EMPTY/RENAMED/upper.TXT"));
        Assert.Equal("replacement contents", reader.ReadToEnd());
        await AssertIndependentFatToolsAsync(output, "/empty/renamed", "UPPER");
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task CaseOnlyFileAndDirectoryRenamesPreserveContentDatesAndUndoRedo(string fixture)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        EditDirectory.AddRootDirectory(source, "EMPTY");
        EditDirectory.SetRootCase(source, "SENTINEL.TXT", 0x18);
        EditDirectory.SetRootCase(source, "EMPTY", 0x08);
        string originalHash = Hash(source);
        byte[] originalEntry = EditDirectory.RootEntry(source, "SENTINEL.TXT");
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = info.Volumes[0].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/EmPtY", NewName: "EMPTY"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/eMpTy/child.txt", work.File("input", "nested")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/EMPTY", NewName: "empty"));
        Assert.Equal("/empty/child.txt", Assert.Single(await service.ReadDirectoryAsync(info.Id, volume, "/EMPTY")).Path);
        await service.UndoAsync(info.Id);
        Assert.Equal("/EMPTY/child.txt", Assert.Single(await service.ReadDirectoryAsync(info.Id, volume, "/empty")).Path);
        await service.RedoAsync(info.Id);
        Assert.Equal("/empty/child.txt", Assert.Single(await service.ReadDirectoryAsync(info.Id, volume, "/EMPTY")).Path);
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/empty", NewName: "EMPTY"));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/SeNtInEl.TxT", NewName: "SENTINEL.txt"));
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume, "/"), entry => entry.Name == "SENTINEL.txt");
        await service.UndoAsync(info.Id);
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume, "/"), entry => entry.Name == "sentinel.txt");
        await service.RedoAsync(info.Id);
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/sentinel.TXT", NewName: "SENTINEL.txt")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/sentinel.TXT", NewName: "Sentinel.txt"));
        Assert.Contains(await service.ReadDirectoryAsync(info.Id, volume, "/"), entry => entry.Name == "Sentinel.txt");
        await service.UndoAsync(info.Id);
        string output = Path.Combine(work.Path, "case-renamed.img");
        await service.SaveAsAsync(info.Id, output);
        Assert.Equal(originalHash, Hash(source));
        byte[] renamed = EditDirectory.RootEntry(output, "SENTINEL.TXT");
        Assert.Equal(0x10, renamed[12]);
        Assert.Equal(originalEntry.AsSpan(22, 10).ToArray(), renamed.AsSpan(22, 10).ToArray());
        Assert.Equal(0, EditDirectory.RootEntry(output, "EMPTY")[12]);
        WorkspaceInfo saved = await service.OpenAsync(output);
        Assert.Contains(await service.ReadDirectoryAsync(saved.Id, saved.Volumes[0].Id, "/"), entry => entry.Name == "SENTINEL.txt");
        Assert.Equal("/EMPTY/child.txt", Assert.Single(await service.ReadDirectoryAsync(saved.Id, saved.Volumes[0].Id, "/empty")).Path);
        Assert.Equal("nested", (await service.PreviewAsync(saved.Id, saved.Volumes[0].Id, "/EmPtY/ChIlD.TxT")).Text);
        await AssertIndependentFatToolsAsync(output, "/EMPTY", "child");
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task ReplacingLowercaseBaseFilePreservesItsCaseFlags(string fixture)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        EditDirectory.SetRootCase(source, "SENTINEL.TXT", 0x18);
        string originalHash = Hash(source);
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, info.Volumes[0].Id, "/SeNtInEl.TxT", work.File("replacement", "lowercase replacement")));
        string output = Path.Combine(work.Path, "replaced.img");
        await service.SaveAsAsync(info.Id, output);
        Assert.Equal(0x18, EditDirectory.RootEntry(output, "SENTINEL.TXT")[12]);
        WorkspaceInfo saved = await service.OpenAsync(output);
        Assert.Equal("sentinel.txt", (await service.ReadDirectoryAsync(saved.Id, saved.Volumes[0].Id, "/")).Single(entry => !entry.IsDirectory).Name);
        Assert.Equal("lowercase replacement", (await service.PreviewAsync(saved.Id, saved.Volumes[0].Id, "/SeNtInEl.TxT")).Text);
        Assert.Equal(originalHash, Hash(source));
        await AssertIndependentFatToolsAsync(output, "/", "sentinel");
    }

    [Fact]
    public async Task LongNameRecordWithIdenticalShortAliasIsPreservedThroughEditing()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        byte[] alias = EditDirectory.RootEntry(source, "SENTINEL.TXT");
        byte checksum = 0;
        foreach (byte value in alias.AsSpan(0, 11).ToArray())
        {
            checksum = unchecked((byte)(((checksum & 1) << 7) + (checksum >> 1) + value));
        }
        byte[] boot = ReadBoot(source);
        using (var stream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite))
        {
            long rootOffset = (BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14))
                + boot[16] * BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22))) * 512L;
            byte[] root = new byte[BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17)) * 32];
            stream.Position = rootOffset;
            stream.ReadExactly(root);
            int insertOffset = checked((int)(EditDirectory.RootEntryOffset(stream, "SENTINEL.TXT") - rootOffset));
            Array.Copy(root, insertOffset, root, insertOffset + 32, root.Length - insertOffset - 32);
            byte[] lfn = new byte[32];
            lfn[0] = 0x41;
            lfn[11] = 0x0f;
            lfn[13] = checksum;
            int[] offsets = [1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30];
            const string name = "SENTINEL.TXT";
            for (int i = 0; i < offsets.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(lfn.AsSpan(offsets[i]), i < name.Length ? name[i] : (ushort)0);
            }
            lfn.CopyTo(root, insertOffset);
            stream.Position = rootOffset;
            stream.Write(root);
        }
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.True(info.Capabilities.Modify.Available, info.Capabilities.Modify.Reason);
        Assert.Equal("SENTINEL.TXT", (await service.ReadDirectoryAsync(info.Id, info.Volumes[0].Id, "/")).Single(entry => !entry.IsDirectory).Name);
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/NEW"));
        string output = Path.Combine(work.Path, "redundant-lfn.img");
        await service.SaveAsAsync(info.Id, output);
        Assert.Equal(EditDirectory.RootEntry(source, "SENTINEL.TXT"), EditDirectory.RootEntry(output, "SENTINEL.TXT"));
    }

    [Fact]
    public async Task OutputValidationRejectsLostCaseFlagsEvenWhenFileHashesMatch()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        EditDirectory.SetRootCase(source, "SENTINEL.TXT", 0x18);
        string originalHash = Hash(source);
        using var service = new ExplorerService(new ImageOpener(), writer: new CaseCorruptingWriter(), stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, info.Volumes[0].Id, "/added.txt", work.File("input", "added")));
        string output = Path.Combine(work.Path, "bad-case.img");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsAsync(info.Id, output));
        Assert.False(File.Exists(output));
        Assert.Equal(originalHash, Hash(source));
        Assert.True((await service.GetEditingSnapshotAsync(info.Id)).IsModified);
        Assert.Empty(Directory.GetFiles(work.Path, ".sectorworks-save-*"));
    }

    private sealed class CaseCorruptingWriter : IStagedImageWriter
    {
        private readonly RawFatImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateCapacity(image, operations, token);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateOutput(source, output, operations, token);
        public void Apply(string path, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            inner.Apply(path, operations, token);
            EditDirectory.SetRootCase(path, "SENTINEL.TXT", 0);
        }
    }

    [Theory]
    [InlineData(368640)]
    [InlineData(737280)]
    public async Task RetroCapacityDoesNotInheritModernFat12WriteQualification(int length)
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat12.img");
        using (var file = new FileStream(source, FileMode.Open, FileAccess.ReadWrite))
        {
            var boot = new byte[512];
            file.ReadExactly(boot);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(19), checked((ushort)(length / 512)));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(24), 9);
            file.Position = 0;
            file.Write(boot);
            file.SetLength(length);
        }
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        Assert.Equal("FAT12", info.Volumes[0].FileSystem);
        Assert.False(info.Capabilities.Modify.Available);
        Assert.False(info.Capabilities.SaveAs.Available);
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public async Task LongMixedCaseUnicodeNamesUseSharedStagedHistoryAndValidatedSave(string fixture)
    {
        using var work = new EditDirectory();
        string source = work.Golden(fixture);
        string hash = Hash(source);
        byte[] original = EditDirectory.RootEntry(source, "SENTINEL.TXT");
        using var service = new ExplorerService(stagingDirectory: work.Path);
        WorkspaceInfo info = await service.OpenAsync(source);
        string volume = info.Volumes[0].Id;
        await service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, volume, "/Mixed directory"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/Mixed directory/Unicode-\u03bb.txt", work.File("first", "first")));
        await service.StageAsync(info.Id, new(ImageChangeKind.ReplaceFile, volume, "/mixed DIRECTORY/UNICODE-\u039b.TXT", work.File("replacement", "second")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/Mixed directory", NewName: "Renamed directory"));
        await service.UndoAsync(info.Id);
        Assert.Equal("second", (await service.PreviewAsync(info.Id, volume, "/Mixed directory/Unicode-\u03bb.txt")).Text);
        await service.RedoAsync(info.Id);
        await service.StageAsync(info.Id, new(ImageChangeKind.Rename, volume, "/Renamed directory/Unicode-\u03bb.txt", NewName: "unicode-\u03bb.txt"));
        await service.StageAsync(info.Id, new(ImageChangeKind.AddFile, volume, "/Delete this long name.txt", work.File("transient", "remove")));
        await service.StageAsync(info.Id, new(ImageChangeKind.Delete, volume, "/delete THIS long name.txt"));
        Assert.Single(await service.SearchAsync(info.Id, "unicode-"));
        string output = Path.Combine(work.Path, "long-unicode.img");
        await service.SaveAsAsync(info.Id, output);
        using IImageSession saved = new ImageOpener().OpenReadOnly(output);
        FileEntry entry = Assert.Single(saved.Volumes[0].FileSystem.ReadDirectory("/Renamed directory"));
        Assert.Equal("unicode-\u03bb.txt", entry.Name);
        using var text = new StreamReader(saved.Volumes[0].FileSystem.OpenRead(entry.Path));
        Assert.Equal("second", text.ReadToEnd());
        Assert.Equal(hash, Hash(source));
        Assert.Equal(original, EditDirectory.RootEntry(output, "SENTINEL.TXT"));
        await AssertIndependentFatToolsAsync(output, "/", "Renamed directory");
    }

    private static string Hash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static byte[] ReadBoot(string path)
    {
        using FileStream file = File.OpenRead(path);
        byte[] boot = new byte[512];
        file.ReadExactly(boot);
        return boot;
    }

    private static byte[] ReadTail(string path)
    {
        using FileStream file = File.OpenRead(path);
        file.Position = file.Length - 64;
        byte[] bytes = new byte[64];
        file.ReadExactly(bytes);
        return bytes;
    }

    private static void MutateRootEntry(string path, string name, Action<byte[], int> mutate)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        byte[] boot = new byte[512];
        file.ReadExactly(boot);
        int reserved = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
        int sectorsPerFat = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22));
        int entries = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
        long root = (reserved + boot[16] * sectorsPerFat) * 512L;
        byte[] bytes = new byte[entries * 32];
        file.Position = root;
        file.ReadExactly(bytes);
        string[] parts = name.Split('.');
        byte[] expected = Encoding.ASCII.GetBytes(parts[0].PadRight(8) + (parts.Length == 2 ? parts[1] : "").PadRight(3));
        int slot = Enumerable.Range(0, entries).Single(index => bytes.AsSpan(index * 32, 11).SequenceEqual(expected)) * 32;
        mutate(bytes, slot);
        file.Position = root;
        file.Write(bytes);
    }

    private static void AssertSpecialDirectoryDates(string path, string name)
    {
        using FileStream file = File.OpenRead(path);
        byte[] boot = new byte[512];
        file.ReadExactly(boot);
        int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
        long fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22));
        bool fat32 = fatSectors == 0;
        if (fat32) { fatSectors = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36)); }
        long fixedRoot = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14)) + boot[16] * fatSectors;
        long data = fixedRoot + (rootEntries * 32 + 511) / 512;
        long root = fat32 ? data + (BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(44)) - 2L) * boot[13] : fixedRoot;
        byte[] entries = new byte[fat32 ? boot[13] * 512 : rootEntries * 32];
        file.Position = root * 512;
        file.ReadExactly(entries);
        byte[] expected = Encoding.ASCII.GetBytes(name.PadRight(11));
        int slot = Enumerable.Range(0, entries.Length / 32).Single(index => entries.AsSpan(index * 32, 11).SequenceEqual(expected)) * 32;
        uint cluster = BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(slot + 26));
        if (fat32) { cluster |= (uint)BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(slot + 20)) << 16; }
        file.Position = (data + (cluster - 2L) * boot[13]) * 512;
        byte[] special = new byte[64];
        file.ReadExactly(special);
        Assert.True(special.AsSpan(0, 11).SequenceEqual(".          "u8));
        Assert.True(special.AsSpan(32, 11).SequenceEqual("..         "u8));
        for (int offset = 0; offset < 64; offset += 32)
        {
            ushort date = BinaryPrimitives.ReadUInt16LittleEndian(special.AsSpan(offset + 24));
            ushort time = BinaryPrimitives.ReadUInt16LittleEndian(special.AsSpan(offset + 22));
            int year = 1980 + (date >> 9);
            int month = (date >> 5) & 15;
            Assert.InRange(month, 1, 12);
            Assert.InRange(date & 31, 1, DateTime.DaysInMonth(year, month));
            Assert.InRange(time >> 11, 0, 23);
            Assert.InRange((time >> 5) & 63, 0, 59);
            Assert.InRange((time & 31) * 2, 0, 58);
        }
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private sealed class FailingWriter(bool corrupt) : IStagedImageWriter
    {
        private readonly RawFatImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken) =>
            inner.ValidateCapacity(image, operations, cancellationToken);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateOutput(source, output, operations, token);
        public void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
        {
            if (!corrupt) { throw new IOException("Injected write failure."); }
            var changed = operations.Select(operation => operation.Request.Kind == ImageChangeKind.AddFile
                ? operation with { OpenContent = () => new MemoryStream(Encoding.UTF8.GetBytes("wrong!!!")) } : operation).ToArray();
            inner.Apply(copyPath, changed, cancellationToken);
        }
    }

    private sealed class InvalidDateWriter : IStagedImageWriter
    {
        private readonly RawFatImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken) =>
            inner.ValidateCapacity(image, operations, cancellationToken);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateOutput(source, output, operations, token);
        public void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
        {
            inner.Apply(copyPath, operations, cancellationToken);
            MutateRootEntry(copyPath, "ADDED.TXT", (bytes, offset) => bytes.AsSpan(offset + 24, 2).Clear());
        }
    }

    private sealed class LostClusterWriter : IStagedImageWriter
    {
        private readonly RawFatImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken) =>
            inner.ValidateCapacity(image, operations, cancellationToken);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateOutput(source, output, operations, token);
        public void Apply(string copyPath, IReadOnlyList<StagedWriteOperation> operations, CancellationToken cancellationToken)
        {
            inner.Apply(copyPath, operations, cancellationToken);
            using var stream = new FileStream(copyPath, FileMode.Open, FileAccess.ReadWrite);
            byte[] boot = new byte[512];
            stream.ReadExactly(boot);
            int reserved = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
            int sectorsPerFat = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22));
            for (int copy = 0; copy < boot[16]; copy++)
            {
                stream.Position = (reserved + copy * sectorsPerFat) * 512L + 150; // FAT12 cluster 100, previously free.
                int first = stream.ReadByte();
                int second = stream.ReadByte();
                Assert.Equal(0, first);
                stream.Position -= 2;
                stream.WriteByte(0xff);
                stream.WriteByte((byte)(second | 0x0f));
            }
            stream.Flush(true);
        }
    }

    private sealed class MetadataCorruptingWriter(int field) : IStagedImageWriter
    {
        private readonly RawFatImageWriter inner = new();
        public Capability Qualify(IImageSession image) => inner.Qualify(image);
        public void ValidateCapacity(IImageSession image, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateCapacity(image, operations, token);
        public void ValidateOutput(IImageSession source, IImageSession output, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token) =>
            inner.ValidateOutput(source, output, operations, token);
        public void Apply(string path, IReadOnlyList<StagedWriteOperation> operations, CancellationToken token)
        {
            inner.Apply(path, operations, token);
            MutateRootEntry(path, "SENTINEL.TXT", (bytes, offset) =>
            {
                if (field == 11) { bytes[offset + field] ^= 2; }
                else
                {
                    Span<byte> date = bytes.AsSpan(offset + field, 2);
                    ushort old = BinaryPrimitives.ReadUInt16LittleEndian(date);
                    BinaryPrimitives.WriteUInt16LittleEndian(date, old == 0x5021 ? (ushort)0x5022 : (ushort)0x5021);
                }
            });
        }
    }

    internal sealed class EditDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "editing-work", Guid.NewGuid().ToString("N"));
        public EditDirectory() => Directory.CreateDirectory(Path);
        public string Golden(string name, bool prepareForWriting = true)
        {
            string goldens = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "golden");
            string path = FixtureCatalog.Materialize(goldens, Path, FixtureCatalog.ReadManifest(goldens).Single(e => e.Name == name));
            if (prepareForWriting && (name is "fat12.img" or "fat16.img" or "fat32.img")) { PrepareConventionalFat(path, name == "fat32.img"); }
            return path;
        }
        public string File(string name, string content)
        {
            string path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }
        public void Dispose() => Directory.Delete(Path, true);

        public static void AddRootDirectory(string path, string name)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            using (var fs = new DiscUtils.Fat.FatFileSystem(stream)) { fs.CreateDirectory(name); }
            byte[] boot = new byte[512];
            stream.Position = 0;
            stream.ReadExactly(boot);
            if (Read16(boot, 22) == 0) { CompleteFat32Hints(stream, boot); }
        }

        public static byte[] RootEntry(string path, string name)
        {
            using var stream = System.IO.File.OpenRead(path);
            stream.Position = RootEntryOffset(stream, name);
            byte[] entry = new byte[32];
            stream.ReadExactly(entry);
            return entry;
        }

        public static void SetRootCase(string path, string name, byte flags)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            stream.Position = RootEntryOffset(stream, name) + 12;
            stream.WriteByte(flags);
        }

        public static long RootEntryOffset(FileStream stream, string name)
        {
            stream.Position = 0;
            byte[] boot = new byte[512];
            stream.ReadExactly(boot);
            bool fat32 = Read16(boot, 22) == 0;
            long rootSector = Read16(boot, 14) + boot[16] * (long)(fat32 ? Read32(boot, 36) : Read16(boot, 22));
            if (fat32) { rootSector += (Read32(boot, 44) - 2L) * boot[13]; }
            int rootBytes = fat32 ? boot[13] * 512 : Read16(boot, 17) * 32;
            string[] parts = name.ToUpperInvariant().Split('.');
            byte[] expected = Encoding.ASCII.GetBytes(parts[0].PadRight(8) + (parts.Length == 2 ? parts[1] : "").PadRight(3));
            stream.Position = rootSector * 512;
            byte[] root = new byte[rootBytes];
            stream.ReadExactly(root);
            for (int offset = 0; offset < root.Length; offset += 32)
            {
                if (root.AsSpan(offset, 11).SequenceEqual(expected)) { return rootSector * 512 + offset; }
            }
            throw new InvalidOperationException("Synthetic FAT root entry not found: " + name);
        }

        // DiscUtils-authored read goldens omit root labels and FAT32 auxiliary sectors.
        // Build clean disposable write fixtures explicitly; never repair product source images.
        private static void PrepareConventionalFat(string path, bool fat32)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            byte[] boot = new byte[512];
            stream.ReadExactly(boot);
            Encoding.ASCII.GetBytes("PHASE2-OPAQUE-BOOT").CopyTo(boot, 400);
            stream.Position = 0;
            stream.Write(boot);
            stream.Position = stream.Length - 64;
            stream.Write(Encoding.ASCII.GetBytes("PHASE2-OPAQUE-FREE-SECTOR"));
            int reserved = Read16(boot, 14);
            uint fatSectors = fat32 ? Read32(boot, 36) : Read16(boot, 22);
            long rootSector = reserved + boot[16] * (long)fatSectors;
            if (fat32) { rootSector += (Read32(boot, 44) - 2L) * boot[13]; }
            int rootBytes = fat32 ? boot[13] * 512 : Read16(boot, 17) * 32;
            byte[] root = new byte[rootBytes];
            stream.Position = rootSector * 512;
            stream.ReadExactly(root);
            int slot = Enumerable.Range(0, rootBytes / 32).First(index => root[index * 32] == 0) * 32;
            boot.AsSpan(fat32 ? 71 : 43, 11).CopyTo(root.AsSpan(slot));
            root[slot + 11] = 8;
            stream.Position = rootSector * 512;
            stream.Write(root);
            if (!fat32) { return; }
            int backup = Read16(boot, 50);
            stream.Position = backup * 512L;
            stream.Write(boot);
            CompleteFat32Hints(stream, boot);
        }

        private static void CompleteFat32Hints(FileStream stream, byte[] boot)
        {
            int reserved = Read16(boot, 14);
            uint fatSectors = Read32(boot, 36);
            int backup = Read16(boot, 50);
            long clusters = (Read32(boot, 32) - reserved - boot[16] * (long)fatSectors) / boot[13];
            byte[] fat = new byte[checked((int)(fatSectors * 512))];
            stream.Position = reserved * 512L;
            stream.ReadExactly(fat);
            uint free = 0;
            uint next = uint.MaxValue;
            for (uint cluster = 2; cluster < clusters + 2; cluster++)
            {
                if ((Read32(fat, checked((int)cluster * 4)) & 0x0fffffff) == 0)
                {
                    free++;
                    if (next == uint.MaxValue) { next = cluster; }
                }
            }
            byte[] info = new byte[512];
            Write32(info, 0, 0x41615252);
            Write32(info, 484, 0x61417272);
            Write32(info, 488, free);
            Write32(info, 492, next);
            Write32(info, 508, 0xaa550000);
            foreach (long sector in new long[] { Read16(boot, 48), backup + Read16(boot, 48) })
            {
                stream.Position = sector * 512;
                stream.Write(info);
            }
        }

        private static ushort Read16(byte[] bytes, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
        private static uint Read32(byte[] bytes, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        private static void Write32(byte[] bytes, int offset, uint value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
