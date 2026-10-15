using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DiscUtils.Ntfs;
using SectorWorks.Core;
using SectorWorks.DependencyProbe;
using SectorWorks.Storage;
using Xunit.Abstractions;
using static SectorWorks.StorageTests.StorageTests;

#pragma warning disable CA1416 // Deliberately qualify Windows-annotated existing-volume APIs on this host.

namespace SectorWorks.StorageTests;

public sealed class NtfsExistingVolumeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(NtfsProbeOperation.AddResident)]
    [InlineData(NtfsProbeOperation.AddNonresident)]
    [InlineData(NtfsProbeOperation.AddWithoutShortNames)]
    [InlineData(NtfsProbeOperation.AddUncompressedWithoutShortNames)]
    [InlineData(NtfsProbeOperation.CreateDirectory)]
    [InlineData(NtfsProbeOperation.CreateDirectoryWithoutShortNames)]
    [InlineData(NtfsProbeOperation.ReadSecurity)]
    public void ExistingVolumeCreationAndSecurityHaveAConcretePortableAclBlocker(NtfsProbeOperation operation)
    {
        using var work = new WorkDirectory();
        string source = work.Golden("ntfs.img");
        string before = Hash(source);
        string copy = work.PathFor("probe.img");
        NtfsProbeResult result = ExistingNtfsVolumeProbe.Run(source, copy, operation);
        output.WriteLine($"{operation}: {result.ExceptionType}: {result.Message}\n{result.StackTrace}");
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(result.Succeeded);
            Assert.Equal(typeof(PlatformNotSupportedException).FullName, result.ExceptionType);
            Assert.Contains("Access Control List (ACL)", result.Message);
            Assert.Contains("RawSecurityDescriptor..ctor", result.StackTrace);
            Assert.Contains("NtfsFileSystem.DoGetSecurity", result.StackTrace);
            Assert.DoesNotContain("NtfsFormatter", result.StackTrace);
            Assert.Contains(operation == NtfsProbeOperation.ReadSecurity ? "GetSecurity" :
                operation is NtfsProbeOperation.CreateDirectory or NtfsProbeOperation.CreateDirectoryWithoutShortNames ?
                "CreateDirectory" : "CreateNewFile", result.StackTrace);
        }
        else
        {
            Assert.True(result.Succeeded, $"{result.ExceptionType}: {result.Message}");
        }
        Assert.Equal(before, Hash(source));
        using IImageSession session = new ImageOpener().OpenReadOnly(source);
        Assert.False(Assert.Single(session.Volumes).FileSystem.Capabilities.Modify.Available);
    }

    [Theory]
    [InlineData(NtfsProbeOperation.GrowResident, "SENTINEL.TXT", "SENTINEL.TXT")]
    [InlineData(NtfsProbeOperation.ShrinkNonresident, "LARGE.BIN", "LARGE.BIN")]
    [InlineData(NtfsProbeOperation.DeleteResident, "SENTINEL.TXT", null)]
    [InlineData(NtfsProbeOperation.DeleteNonresident, "LARGE.BIN", null)]
    [InlineData(NtfsProbeOperation.RenameResidentUnicode, "SENTINEL.TXT", ExistingNtfsVolumeProbe.UnicodeFileName)]
    [InlineData(NtfsProbeOperation.RenameNonresidentUnicode, "LARGE.BIN", ExistingNtfsVolumeProbe.UnicodeFileName)]
    public void SecurityFreeExistingFilePathsRoundTripButDoNotQualifyAWriter(
        NtfsProbeOperation operation, string original, string? expected)
    {
        using var work = new WorkDirectory();
        string source = work.Golden("ntfs.img");
        string sourceHash = Hash(source);
        string copy = work.PathFor("probe.img");
        byte[] originalSecurity = ReadAttribute(source, original, "$SECURITY_DESCRIPTOR");
        byte[] originalContent = ReadAttribute(source, original, "$DATA");
        byte[] untouchedContent = ReadAttribute(source, original == "LARGE.BIN" ? "SENTINEL.TXT" : "LARGE.BIN", "$DATA");
        int originalClusters = AllocatedBits(ReadAttribute(source, "$Bitmap", "$DATA"));
        int originalRecords = AllocatedBits(ReadAttribute(source, "$MFT", "$BITMAP"));
        DateTime creation;
        FileAttributes attributes;
        using (FileStream stream = File.OpenRead(source))
        using (var fs = new NtfsFileSystem(stream))
        {
            creation = fs.GetCreationTimeUtc(original);
            attributes = fs.GetAttributes(original);
        }
        NtfsProbeResult result = ExistingNtfsVolumeProbe.Run(source, copy, operation);
        Assert.True(result.Succeeded, $"{result.ExceptionType}: {result.Message}\n{result.StackTrace}");
        using (IImageSession session = new ImageOpener().OpenReadOnly(copy))
        {
            IReadOnlyFileSystem fs = Assert.Single(session.Volumes).FileSystem;
            Assert.False(fs.Capabilities.Modify.Available);
            if (expected is null)
            {
                Assert.DoesNotContain(fs.ReadDirectory("/"), e => e.Name == original);
            }
            else
            {
                Assert.Contains(fs.ReadDirectory("/"), e => e.Name == expected);
                using Stream content = fs.OpenRead("/" + expected);
                using var actual = new MemoryStream();
                content.CopyTo(actual);
                byte[] expectedContent = operation == NtfsProbeOperation.GrowResident ? ExistingNtfsVolumeProbe.LargePayload() :
                    operation == NtfsProbeOperation.ShrinkNonresident ? "small"u8.ToArray() : originalContent;
                Assert.Equal(expectedContent, actual.ToArray());
            }
        }
        if (expected is not null)
        {
            Assert.Equal(originalSecurity, ReadAttribute(copy, expected, "$SECURITY_DESCRIPTOR"));
            using FileStream stream = File.OpenRead(copy);
            using var fs = new NtfsFileSystem(stream);
            Assert.Equal(creation, fs.GetCreationTimeUtc(expected));
            Assert.Equal(attributes, fs.GetAttributes(expected));
        }
        Assert.Equal(untouchedContent, ReadAttribute(copy, original == "LARGE.BIN" ? "SENTINEL.TXT" : "LARGE.BIN", "$DATA"));
        int expectedClusterDelta = operation switch
        {
            NtfsProbeOperation.GrowResident => 4,
            NtfsProbeOperation.ShrinkNonresident or NtfsProbeOperation.DeleteNonresident => -2,
            _ => 0
        };
        Assert.Equal(originalClusters + expectedClusterDelta, AllocatedBits(ReadAttribute(copy, "$Bitmap", "$DATA")));
        Assert.Equal(originalRecords - (expected is null ? 1 : 0), AllocatedBits(ReadAttribute(copy, "$MFT", "$BITMAP")));
        AssertNativeChecks(copy, expected);
        AssertNativeAllocationAndIndex(copy, original, expected, operation);
        Assert.Equal(sourceHash, Hash(source));
    }

    [Fact]
    public void FailedCreateCanMutateDisposableMetadataBeforeSecurityInheritanceThrows()
    {
        using var work = new WorkDirectory();
        string source = work.Golden("ntfs.img");
        string before = Hash(source);
        string copy = work.PathFor("failed.img");
        int recordsBefore = AllocatedBits(ReadAttribute(source, "$MFT", "$BITMAP"));
        NtfsProbeResult result = ExistingNtfsVolumeProbe.Run(source, copy, NtfsProbeOperation.AddResident);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(typeof(PlatformNotSupportedException).FullName, result.ExceptionType);
            Assert.NotEqual(before, Hash(copy));
            int recordsAfter = AllocatedBits(ReadAttribute(copy, "$MFT", "$BITMAP"));
            Assert.Equal(recordsBefore + 1, recordsAfter);
            output.WriteLine($"Allocated MFT records before: {recordsBefore}; after: {recordsAfter}");
            if (ToolAvailable("ntfsinfo"))
            {
                string text = RunTool("ntfsinfo", ["-F", "/" + ExistingNtfsVolumeProbe.UnicodeFileName, copy]);
                Assert.Contains("Security ID:\t\t 0 (0x0)", text);
                Assert.DoesNotContain("Dumping attribute $SECURITY_DESCRIPTOR", text);
                Assert.Contains("Data size:\t\t 0 (0x0)", text);
                output.WriteLine(text);
            }
            if (ToolAvailable("ntfsfix")) { output.WriteLine(RunTool("ntfsfix", ["-n", copy])); }
        }
        Assert.Equal(before, Hash(source));
    }

    [Fact]
    public void FreshNativeAuthoredExistingVolumeReproducesCreationBlockerWithoutUsingFormatter()
    {
        if (!ToolAvailable("mkntfs") || !ToolAvailable("ntfscp"))
        {
            output.WriteLine("Native fixture authoring tools unavailable; immutable independently authored fixture probes still run.");
            return;
        }
        using var work = new WorkDirectory();
        string source = work.PathFor("native.img");
        using (FileStream image = File.Create(source)) { image.SetLength(32 * 1024 * 1024); }
        output.WriteLine(RunTool("mkntfs", ["-F", "-Q", "-T", "-L", "SWNTFS", "-s", "512", "-p", "0", "-H", "4", "-S", "32", source]));
        string payload = work.PathFor("payload.txt");
        File.WriteAllText(payload, FixtureCatalog.Sentinel);
        RunTool("ntfscp", ["-f", source, payload, "/SENTINEL.TXT"]);
        File.WriteAllBytes(payload, Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray());
        RunTool("ntfscp", ["-f", source, payload, "/LARGE.BIN"]);
        string before = Hash(source);
        AssertNativeChecks(source, "SENTINEL.TXT");
        foreach (NtfsProbeOperation operation in new[] { NtfsProbeOperation.AddResident, NtfsProbeOperation.CreateDirectory })
        {
            NtfsProbeResult result = ExistingNtfsVolumeProbe.Run(source, work.PathFor(operation + ".img"), operation);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(typeof(PlatformNotSupportedException).FullName, result.ExceptionType);
                Assert.Contains("NtfsFileSystem.DoGetSecurity", result.StackTrace);
            }
            else { Assert.True(result.Succeeded, result.Message); }
            output.WriteLine($"Native-authored {operation}: {result.ExceptionType}: {result.Message}\n{result.StackTrace}");
        }
        Assert.Equal(before, Hash(source));
    }

    [Fact]
    public void ProbeNeverOverwritesSourceOrExistingDestination()
    {
        using var work = new WorkDirectory();
        string source = work.Golden("ntfs.img");
        string before = Hash(source);
        Assert.Throws<IOException>(() => ExistingNtfsVolumeProbe.Run(source, source, NtfsProbeOperation.DeleteResident));
        Assert.Equal(before, Hash(source));
        string destination = work.PathFor("existing.img");
        File.WriteAllText(destination, "existing destination");
        Assert.Throws<IOException>(() => ExistingNtfsVolumeProbe.Run(source, destination, NtfsProbeOperation.DeleteResident));
        Assert.Equal("existing destination", File.ReadAllText(destination));
        Assert.Equal(before, Hash(source));
    }

    private void AssertNativeChecks(string path, string? expected)
    {
        foreach ((string tool, string[] arguments) in new (string, string[])[]
        {
            ("ntfsfix", ["-n", path]),
            ("ntfsinfo", ["-m", path]),
            ("ntfscluster", ["-i", path]),
            ("ntfsinfo", ["-i", "6", path]),
            ("ntfsinfo", ["-i", "0", path]),
            ("ntfsinfo", ["-i", "5", path])
        })
        {
            if (!ToolAvailable(tool)) { output.WriteLine($"{tool}: unavailable; no independent qualification claimed."); continue; }
            string text = RunTool(tool, arguments);
            output.WriteLine($"{tool} {string.Join(' ', arguments[..^1])}:\n{text}");
            if (tool == "ntfsfix")
            {
                Assert.Contains("$MFT and $MFTMirr completed successfully", text);
                Assert.Contains("Checking the alternate boot sector... OK", text);
            }
        }
        if (expected is not null && ToolAvailable("ntfsinfo"))
        {
            string text = RunTool("ntfsinfo", ["-F", "/" + expected, path]);
            Assert.Contains($"Filename:\t\t '{expected}'", text);
            Assert.Contains("Owner SID:", text);
            Assert.Contains("Discretionary ACL:", text);
            output.WriteLine(text);
        }
    }

    private void AssertNativeAllocationAndIndex(string path, string original, string? expected, NtfsProbeOperation operation)
    {
        byte[] bitmap = ReadAttribute(path, "$Bitmap", "$DATA");
        byte[] mftBitmap = ReadAttribute(path, "$MFT", "$BITMAP");
        if (ToolAvailable("ntfscat"))
        {
            Assert.Equal(bitmap, RunToolBytes("ntfscat", [path, "/$Bitmap"]));
            Assert.Equal(mftBitmap, RunToolBytes("ntfscat", ["-a", "176", path, "/$MFT"]));
        }
        if (ToolAvailable("ntfsls"))
        {
            string[] names = RunTool("ntfsls", ["-a", "-p", "/", path]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string untouched = original == "LARGE.BIN" ? "SENTINEL.TXT" : "LARGE.BIN";
            Assert.Equal((expected is null ? new[] { untouched } : new[] { untouched, expected }).Order(), names.Order());
        }
        if (expected is null) { return; }
        if (ToolAvailable("ntfsinfo"))
        {
            string text = RunTool("ntfsinfo", ["-F", "/" + expected, path]);
            Match match = Regex.Match(text, @"Dumping Inode (\d+)");
            Assert.True(match.Success, text);
            int inode = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.NotEqual(0, mftBitmap[inode / 8] & (1 << (inode % 8)));
        }
        if (ToolAvailable("ntfscluster"))
        {
            string text = RunTool("ntfscluster", ["-F", "/" + expected, path]);
            output.WriteLine(text);
            int clusters = 0;
            foreach (Match match in Regex.Matches(text, @"^\s+(\d+)\s+(\d+)\s+(\d+)\s*$", RegexOptions.Multiline))
            {
                int lcn = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                int length = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                for (int cluster = lcn; cluster < checked(lcn + length); cluster++)
                {
                    Assert.InRange(cluster / 8, 0, bitmap.Length - 1);
                    Assert.NotEqual(0, bitmap[cluster / 8] & (1 << (cluster % 8)));
                }
                clusters += length;
            }
            int expectedClusters = operation == NtfsProbeOperation.GrowResident ? 4 :
                operation == NtfsProbeOperation.RenameNonresidentUnicode ? 2 : 0;
            Assert.Equal(expectedClusters, clusters);
        }
    }

    private static byte[] ReadAttribute(string path, string name, string attribute)
    {
        using FileStream image = File.OpenRead(path);
        using var fs = new NtfsFileSystem(image);
        using Stream stream = fs.OpenFile($"{name}::{attribute}", FileMode.Open, FileAccess.Read);
        Assert.InRange(stream.Length, 0, 1024 * 1024);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int AllocatedBits(byte[] bitmap) => bitmap.Sum(value => BitOperations.PopCount((uint)value));

    private static bool ToolAvailable(string tool) => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator).Any(directory => File.Exists(Path.Combine(directory, tool)));

    private static string RunTool(string tool, string[] arguments)
        => Encoding.UTF8.GetString(RunToolBytes(tool, arguments));

    private static byte[] RunToolBytes(string tool, string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start)!;
        using var buffer = new MemoryStream();
        Task stdout = process.StandardOutput.BaseStream.CopyToAsync(buffer);
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(true);
            throw new TimeoutException($"Read-only NTFS checker {tool} exceeded 15 seconds.");
        }
        stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"{tool}: exit {process.ExitCode}\n{error}\n{Encoding.UTF8.GetString(buffer.ToArray())}");
        return buffer.ToArray();
    }
}
