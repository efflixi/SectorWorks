using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class OpticalRebuildTests
{
    [IndependentOpticalTheory]
    [InlineData("plain")]
    [InlineData("level2")]
    [InlineData("joliet")]
    [InlineData("rockridge")]
    [InlineData("bridge")]
    [InlineData("udf")]
    public async Task IndependentlyAuthoredOpticalImagesUseTheSharedEditingAndRebuildPipeline(string variant)
    {
        using var work = new WorkDirectory();
        string source = Author(work, variant);
        string original = Hash(source);
        string target = work.PathFor("result.iso");
        string add = work.PathFor("import");
        byte[] data = new byte[5000];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(add, data);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.Rebuild.Available, string.Join("; ", workspace.Warnings) + " " + workspace.Capabilities.Rebuild.Reason);
        Assert.Equal(WorkspaceState.RebuildRequired, workspace.State);
        Assert.Equal(2048, workspace.Metadata.SectorSize);
        Assert.False(workspace.Capabilities.CommitInPlace.Available);
        string id = Assert.Single(workspace.Volumes).Id;
        string name = variant is "plain" or "level2" ? "PAYLOAD.DAT" : "Édit_日本.txt";
        string renamed = variant is "plain" ? "RENAMED.TXT" : variant == "level2" ? "RENAMED_LONG.TXT" : "Renamed_日本.txt";
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, id, "/NEW"));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.AddFile, id, "/NEW/" + name, add));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.ReplaceFile, id, "/DIR/KEEP.TXT", add));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, id, "/DIR/MOVE.TXT", NewName: renamed));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Delete, id, "/DROP.TXT"));
        if (variant is "plain" or "level2")
        {
            Assert.Contains(await service.SearchAsync(workspace.Id, "PAYLOAD"), hit => hit.Entry.Name == name);
        }
        Assert.Single(await service.ReadDirectoryAsync(workspace.Id, id, "/NEW"));
        Assert.NotEmpty((await service.ReadHexAsync(workspace.Id, id, "/NEW/" + name, 0)).Text);
        string extraction = work.PathFor("extracted");
        Directory.CreateDirectory(extraction);
        ExtractionResult extracted = await service.ExtractAsync(workspace.Id, id,
            await service.ReadDirectoryAsync(workspace.Id, id, "/NEW"), extraction);
        Assert.Equal(1, extracted.FilesCompleted);
        Assert.Empty(extracted.Failures);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(extraction, name)));
        Assert.Equal("move text", (await service.PreviewAsync(workspace.Id, id, "/DIR/" + renamed)).Text);
        Assert.Contains(await service.ReadDirectoryAsync(workspace.Id, id, "/"), e => e.Name == "UNCH.TXT");
        Assert.DoesNotContain(await service.ReadDirectoryAsync(workspace.Id, id, "/"), e => e.Name == "DROP.TXT");
        EditingSnapshot undo = await service.UndoAsync(workspace.Id);
        Assert.True(undo.CanRedo);
        Assert.Contains(await service.ReadDirectoryAsync(workspace.Id, id, "/"), e => e.Name == "DROP.TXT");
        EditingSnapshot redo = await service.RedoAsync(workspace.Id);
        Assert.Equal(5, redo.Changes.Count);
        await Assert.ThrowsAnyAsync<Exception>(() => service.CommitAsync(workspace.Id));
        SaveImageResult result = await service.SaveAsAsync(workspace.Id, target);
        Assert.Equal(redo.EstimatedOutputBytes, result.Length);
        Assert.Equal(5, (await service.GetEditingSnapshotAsync(workspace.Id)).Changes.Count);
        Assert.Equal(original, Hash(source));
        Assert.Empty(Directory.EnumerateFiles(work.PathFor(""), ".sectorworks-rebuild-*"));
        using (IImageSession reopened = new ImageOpener().OpenReadOnly(target))
        {
            IReadOnlyFileSystem fs = Assert.Single(reopened.Volumes).FileSystem;
            Assert.Equal(data, ReadAll(fs.OpenRead("/NEW/" + name)));
            Assert.Equal(data, ReadAll(fs.OpenRead("/DIR/KEEP.TXT")));
            Assert.Equal("move text"u8.ToArray(), ReadAll(fs.OpenRead("/DIR/" + renamed)));
            Assert.Equal("untouched"u8.ToArray(), ReadAll(fs.OpenRead("/UNCH.TXT")));
        }
        if (variant != "udf")
        {
            string primary = Text(Run("isoinfo", "-i", target, "-f"));
            Assert.Contains("/UNCH.TXT;1", primary, StringComparison.Ordinal);
            Assert.DoesNotContain("DROP.TXT", primary, StringComparison.Ordinal);
            if (variant is "joliet" or "bridge") { Assert.Contains("/NEW/", Text(Run("isoinfo", "-J", "-i", target, "-f")), StringComparison.Ordinal); }
            if (variant is "rockridge" or "bridge")
            {
                Assert.Equal("untouched"u8.ToArray(), Run("isoinfo", "-R", "-i", target, "-x", "/UNCH.TXT"));
                Assert.Contains("/NEW/", Text(Run("isoinfo", "-R", "-i", target, "-f")), StringComparison.Ordinal);
            }
        }
        if (variant is "udf" or "bridge")
        {
            Assert.Contains("Type = Udf", Text(Run("7z", "l", target)), StringComparison.Ordinal);
            Assert.Equal(data, Run("7z", "x", "-so", target, "DIR/KEEP.TXT"));
            Assert.Equal("untouched"u8.ToArray(), Run("7z", "x", "-so", target, "UNCH.TXT"));
        }
        await service.DiscardAsync(workspace.Id);
        Assert.Empty((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
    }

    [IndependentOpticalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RebuildRetainsCompleteNoEmulationBootMembersAtTheirOriginalLbas(bool efi)
    {
        using var work = new WorkDirectory();
        string tree = work.PathFor("fixture");
        Directory.CreateDirectory(tree);
        byte[] boot = new byte[4096];
        "SectorWorks disposable catalog test; not boot code"u8.CopyTo(boot);
        File.WriteAllBytes(Path.Combine(tree, "BOOT.IMG"), boot);
        File.WriteAllText(Path.Combine(tree, "NOTE.TXT"), "note");
        string source = work.PathFor("boot.iso");
        _ = Run("genisoimage", "-quiet", "-V", "SW_BOOT", efi ? "-e" : "-b", "BOOT.IMG", "-c", "BOOT.CAT",
            "-no-emul-boot", "-boot-load-size", "4", "-o", source, tree);
        byte[] original = File.ReadAllBytes(source);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        VolumeInfo volume = Assert.Single(workspace.Volumes);
        Assert.Contains(efi ? "UEFI" : "BIOS", volume.Properties["El Torito platforms"], StringComparison.Ordinal);
        Assert.True(workspace.Capabilities.Rebuild.Available, string.Join("; ", workspace.Warnings));
        Assert.False(workspace.Capabilities.PreserveBootability.Available);
        Assert.False(workspace.Capabilities.PreserveExactLayout.Available);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(workspace.Id, new(ImageChangeKind.Delete, volume.Id, "/BOOT.IMG")));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/BOOT.CAT", NewName: "OTHER.CAT")));
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, volume.Id, "/NOTE.TXT", NewName: "RENAMED.TXT"));
        string target = work.PathFor("out.iso");
        await service.SaveAsAsync(workspace.Id, target);
        byte[] output = File.ReadAllBytes(target);
        int catalogAddress = int.Parse(volume.Properties["El Torito catalog LBA"], System.Globalization.CultureInfo.InvariantCulture) * 2048;
        byte[] catalog = original.AsSpan(catalogAddress, 2048).ToArray();
        Assert.Equal(catalog, output.AsSpan(catalogAddress, 2048).ToArray());
        int bootAddress = (int)BinaryPrimitives.ReadUInt32LittleEndian(catalog.AsSpan(40)) * 2048;
        Assert.Equal(boot, output.AsSpan(bootAddress, boot.Length).ToArray());
        Assert.Equal(boot, Run("isoinfo", "-i", target, "-x", "/BOOT.IMG;1"));
        Assert.Contains("El Torito", Text(Run("isoinfo", "-d", "-i", target)), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [IndependentOpticalFact]
    public async Task OpaqueSystemAreaNeverBecomesEditableAndFailedRebuildLeavesNoOutput()
    {
        using var work = new WorkDirectory();
        string source = Author(work, "plain");
        string target = work.PathFor("out.iso");
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.Rebuild.Available, workspace.Capabilities.Rebuild.Reason);
        string id = Assert.Single(workspace.Volumes).Id;
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, id, "/not-a-primary-name")));
        Assert.Empty((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.CreateDirectory, id, "/NEW"));
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(stage =>
        {
            if (stage.Stage == "Writing rebuilt optical image") { cancellation.Cancel(); }
        });
        string hash = Hash(source);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsAsync(workspace.Id, target, progress, cancellation.Token));
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.EnumerateFiles(work.PathFor(""), ".sectorworks-rebuild-*"));
        Assert.Equal(hash, Hash(source));
        Assert.Single((await service.GetEditingSnapshotAsync(workspace.Id)).Changes);
        await service.CloseAsync(workspace.Id);
        using (var file = File.OpenWrite(source)) { file.WriteByte(0xc2); }
        WorkspaceInfo opaque = await service.OpenAsync(source);
        Assert.False(opaque.Capabilities.Modify.Available);
        Assert.Contains("untracked", opaque.Capabilities.Rebuild.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(opaque.Capabilities.Browse.Available);
    }

    [IndependentOpticalFact]
    public async Task BridgeWithMismatchedNamespaceCannotSilentlyDropIt()
    {
        using var work = new WorkDirectory();
        string source = Author(work, "bridge");
        byte[] image = File.ReadAllBytes(source);
        int offset = image.AsSpan().IndexOf(System.Text.Encoding.BigEndianUnicode.GetBytes("UNCH.TXT"));
        Assert.True(offset >= 0);
        image[offset + 1] = (byte)'X';
        File.WriteAllBytes(source, image);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.Browse.Available);
        Assert.False(workspace.Capabilities.Rebuild.Available);
        Assert.Contains("names disagree", workspace.Capabilities.Rebuild.Reason, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(workspace.Id,
            new(ImageChangeKind.CreateDirectory, Assert.Single(workspace.Volumes).Id, "/NEW")));
        Assert.Equal(image, File.ReadAllBytes(source));
    }

    [IndependentOpticalFact]
    public async Task ValidHybridMbrDoesNotEnableRebuildOrDiskCommit()
    {
        using var work = new WorkDirectory();
        string path = Author(work, "plain");
        byte[] image = File.ReadAllBytes(path);
        image[450] = 0x17;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(454), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(458), (uint)(image.Length / 512 - 1));
        image[510] = 0x55;
        image[511] = 0xaa;
        File.WriteAllBytes(path, image);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        Assert.True(workspace.Metadata.HasOpticalPartitionLayout);
        Assert.Single(workspace.Partitions);
        Assert.True(workspace.Capabilities.Browse.Available);
        Assert.False(workspace.Capabilities.Modify.Available);
        Assert.False(workspace.Capabilities.CommitInPlace.Available);
        Assert.Contains("Hybrid", workspace.Capabilities.Rebuild.Reason, StringComparison.Ordinal);
        Assert.Equal(image, File.ReadAllBytes(path));
    }

    [IndependentOpticalTheory]
    [InlineData("XCD322.0.443")]
    [InlineData("XCD322.1 (137)")]
    [InlineData("CD Rep 2.0.132")]
    [InlineData("PDSC IS 2.0IS20123456Test Press")]
    public async Task StandaloneRebuildPreservesVerifiedZeroTailAndDescriptorVersionStamp(string stamp)
    {
        using var work = new WorkDirectory();
        string source = Author(work, "plain");
        byte[] original = File.ReadAllBytes(source);
        const int padding = 64 * 2048;
        Array.Resize(ref original, original.Length + padding);
        int offset = stamp.StartsWith("PDSC", StringComparison.Ordinal) ? 1395
            : stamp.StartsWith("XCD", StringComparison.Ordinal) ? 1383 : 883;
        System.Text.Encoding.ASCII.GetBytes(stamp).CopyTo(original, 16 * 2048 + offset);
        File.WriteAllBytes(source, original);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.Rebuild.Available, workspace.Capabilities.Rebuild.Reason);
        await service.StageAsync(workspace.Id, new(ImageChangeKind.Rename, Assert.Single(workspace.Volumes).Id, "/UNCH.TXT", NewName: "MOVED.TXT"));
        string output = work.PathFor("out.iso");
        await service.SaveAsAsync(workspace.Id, output);
        byte[] saved = File.ReadAllBytes(output);
        int declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(16 * 2048 + 80)) * 2048;
        Assert.Equal(padding, saved.Length - declared);
        Assert.False(saved.AsSpan(declared).ContainsAnyExcept((byte)0));
        Assert.Equal(original.AsSpan(16 * 2048 + 883, 1165).ToArray(), saved.AsSpan(16 * 2048 + 883, 1165).ToArray());
        Assert.Equal("untouched"u8.ToArray(), Run("isoinfo", "-i", output, "-x", "/MOVED.TXT;1"));
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [IndependentOpticalTheory]
    [InlineData("round trip")]
    [InlineData("different PVD")]
    [InlineData("unknown system sector")]
    [InlineData("bad record date")]
    [InlineData("missing document")]
    [InlineData("unknown Apple suffix")]
    public async Task LegacyIsoMetadataIsPreservedOnlyInTheQualifiedShapes(string scenario)
    {
        using var work = new WorkDirectory();
        string source = Author(work, "plain");
        byte[] original = File.ReadAllBytes(source);
        const int sector = 2048;
        const int primary = 16 * sector;
        "Test Press"u8.CopyTo(original.AsSpan(primary + 318));
        "(c) 1999 Test Press"u8.CopyTo(original.AsSpan(primary + 702));
        "DROP.TXT;1"u8.CopyTo(original.AsSpan(primary + 739));
        if (scenario == "missing document") { original[primary + 739] = (byte)'X'; }
        for (int block = 1; block <= 15; block++)
        { "The Personal RomMaker (Windows V3.01)"u8.CopyTo(original.AsSpan(block * sector)); }
        if (scenario == "unknown system sector") { original[sector * 2 + 256] = 1; }
        Assert.Equal(255, original[primary + sector]);
        Assert.False(original.AsSpan(primary + 2 * sector, sector).ContainsAnyExcept((byte)0));
        original.AsSpan(primary + sector, sector).CopyTo(original.AsSpan(primary + 2 * sector));
        original.AsSpan(primary, sector).CopyTo(original.AsSpan(primary + sector));
        if (scenario == "different PVD") { original[primary + sector + 40] = (byte)'X'; }
        int record = FindRecord(original);
        byte[] date = [0, 0, 0, 0, 0, 0, 0xec];
        if (scenario == "bad record date") { date[1] = 17; }
        date.CopyTo(original, record + 18);
        int recordEnd = record + original[record];
        Assert.Equal(0, original[recordEnd]); // Last root record; only zero padding follows it.
        byte[] finder = [0x41, 0x41, 14, 2, .. "TEXTttxt"u8, 1, 0];
        finder.CopyTo(original, recordEnd);
        original[record] += (byte)finder.Length;
        if (scenario == "unknown Apple suffix")
        {
            original[record] += 2;
            original[recordEnd + 15] = 0x91;
        }
        File.WriteAllBytes(source, original);

        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo opened = await service.OpenAsync(source);
        Assert.True(opened.Capabilities.Browse.Available);
        if (scenario != "round trip")
        {
            Assert.False(opened.Capabilities.Rebuild.Available);
            Assert.Contains(scenario switch
            {
                "different PVD" => "multiple",
                "unknown system sector" => "untracked system area",
                "bad record date" => "recording date",
                "missing document" => "does not resolve",
                _ => "Invalid SUSP entry"
            }, opened.Capabilities.Rebuild.Reason, StringComparison.Ordinal);
            return;
        }
        string id = Assert.Single(opened.Volumes).Id;
        Assert.Null(Assert.Single(await service.ReadDirectoryAsync(opened.Id, id, "/"), e => e.Name == "UNCH.TXT").ModifiedUtc);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(opened.Id, new(ImageChangeKind.Delete, id, "/DROP.TXT")));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.StageAsync(opened.Id,
            new(ImageChangeKind.Rename, id, "/DROP.TXT", NewName: "MOVED.TXT")));
        await service.StageAsync(opened.Id, new(ImageChangeKind.CreateDirectory, id, "/NEW"));
        string output = work.PathFor("out.iso");
        await service.SaveAsAsync(opened.Id, output);
        byte[] result = File.ReadAllBytes(output);
        Assert.True(result.AsSpan(primary, sector).SequenceEqual(result.AsSpan(primary + sector, sector)));
        Assert.True(result.AsSpan(0, primary).SequenceEqual(original.AsSpan(0, primary)));
        Assert.True(result.AsSpan(primary + 702, 111).SequenceEqual(original.AsSpan(primary + 702, 111)));
        int resultRecord = FindRecord(result);
        Assert.True(result.AsSpan(resultRecord + 18, 7).SequenceEqual(date));
        Assert.True(result.AsSpan(resultRecord + result[resultRecord] - finder.Length, finder.Length).SequenceEqual(finder));
        Assert.Contains("/NEW", Text(Run("isoinfo", "-i", output, "-f")), StringComparison.Ordinal);
        Assert.Equal("untouched"u8.ToArray(), Run("isoinfo", "-i", output, "-x", "/UNCH.TXT;1"));
        Assert.Equal("drop"u8.ToArray(), Run("isoinfo", "-i", output, "-x", "/DROP.TXT;1"));
        Assert.Equal(original, File.ReadAllBytes(source));

        static int FindRecord(byte[] image)
        {
            int root = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(primary + 158)) * sector);
            int identifier = image.AsSpan(root, sector).IndexOf("UNCH.TXT;1"u8);
            Assert.True(identifier >= 33);
            return root + identifier - 33;
        }
    }

    [IndependentOpticalTheory]
    [InlineData("omitted geometry")]
    [InlineData("omitted sequence")]
    [InlineData("conflicting geometry")]
    [InlineData("short parent")]
    [InlineData("different parent extent")]
    public async Task StandaloneIsoQualifiesOnlyKnownRedundantFieldVariants(string scenario)
    {
        const int sector = 2048;
        const int primary = 16 * sector;
        bool parentVariant = scenario.EndsWith("parent", StringComparison.Ordinal) || scenario == "different parent extent";
        using var work = new WorkDirectory();
        string source = Author(work, parentVariant ? "large root" : "plain");
        byte[] original = File.ReadAllBytes(source);
        int parentRecord = parentVariant ? FindChildParentRecord(original) : 0;
        switch (scenario)
        {
            case "omitted geometry":
                original.AsSpan(primary + 122, 2).Clear();
                original.AsSpan(primary + 130, 2).Clear();
                goto case "omitted sequence";
            case "omitted sequence":
                original.AsSpan(primary + 126, 2).Clear();
                break;
            case "conflicting geometry":
                original[primary + 127] = 2;
                break;
            case "different parent extent":
                WriteBoth(original.AsSpan(parentRecord + 2), 16);
                goto case "short parent";
            case "short parent":
                Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(parentRecord + 10)) > sector);
                WriteBoth(original.AsSpan(parentRecord + 10), sector);
                break;
        }
        File.WriteAllBytes(source, original);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo opened = await service.OpenAsync(source);
        Assert.True(opened.Capabilities.Browse.Available);
        if (scenario is "conflicting geometry" or "different parent extent")
        {
            Assert.False(opened.Capabilities.Rebuild.Available);
            return;
        }
        Assert.True(opened.Capabilities.Rebuild.Available, opened.Capabilities.Rebuild.Reason);
        string id = Assert.Single(opened.Volumes).Id;
        await service.StageAsync(opened.Id, new(ImageChangeKind.CreateDirectory, id, "/NEW"));
        string output = work.PathFor("out.iso");
        await service.SaveAsAsync(opened.Id, output);
        byte[] result = File.ReadAllBytes(output);
        Assert.True(original.AsSpan(primary + 120, 12).SequenceEqual(result.AsSpan(primary + 120, 12)));
        if (parentVariant)
        {
            int savedParent = FindChildParentRecord(result);
            Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(primary + 166)),
                BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(savedParent + 10)));
        }
        Assert.Contains("/NEW", Text(Run("isoinfo", "-i", output, "-f")), StringComparison.Ordinal);
        Assert.Equal("original"u8.ToArray(), Run("isoinfo", "-i", output, "-x", "/DIR/KEEP.TXT;1"));
        Assert.Equal(original, File.ReadAllBytes(source));

        static void WriteBoth(Span<byte> field, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(field, value);
            BinaryPrimitives.WriteUInt32BigEndian(field[4..], value);
        }

        static int FindChildParentRecord(byte[] image)
        {
            int root = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(primary + 158)) * sector);
            int recordId = image.AsSpan(root, sector).IndexOf("DIR"u8);
            Assert.True(recordId >= 33);
            int child = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(root + recordId - 31)) * sector);
            return child + image[child];
        }
    }

    [IndependentOpticalTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StandaloneIsoRetainsLegacyRootAndMkisofsRecord(bool fingerprinted, bool invalidFingerprint)
    {
        const int sector = 2048;
        const int primary = 16 * sector;
        const int mastering = 18 * sector;
        using var work = new WorkDirectory();
        string source = Author(work, "plain");
        byte[] original = File.ReadAllBytes(source);
        original.AsSpan(primary + 120, 4).Clear(); // De facto single-volume set marker.
        original[primary + 189] = 1; // Embedded root points to the verified actual root.
        Assert.False(original.AsSpan(mastering, sector).ContainsAnyExcept((byte)0));
        "MKI Mon Jan  1 12:00:00 2001\n3.02a09 -o generated.iso fixture"u8.CopyTo(original.AsSpan(mastering));
        if (fingerprinted)
        {
            int sum = original.AsSpan(primary, sector).ToArray().Sum(value => (int)value);
            original[mastering + sector - 3] = (byte)(sum >> 16);
            original[mastering + sector - 2] = unchecked((byte)(sum >> 8));
            original[mastering + sector - 1] = unchecked((byte)(sum + (invalidFingerprint ? 1 : 0)));
        }
        File.WriteAllBytes(source, original);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo opened = await service.OpenAsync(source);
        Assert.True(opened.Capabilities.Browse.Available);
        if (invalidFingerprint)
        {
            Assert.False(opened.Capabilities.Rebuild.Available);
            Assert.Contains("Nonzero untracked", opened.Capabilities.Rebuild.Reason, StringComparison.Ordinal);
            return;
        }
        Assert.True(opened.Capabilities.Rebuild.Available, opened.Capabilities.Rebuild.Reason);
        string id = Assert.Single(opened.Volumes).Id;
        await service.StageAsync(opened.Id, new(ImageChangeKind.CreateDirectory, id, "/NEW"));
        string output = work.PathFor("out.iso");
        await service.SaveAsAsync(opened.Id, output);
        byte[] saved = File.ReadAllBytes(output);
        Assert.False(saved.AsSpan(primary + 120, 4).ContainsAnyExcept((byte)0));
        Assert.Equal(1, saved[primary + 189]);
        Assert.True(saved.AsSpan(mastering, sector - 3).SequenceEqual(original.AsSpan(mastering, sector - 3)));
        int checksum = (saved[mastering + sector - 3] << 16) | (saved[mastering + sector - 2] << 8) | saved[mastering + sector - 1];
        Assert.Equal(fingerprinted ? saved.AsSpan(primary, sector).ToArray().Sum(value => (int)value) : 0, checksum);
        Assert.Equal("untouched"u8.ToArray(), Run("isoinfo", "-i", output, "-x", "/UNCH.TXT;1"));
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [IndependentOpticalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandaloneRebuildStillRejectsUnknownDescriptorOrNonzeroTail(bool nonzeroTail)
    {
        using var work = new WorkDirectory();
        string source = Author(work, "plain");
        byte[] image = File.ReadAllBytes(source);
        Array.Resize(ref image, image.Length + 2048);
        image[nonzeroTail ? image.Length - 1 : 16 * 2048 + 883] = 0xa5;
        File.WriteAllBytes(source, image);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.Browse.Available);
        Assert.False(workspace.Capabilities.Rebuild.Available);
        Assert.Contains(nonzeroTail ? "Nonzero untracked" : "Opaque ISO application", workspace.Capabilities.Rebuild.Reason,
            StringComparison.Ordinal);
    }

    [IndependentOpticalFact]
    public async Task DeclaredVolumeBoundaryCannotExcludeLiveIsoExtents()
    {
        using var work = new WorkDirectory();
        string source = Author(work, "plain");
        byte[] image = File.ReadAllBytes(source);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(16 * 2048 + 80), 20);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(16 * 2048 + 84), 20);
        File.WriteAllBytes(source, image);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(source);
        Assert.True(workspace.Capabilities.Browse.Available);
        Assert.False(workspace.Capabilities.Rebuild.Available);
        Assert.Contains("crosses its declared volume length", workspace.Capabilities.Rebuild.Reason, StringComparison.Ordinal);
    }

    [IndependentOpticalTheory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task PartialDirectoryExtentAndLegacyAliasCanBePreservedOnlyForUnderstoodRecords(
        bool unknownPadding, bool withXaRecords, bool unknownXaFileNumber)
    {
        using var work = new WorkDirectory();
        string tree = work.PathFor("partial");
        Directory.CreateDirectory(tree);
        File.WriteAllText(Path.Combine(tree, "BASE.TXT"), "stable content");
        string source = work.PathFor("input.iso");
        _ = Run("genisoimage", "-quiet", "-o", source, tree);
        byte[] bytes = File.ReadAllBytes(source);
        const int pvd = 16 * 2048;
        int root = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pvd + 158)) * 2048;
        if (withXaRecords)
        {
            byte[] original = bytes.AsSpan(root, 2048).ToArray();
            bytes.AsSpan(root, 2048).Clear();
            int write = root;
            for (int position = 0; original[position] != 0; position += original[position])
            {
                int length = original[position];
                original.AsSpan(position, length).CopyTo(bytes.AsSpan(write));
                bool directory = (original[position + 25] & 2) != 0;
                bytes[write] = (byte)(length + 14);
                Span<byte> xa = bytes.AsSpan(write + length, 14);
                BinaryPrimitives.WriteUInt16BigEndian(xa[4..], directory ? (ushort)0x8d55 : (ushort)0x0911);
                "XA"u8.CopyTo(xa[6..]);
                if (!directory && unknownXaFileNumber) { xa[8] = 4; }
                write += length + 14;
            }
            bytes[pvd + 156] = 48;
        }
        int size = 0;
        while (bytes[root + size] != 0) { size += bytes[root + size]; }
        SetLength(pvd + 156 + 10);
        SetLength(root + 10);
        SetLength(root + bytes[root] + 10);
        int alias = bytes.AsSpan(root, size).IndexOf("BASE.TXT;1"u8);
        Assert.True(alias >= 0);
        "MiX&.TXT;1"u8.CopyTo(bytes.AsSpan(root + alias));
        "CD-XA001"u8.CopyTo(bytes.AsSpan(pvd + 1024));
        if (unknownPadding) { bytes[root + size] = 0x77; }
        File.WriteAllBytes(source, bytes);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo input = await service.OpenAsync(source);
        if (unknownPadding || unknownXaFileNumber)
        {
            Assert.True(input.Capabilities.Browse.Available);
            Assert.False(input.Capabilities.Rebuild.Available);
            Assert.Contains(unknownPadding ? "Nonzero bytes follow the declared" : "XA directory record contains",
                input.Capabilities.Rebuild.Reason, StringComparison.Ordinal);
        }
        else
        {
            Assert.True(input.Capabilities.Rebuild.Available, input.Capabilities.Rebuild.Reason);
            string add = work.PathFor("host.txt");
            File.WriteAllText(add, "added bytes");
            await service.StageAsync(input.Id, new(ImageChangeKind.AddFile, Assert.Single(input.Volumes).Id, "/ADDED.TXT", add));
            string saved = work.PathFor("rebuilt.iso");
            await service.SaveAsAsync(input.Id, saved);
            Assert.Equal("stable content"u8.ToArray(), Run("isoinfo", "-i", saved, "-x", "/MiX&.TXT;1"));
            Assert.Equal("added bytes"u8.ToArray(), Run("isoinfo", "-i", saved, "-x", "/ADDED.TXT;1"));
            byte[] result = File.ReadAllBytes(saved);
            Assert.True(result.AsSpan(pvd + 1024, 8).SequenceEqual("CD-XA001"u8));
            if (withXaRecords)
            {
                int savedRoot = (int)BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(pvd + 158)) * 2048;
                int added = result.AsSpan(savedRoot, 2048).IndexOf("ADDED.TXT;1"u8);
                Assert.True(added >= 0);
                Assert.True(result.AsSpan(savedRoot + added + 11 + 6, 2).SequenceEqual("XA"u8));
            }
        }
        Assert.Equal(bytes, File.ReadAllBytes(source));

        void SetLength(int offset)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)size);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 4), (uint)size);
        }
    }

    [IndependentOpticalFact]
    public async Task HybridExposesOpticalAndDiskFilesWithoutQualifyingEitherWriter()
    {
        using var work = new WorkDirectory();
        string path = Author(work, "plain");
        byte[] iso = File.ReadAllBytes(path);
        byte[] fat = File.ReadAllBytes(work.Golden("fat12.img"));
        byte[] hybrid = [.. iso, .. fat];
        hybrid[450] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(hybrid.AsSpan(454), (uint)(iso.Length / 512));
        BinaryPrimitives.WriteUInt32LittleEndian(hybrid.AsSpan(458), (uint)(fat.Length / 512));
        hybrid[510] = 0x55;
        hybrid[511] = 0xaa;
        File.WriteAllBytes(path, hybrid);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        Assert.True(workspace.Metadata.HasOpticalPartitionLayout);
        VolumeInfo optical = Assert.Single(workspace.Volumes, v => v.Partition is null);
        VolumeInfo disk = Assert.Single(workspace.Volumes, v => v.Partition is not null);
        Assert.Equal("ISO9660", optical.FileSystem);
        Assert.Equal("FAT12", disk.FileSystem);
        Assert.NotEmpty(await service.ReadDirectoryAsync(workspace.Id, disk.Id, "/"));
        Assert.Equal("untouched", (await service.PreviewAsync(workspace.Id, optical.Id, "/UNCH.TXT")).Text);
        Assert.All(workspace.Volumes, v => Assert.False(v.Capabilities!.AddFiles.Available));
        Assert.False(workspace.Capabilities.Rebuild.Available);
        Assert.False(workspace.Capabilities.CommitInPlace.Available);
    }

    [IndependentOpticalFact]
    public async Task RejectedHybridDiskMapDoesNotHideIndependentlyValidatedOpticalFilesystem()
    {
        using var work = new WorkDirectory();
        string path = Author(work, "plain");
        byte[] image = File.ReadAllBytes(path);
        image[450] = 0x81;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(458), 1);
        image[510] = 0x55;
        image[511] = 0xaa;
        File.WriteAllBytes(path, image);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        Assert.True(workspace.Metadata.HasOpticalPartitionLayout);
        Assert.Empty(workspace.Partitions);
        Assert.Contains(workspace.Warnings, warning => warning.Contains("partition view rejected", StringComparison.Ordinal));
        VolumeInfo optical = Assert.Single(workspace.Volumes);
        Assert.Equal("untouched", (await service.PreviewAsync(workspace.Id, optical.Id, "/UNCH.TXT")).Text);
        Assert.False(workspace.Capabilities.Rebuild.Available);
        Assert.False(workspace.Capabilities.CommitInPlace.Available);
    }

    [Fact]
    public async Task UnvalidatedOpticalSignatureDoesNotHideVerifiedRawDiskView()
    {
        using var work = new WorkDirectory();
        string path = work.Golden("fat12.img");
        byte[] disk = File.ReadAllBytes(path);
        "CD001"u8.CopyTo(disk.AsSpan(16 * 2048 + 1));
        disk[16 * 2048 + 6] = 0;
        File.WriteAllBytes(path, disk);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        Assert.Equal("Raw", workspace.Metadata.Format);
        Assert.Equal("FAT12", Assert.Single(workspace.Volumes).FileSystem);
        Assert.Contains(workspace.Warnings, warning => warning.Contains("conflicting signatures", StringComparison.Ordinal));
        Assert.False(workspace.Capabilities.SaveAs.Available);
        Assert.False(workspace.Capabilities.Rebuild.Available);
    }

    [IndependentOpticalFact]
    public async Task CorruptUdfDescriptorCrcNeverEnablesReadOrRebuild()
    {
        using var work = new WorkDirectory();
        string path = Author(work, "udf");
        byte[] image = File.ReadAllBytes(path);
        int mainDescriptorLocation = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(256 * 2048 + 20));
        image[mainDescriptorLocation * 2048 + 24] ^= 1;
        File.WriteAllBytes(path, image);
        using var service = new ExplorerService(new ImageOpener(), stagingDirectory: work.PathFor("stage"));
        WorkspaceInfo workspace = await service.OpenAsync(path);
        Assert.Equal("UDF", workspace.Metadata.Format);
        Assert.Empty(workspace.Volumes);
        Assert.False(workspace.Capabilities.Rebuild.Available);
        Assert.Contains("CRC", workspace.Capabilities.Rebuild.Reason, StringComparison.Ordinal);
        Assert.Equal(image, File.ReadAllBytes(path));
    }

    private sealed class ImmediateProgress(Action<OperationProgress> callback) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => callback(value);
    }

    private static string Author(WorkDirectory work, string variant)
    {
        string directory = work.PathFor("fixture");
        Directory.CreateDirectory(Path.Combine(directory, "DIR"));
        File.WriteAllText(Path.Combine(directory, "DIR", "KEEP.TXT"), "original");
        File.WriteAllText(Path.Combine(directory, "DIR", "MOVE.TXT"), "move text");
        File.WriteAllText(Path.Combine(directory, "DROP.TXT"), "drop");
        File.WriteAllText(Path.Combine(directory, "UNCH.TXT"), "untouched");
        if (variant == "level2") { File.WriteAllText(Path.Combine(directory, "LONG_IDENTIFIER.TXT"), "level 2"); }
        if (variant == "large root")
        {
            for (int i = 0; i < 65; i++)
            { File.WriteAllText(Path.Combine(directory, $"F{i:D3}.TXT"), "root entry"); }
        }
        if (variant is "joliet" or "rockridge" or "bridge" or "udf")
        { File.WriteAllText(Path.Combine(directory, "Été.txt"), "Unicode"); }

        string path = work.PathFor("source.iso");
        string[] flags = variant switch
        {
            "level2" => ["-iso-level", "2"],
            "joliet" => ["-J"],
            "rockridge" => ["-R"],
            "bridge" => ["-J", "-R", "-udf"],
            "udf" => ["-udf"],
            _ => []
        };
        _ = Run("genisoimage", ["-quiet", "-input-charset", "utf-8", "-V", "SW_PHASE4", .. flags, "-o", path, directory]);
        if (variant == "udf") { StripIsoViewFromGeneratedBridge(path); }
        return path;
    }

    private static void StripIsoViewFromGeneratedBridge(string path)
    {
        // Derive a UDF-only test from the independent DVD-ROM author: remove
        // only the primary ISO descriptor and its registered path tables and
        // directories. UDF records and shared payload sectors remain untouched.
        const int sector = 2048;
        byte[] image = File.ReadAllBytes(path);
        Span<byte> pvd = image.AsSpan(16 * sector, sector);
        int tableSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(pvd[132..]);
        int table = (int)BinaryPrimitives.ReadUInt32LittleEndian(pvd[140..]);
        for (int i = 0; i < tableSize;)
        {
            int start = table * sector + i;
            int directory = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(start + 2));
            int directorySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(directory * sector + 10));
            image.AsSpan(directory * sector, directorySize).Clear();
            int key = image[start];
            i += 8 + key + (key & 1);
        }
        for (int offset = 140; offset <= 152; offset += 4)
        {
            int lba = (int)(offset < 148 ? BinaryPrimitives.ReadUInt32LittleEndian(pvd[offset..])
                : BinaryPrimitives.ReadUInt32BigEndian(pvd[offset..]));
            if (lba != 0) { image.AsSpan(lba * sector, (tableSize + sector - 1) / sector * sector).Clear(); }
        }
        image.AsSpan(18 * sector, 3 * sector).CopyTo(image.AsSpan(16 * sector));
        image.AsSpan(19 * sector, 2 * sector).Clear();
        File.WriteAllBytes(path, image);
        Assert.Contains("Type = Udf", Text(Run("7z", "l", path)), StringComparison.Ordinal);
    }

    private static byte[] ReadAll(Stream input)
    {
        using (input)
        using (var buffer = new MemoryStream())
        {
            input.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Text(byte[] data) => System.Text.Encoding.UTF8.GetString(data);

    private static byte[] Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch " + program);
        using var output = new MemoryStream();
        Task copying = process.StandardOutput.BaseStream.CopyToAsync(output);
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(true);
            throw new TimeoutException(program + " timed out.");
        }
        copying.GetAwaiter().GetResult();
        if (process.ExitCode != 0) { throw new InvalidDataException(program + ": " + error.GetAwaiter().GetResult()); }
        return output.ToArray();
    }
}


public sealed class IndependentOpticalFactAttribute : FactAttribute
{
    public IndependentOpticalFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) { Skip = "Independent optical authoring and oracle commands run on Linux; portable runtime qualification is separate."; }
    }
}

public sealed class IndependentOpticalTheoryAttribute : TheoryAttribute
{
    public IndependentOpticalTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux()) { Skip = "Independent optical authoring and oracle commands run on Linux; portable runtime qualification is separate."; }
    }
}
