using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using static SectorWorks.ServicesTests.EditingTests;

namespace SectorWorks.ServicesTests;

public sealed class FreshProfileTests
{
    [LinuxFreshProfileFact]
    public async Task MissingXdgDataDirectorySupportsDefaultStartupStoresStagingAndRecoveryWithoutRelativeFallback()
    {
        using var work = new EditDirectory();
        string source = work.Golden("fat16.img");
        string data = Path.Combine(work.Path, "not-created-data");
        string result = Path.Combine(work.Path, "fresh-profile-result.json");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { WorkingDirectory = work.Path, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["XDG_DATA_HOME"] = data;
        foreach (string argument in new[]
        {
            "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "SectorWorks.ServicesTests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "SectorWorks.ServicesTests.deps.json"),
            typeof(SectorWorks.RuntimeProbe.Program).Assembly.Location, "fresh-profile", source, data, result
        }) { start.ArgumentList.Add(argument); }
        using Process child = Process.Start(start) ?? throw new IOException("Fresh-profile child could not start.");
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> error = child.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await child.WaitForExitAsync(timeout.Token);
            Assert.True(child.ExitCode == 0, await output + await error);
            using JsonDocument record = JsonDocument.Parse(File.ReadAllText(result));
            Assert.Equal("passed", record.RootElement.GetProperty("Status").GetString());
            Assert.Equal(Path.Combine(data, "SectorWorks"), record.RootElement.GetProperty("ApplicationDirectory").GetString());
            Assert.False(record.RootElement.GetProperty("RelativeFallbackCreated").GetBoolean());
            Assert.True(record.RootElement.GetProperty("DefaultStagingExercised").GetBoolean());
            Assert.Equal(0, record.RootElement.GetProperty("RecoveryRegistrations").GetInt32());
            using FileStream unchanged = File.OpenRead(source);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(unchanged)), record.RootElement.GetProperty("SourceSha256").GetString());
            Assert.False(Directory.Exists(Path.Combine(work.Path, "SectorWorks")));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); }
        }
    }

    public sealed class LinuxFreshProfileFactAttribute : FactAttribute
    {
        public LinuxFreshProfileFactAttribute()
        {
            if (!OperatingSystem.IsLinux()) { Skip = "Fresh XDG profile behavior requires Linux; Windows known-folder resolution is exercised separately."; }
        }
    }
}
