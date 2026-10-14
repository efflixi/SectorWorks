using System.Diagnostics;
using DiscUtils.Fat;
using SectorWorks.DependencyProbe;
using Xunit;
using Xunit.Abstractions;

namespace SectorWorks.DependencyTests;

public sealed class FixtureTests(ITestOutputHelper output)
{
    private static readonly string GoldenRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "golden");

    public static IEnumerable<object[]> ValidFixtures() =>
        FixtureCatalog.ReadManifest(GoldenRoot)
            .Where(e => !e.Name.StartsWith("truncated", StringComparison.Ordinal) && e.Name != "corrupt-fat.img")
            .Select(e => new object[] { e.Name });

    [Fact]
    public void CatalogIncludesEveryFoundationFixtureExactlyOnce()
    {
        string[] expected =
        [
            "fat12.img", "fat16.img", "fat32.img", "mbr.img", "gpt.img", "ntfs.img",
            "joliet.iso", "dynamic.vhd", "dynamic.vhdx", "truncated-fat.img", "corrupt-fat.img",
            "truncated.iso", "truncated.vhd", "truncated.vhdx",
        ];
        Assert.Equal(expected.Order(), FixtureCatalog.ReadManifest(GoldenRoot).Select(e => e.Name).Order());
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void GoldenReopensAndSentinelMatches(string name)
    {
        WithFixture(name, FixtureCatalog.VerifyImage);
    }

    [Theory]
    [InlineData("fat12.img")]
    [InlineData("fat16.img")]
    [InlineData("fat32.img")]
    public void GeneratedDisposableFatSupportsCreateMoveAndReopen(string name)
    {
        WithFixture(name, FixtureCatalog.RoundTripFat);
    }

    [Theory]
    [InlineData("truncated-fat.img", "System.IO.EndOfStreamException")]
    [InlineData("corrupt-fat.img", "DiscUtils.InvalidFileSystemException")]
    [InlineData("truncated.iso", "System.IO.IOException")]
    [InlineData("truncated.vhd", "System.ArgumentOutOfRangeException")]
    [InlineData("truncated.vhdx", "System.IO.EndOfStreamException")]
    public async Task MalformedInputFailsWithObservedErrorWithinProcessBudget(string name, string expectedError)
    {
        string working = NewWorkingDirectory();
        try
        {
            string path = FixtureCatalog.Materialize(GoldenRoot, working,
                FixtureCatalog.ReadManifest(GoldenRoot).Single(e => e.Name == name));
            string probe = typeof(FixtureCatalog).Assembly.Location;
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var start = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add("--runtimeconfig");
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SectorWorks.DependencyTests.runtimeconfig.json"));
            start.ArgumentList.Add(probe);
            start.ArgumentList.Add("malformed");
            start.ArgumentList.Add(path);
            using Process process = Process.Start(start) ?? throw new IOException("Could not start bounded probe.");
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(budget.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("Malformed probe exceeded 15 seconds: " + name);
            }
            string diagnostic = await stderr;
            output.WriteLine(diagnostic);
            output.WriteLine(await stdout);
            Assert.Equal(1, process.ExitCode);
            Assert.StartsWith(expectedError + ":", diagnostic);
        }
        finally
        {
            Directory.Delete(working, recursive: true);
        }
    }

    [Fact]
    public void ReadOnlyFatRejectsWriteAttempt()
    {
        WithFixture("fat12.img", path =>
        {
            using FileStream source = File.OpenRead(path);
            using var fs = new FatFileSystem(source);
            Assert.False(fs.CanWrite);
            Assert.Throws<NotSupportedException>(() => fs.OpenFile("NEW.TXT", FileMode.CreateNew, FileAccess.Write));
        });
    }

    [Fact]
    public void LargeDynamicVhdxReopensWithoutExpandingVirtualSize()
    {
        const long capacity = 1024L * 1024 * 1024 * 1024;
        string working = NewWorkingDirectory();
        try
        {
            string path = Path.Combine(working, "large.vhdx");
            using (FileStream target = File.Create(path))
            using (var disk = DiscUtils.Vhdx.Disk.InitializeDynamic(target, DiscUtils.Streams.Ownership.None, capacity))
            {
                disk.Content.Position = capacity - 512;
                disk.Content.WriteByte(0x5a);
            }
            Assert.True(new FileInfo(path).Length < 64 * 1024 * 1024);
            using FileStream source = File.OpenRead(path);
            using var measured = new MeasuredReadStream(source);
            using var reopened = new DiscUtils.Vhdx.Disk(measured, DiscUtils.Streams.Ownership.None);
            Assert.Equal(capacity, reopened.Capacity);
            reopened.Content.Position = capacity - 512;
            Assert.Equal(0x5a, reopened.Content.ReadByte());
            Assert.True(measured.TotalRead < 16 * 1024 * 1024, $"Read {measured.TotalRead} bytes.");
            Assert.True(measured.LargestRead <= 4 * 1024 * 1024, $"Largest request {measured.LargestRead}.");
            output.WriteLine($"1 TiB virtual / {source.Length} container bytes; read {measured.TotalRead}; largest request {measured.LargestRead}.");
        }
        finally
        {
            Directory.Delete(working, recursive: true);
        }
    }

    private static void WithFixture(string name, Action<string> action)
    {
        string working = NewWorkingDirectory();
        try
        {
            FixtureEntry entry = FixtureCatalog.ReadManifest(GoldenRoot).Single(e => e.Name == name);
            action(FixtureCatalog.Materialize(GoldenRoot, working, entry));
        }
        finally
        {
            Directory.Delete(working, recursive: true);
        }
    }

    private static string NewWorkingDirectory()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixture-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
