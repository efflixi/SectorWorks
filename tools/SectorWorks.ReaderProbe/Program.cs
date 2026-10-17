using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Services;
using SectorWorks.Storage;

namespace SectorWorks.ReaderProbe;

[SupportedOSPlatform("linux")]
internal static class Program
{
    private const long ExtractionBudget = 64L * 1024 * 1024;

    private static async Task<int> Main(string[] args)
    {
        try { return await Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Reader probe could not establish or finish integrity evidence: {error.GetType().Name}.");
            return 1;
        }
    }

    private static async Task<int> Run(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 2)
        {
            Console.Error.WriteLine("Linux only: reader-probe <external-image> <existing-private-local-output-directory>");
            return 2;
        }
        string image = Path.GetFullPath(args[0]);
        string destination = Path.GetFullPath(args[1]);
        ValidateDestination(image, destination);
        string identityBefore = await Identity(image);
        string before = await SourceHash(image);
        Require(identityBefore == await Identity(image), "Source changed during baseline hashing.");
        var report = new Dictionary<string, object?>
        {
            ["SourceBytes"] = new FileInfo(image).Length,
            ["IdentityBefore"] = identityBefore,
            ["SourceSha256Before"] = before,
            ["ExtractionBudgetBytes"] = ExtractionBudget,
            ["Provenance"] = "External owner media, Linux in-place reads only; no historical authoring or health certification"
        };
        bool passed = false;
        string owned = Path.Combine(destination, $"sectorworks-reader-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(owned, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            await Probe(image, owned, report, timeout.Token);
            passed = true;
        }
        catch (Exception error)
        {
            // Do not serialize paths, source filenames, exception messages or content.
            report["FailureType"] = error.GetType().Name;
        }
        finally
        {
            try
            {
                if (Directory.Exists(owned)) { Directory.Delete(owned, recursive: true); }
            }
            catch (IOException) { passed = false; }
            catch (UnauthorizedAccessException) { passed = false; }
            report["ExtractedContentRemoved"] = !Directory.Exists(owned);
            string after = await SourceHash(image);
            string identityAfter = await Identity(image);
            report["SourceSha256After"] = after;
            report["IdentityAfter"] = identityAfter;
            report["SourceUnchanged"] = before == after && identityBefore == identityAfter;
            report["Passed"] = passed && before == after && identityBefore == identityAfter;
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return (bool)report["Passed"]! ? 0 : 1;
    }

    private static async Task Probe(string path, string owned, Dictionary<string, object?> report, CancellationToken token)
    {
        using (IImageSession image = new ImageOpener().OpenReadOnly(path, token))
        {
            ImageVolume volume = image.Volumes.Single();
            IReadOnlyFileSystem fs = volume.FileSystem;
            Require(fs.Properties["NTFS version"] == "3.0", "Unexpected observed version.");
            report["Version"] = fs.Properties["NTFS version"];
            report["AttributeLists"] = fs.Properties.GetValueOrDefault("Attribute lists");
            report["StrictWriterQualification"] = new DiskImageWriter().Qualify(image).Reason;
            report["OriginalStrictPreflightRejection"] = StrictDiagnostic(image.Device, token);
            int directories = 0, files = 0, root = 0;
            var stack = new Stack<(string Path, int Depth)>();
            stack.Push(("/", 0));
            while (stack.TryPop(out var current))
            {
                token.ThrowIfCancellationRequested();
                Require(current.Depth <= 128 && directories + files <= 100000, "Enumeration safety bound.");
                IReadOnlyList<FileEntry> entries = fs.ReadDirectory(current.Path, token);
                if (current.Path == "/") { root = entries.Count; }
                foreach (FileEntry entry in entries)
                {
                    if (entry.IsDirectory)
                    {
                        Require(!entry.IsLink, "Unqualified directory link.");
                        directories++;
                        stack.Push((entry.Path, current.Depth + 1));
                    }
                    else { files++; }
                }
            }
            report["FilteredDirectories"] = directories;
            report["FilteredDirectoriesIncludingRoot"] = directories + 1;
            report["FilteredFiles"] = files;
            report["RootEntries"] = root;
            report["NativeRootEntries"] = await NativeDirectoryCount(path, "/", token);
            report["System32FilteredEntries"] = fs.ReadDirectory("/WINNT/system32", token).Count;
            report["NativeSystem32Entries"] = await NativeDirectoryCount(path, "/WINNT/system32", token);
            report["DirectKnownStream"] = await CompareStream(path, fs, "/boot.ini", 191, token);
        }

        using var service = new ExplorerService(new ImageOpener(), new NullLog(), stagingDirectory: owned);
        WorkspaceInfo workspace = await service.OpenAsync(path, token);
        VolumeInfo info = workspace.Volumes.Single();
        Require(workspace.State == WorkspaceState.ReadyReadOnly && !workspace.Capabilities.Modify.Available &&
            !workspace.Capabilities.SaveAs.Available && !workspace.Capabilities.CommitInPlace.Available &&
            !info.Capabilities!.Modify.Available, "Owner media must remain noneditable.");
        IReadOnlyList<SearchHit> textHits = await service.SearchAsync(workspace.Id, ".txt", token);
        FileEntry? text = null;
        PreviewResult? preview = null;
        foreach (SearchHit hit in textHits.Where(h => !h.Entry.IsDirectory && h.Entry.Length is > 0 and <= 65536).Take(100))
        {
            PreviewResult candidate = await service.PreviewAsync(workspace.Id, info.Id, hit.Entry.Path, token);
            if (!candidate.Truncated && candidate.Description.Contains("UTF", StringComparison.OrdinalIgnoreCase) &&
                candidate.Text.Length > 20)
            {
                text = hit.Entry;
                preview = candidate;
                break;
            }
        }
        Require(text is not null && preview is not null, "No meaningful bounded ordinary text candidate.");
        var selected = new List<FileEntry> { text! };
        foreach (string known in new[] { "/WINNT/system32/ntdll.dll", "/WINNT/system32/kernel32.dll" })
        {
            SearchHit hit = (await service.SearchAsync(workspace.Id, Path.GetFileName(known), token))
                .Single(h => h.Entry.Path.Equals(known, StringComparison.OrdinalIgnoreCase));
            selected.Add(hit.Entry);
        }
        Require(selected.Sum(entry => entry.Length) <= ExtractionBudget, "Selected extraction exceeds owner budget.");
        report["PreviewCharacters"] = preview!.Text.Length;
        report["SearchHitCount"] = textHits.Count;
        var checks = new List<object>();
        for (int index = 0; index < selected.Count; index++)
        {
            FileEntry entry = selected[index];
            string target = Path.Combine(owned, $"selected-{index + 1:D2}");
            Directory.CreateDirectory(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            PreviewResult hex = await service.ReadHexAsync(workspace.Id, info.Id, entry.Path, 0, token);
            Require(hex.Text.Length > 0, "Hex workflow returned no data.");
            ExtractionResult result = await service.ExtractAsync(workspace.Id, info.Id, [entry], target, cancellationToken: token);
            Require(result.Failures.Count == 0 && result.BytesCompleted == entry.Length, "Selected extraction failed.");
            using FileStream extracted = File.OpenRead(Path.Combine(target, entry.Name));
            string extractedHash = Convert.ToHexString(await SHA256.HashDataAsync(extracted, token));
            (string nativeHash, long nativeBytes) = await NativeHash(path, entry.Path, entry.Length, token);
            using IImageSession reopened = new ImageOpener().OpenReadOnly(path, token);
            object direct = await CompareStream(path, reopened.Volumes.Single().FileSystem, entry.Path, entry.Length, token);
            Require(extractedHash == nativeHash && nativeBytes == entry.Length, "Extracted bytes differ from native bytes.");
            checks.Add(new
            {
                Alias = $"selected-{index + 1:D2}",
                entry.Length,
                ExtractedSha256 = extractedHash,
                NativeSha256 = nativeHash,
                NativeDataResident = await NativeResident(path, entry.Path, token),
                HexCharacters = hex.Text.Length,
                Direct = direct
            });
        }
        report["SelectedContentComparisons"] = checks;
        report["ExtractedBytes"] = selected.Sum(entry => entry.Length);
        report["WorkspaceReadOnly"] = true;
    }

    private static string StrictDiagnostic(IReadOnlyBlockDevice device, CancellationToken token)
    {
        var boot = new byte[512];
        Require(device.Read(0, boot) == boot.Length, "Short boot sector.");
        int sector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        int cluster = checked(sector * boot[13]);
        MethodInfo method = typeof(ImageOpener).Assembly.GetType("SectorWorks.Storage.NtfsValidation")!
            .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { method.Invoke(null, [device, boot, sector, cluster, token]); }
        catch (TargetInvocationException error) when (error.InnerException is ImageFormatException)
        {
            return error.InnerException.Message;
        }
        throw new InvalidDataException("Expected historical strict preflight rejection was not reproduced.");
    }

    private static async Task<object> CompareStream(string image, IReadOnlyFileSystem fs, string path, long length, CancellationToken token)
    {
        Require(length <= ExtractionBudget, "Selected stream exceeds read budget.");
        using Stream stream = fs.OpenRead(path);
        Require(!stream.CanWrite && stream.Length == length, "Read-only stream length mismatch.");
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        (string native, long count) = await NativeHash(image, path, length, token);
        Require(hash == native && count == length, "Production stream differs from independent native stream.");
        return new { Bytes = length, Sha256 = hash, NativeSha256 = native };
    }

    private static async Task<(string Hash, long Bytes)> NativeHash(string image, string path, long length, CancellationToken token)
    {
        using Process process = Start("/usr/bin/ntfscat", image, path);
        Task<string> error = process.StandardError.ReadToEndAsync(token);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long count = 0;
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, token)) != 0)
            {
                count = checked(count + read);
                Require(count <= length, "Native stream exceeds declared size.");
                hash.AppendData(buffer, 0, read);
            }
            await process.WaitForExitAsync(token);
            _ = await error;
            Require(process.ExitCode == 0 && count == length, "Native stream read failed.");
            return (Convert.ToHexString(hash.GetHashAndReset()), count);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
    }

    private static async Task<int> NativeDirectoryCount(string image, string path, CancellationToken token)
    {
        using Process process = Start("/usr/bin/ntfsls", "-p", path, image);
        Task<string> error = process.StandardError.ReadToEndAsync(token);
        int count = 0;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token) is not null)
            {
                Require(++count <= 100000, "Native directory count exceeds bound.");
            }
            await process.WaitForExitAsync(token);
            _ = await error;
            Require(process.ExitCode == 0, "Native directory read failed.");
            return count;
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
    }

    private static async Task<bool> NativeResident(string image, string path, CancellationToken token)
    {
        using Process process = Start("/usr/bin/ntfsinfo", "-F", path, image);
        Task<string> error = process.StandardError.ReadToEndAsync(token);
        bool data = false;
        bool? resident = null;
        int lines = 0;
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(token)) is not null)
            {
                Require(++lines <= 2000, "Native file metadata exceeds bound.");
                if (line.Contains("Dumping attribute", StringComparison.Ordinal))
                {
                    data = line.Contains("$DATA (", StringComparison.Ordinal);
                }
                if (data && line.TrimStart().StartsWith("Resident:", StringComparison.Ordinal))
                {
                    Require(line.TrimEnd().EndsWith("Yes", StringComparison.Ordinal) ||
                        line.TrimEnd().EndsWith("No", StringComparison.Ordinal), "Ambiguous native residence metadata.");
                    resident = line.TrimEnd().EndsWith("Yes", StringComparison.Ordinal);
                }
            }
            await process.WaitForExitAsync(token);
            _ = await error;
            Require(process.ExitCode == 0 && resident.HasValue, "Native file residence could not be qualified.");
            return resident!.Value;
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
    }
    private static async Task<string> SourceHash(string image)
    {
        using FileStream stream = new(image, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task<string> Identity(string path)
    {
        using Process process = Start("/usr/bin/stat", "-Lc", "%d:%i:%s:%y:%z", "--", path);
        string identity = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0, "Unable to identify source.");
        return identity.Trim();
    }

    private static Process Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        return Process.Start(start) ?? throw new IOException("Native diagnostic did not start.");
    }

    private static void ValidateDestination(string image, string destination)
    {
        Require(Directory.Exists(destination) && !Directory.EnumerateFileSystemEntries(destination).Any(),
            "Output destination must be an existing empty private directory.");
        Require(!destination.Split(Path.DirectorySeparatorChar).Any(part => part.Equals("Dropbox", StringComparison.OrdinalIgnoreCase)),
            "Output destination must not be synchronized storage.");
        Require(!destination.StartsWith(Path.GetFullPath(".") + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            destination != Path.GetFullPath("."), "Output must be outside repository.");
        for (DirectoryInfo? current = new(destination); current is not null; current = current.Parent)
        {
            Require(current.LinkTarget is null, "Output path must not traverse links.");
        }
        UnixFileMode permissions = File.GetUnixFileMode(destination);
        Require((permissions & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) == 0, "Output must be owner-only.");
        Require(File.Exists(image) && new FileInfo(image).LinkTarget is null, "Source must be a regular non-link file.");
    }

    private static void Require(bool condition, string diagnostic)
    {
        if (!condition) { throw new InvalidDataException(diagnostic); }
    }

    private sealed class NullLog : IExplorerLog
    {
        public void Write(string operation, string path, string message, string level = "Information") { }
    }
}
