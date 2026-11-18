using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.Storage;

namespace SectorWorks.RuntimeProbe;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["fresh-profile", var profileImage, var expectedData, var profileResult])
        {
            FreshProfile(profileImage, expectedData, profileResult);
            return 0;
        }
        if (args is ["commit-pause", var image, var catalog, var marker, var stage])
        {
            using var service = new ExplorerService(stagingDirectory: Path.Combine(catalog, "spool"),
                recoveryCatalog: new CommitRecoveryCatalog(catalog));
            WorkspaceInfo info = service.OpenAsync(image).GetAwaiter().GetResult();
            service.StageAsync(info.Id, new(ImageChangeKind.CreateDirectory, info.Volumes[0].Id, "/INTERRUPT")).GetAwaiter().GetResult();
            service.CommitAsync(info.Id, new PauseProgress(stage, marker)).GetAwaiter().GetResult();
            Console.Error.WriteLine("Commit unexpectedly returned instead of reaching the interruption barrier.");
            return 1;
        }
        if (args is ["lock-blocked", var source])
        {
            try
            {
                using SourceWriteLock unexpected = SourceWriteLock.Acquire(source);
                Console.Error.WriteLine("An independent writer acquired a supposedly locked source.");
                return 1;
            }
            catch (IOException error)
            {
                Console.WriteLine(error.Message);
                return 0;
            }
        }
        if (args is ["log", var path, var worker, var countText, var ready, var release]
            && int.TryParse(countText, out int count) && count is > 0 and <= 1000)
        {
            File.WriteAllText(ready, worker);
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(release))
            {
                if (DateTime.UtcNow >= deadline) { throw new TimeoutException("The log probe start barrier was not released."); }
                Thread.Sleep(20);
            }
            for (int index = 0; index < count; index++)
            {
                if (index == 0) { File.WriteAllText(ready + ".attempt", worker); }
                new JsonExplorerLog(path).Write("ConcurrentProbe", worker, worker + ":" + index);
            }
            return 0;
        }
        Console.Error.WriteLine("Usage: runtime-probe lock-blocked <source> | log <path> <worker> <count> <ready> <release>");
        return 2;
    }

    private static void FreshProfile(string source, string expectedData, string result)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(expectedData) || Directory.Exists(expectedData))
        { throw new IOException("The fresh-profile probe requires a new absolute Linux data directory."); }
        string application = Path.Combine(expectedData, "SectorWorks");
        if (WorkspaceDataFiles.ApplicationDataDirectory() != application)
        { throw new IOException("Default application data did not resolve to the configured fresh profile."); }
        string original;
        using (FileStream input = File.OpenRead(source)) { original = Convert.ToHexString(SHA256.HashData(input)); }
        var catalog = new CommitRecoveryCatalog();
        if (catalog.DiscoverAsync().GetAwaiter().GetResult().Items.Count != 0 || Directory.Exists(expectedData))
        { throw new IOException("Fresh discovery unexpectedly created data or found recovery work."); }
        var preferences = new JsonPreferencesStore();
        preferences.Save(preferences.Load());
        var layout = new JsonLayoutStore();
        layout.Save(layout.Load());
        new JsonExplorerLog().Write("FreshProfile", "", "Default paths use the configured application-data root.");
        string payload = Path.Combine(Path.GetDirectoryName(result)!, "fresh-profile-input.txt");
        using (var file = new FileStream(payload, FileMode.CreateNew, FileAccess.Write))
        using (var text = new StreamWriter(file)) { text.Write("fresh profile captured payload"); }
        try
        {
            using var service = new ExplorerService();
            WorkspaceInfo info = service.OpenAsync(source).GetAwaiter().GetResult();
            service.StageAsync(info.Id, new(ImageChangeKind.AddFile, info.Volumes[0].Id, "/FIRST.TXT", payload)).GetAwaiter().GetResult();
            if (!Directory.Exists(Path.Combine(application, "WorkspaceData"))
                || Directory.GetFiles(Path.Combine(application, "WorkspaceData"), "*", SearchOption.AllDirectories).Length == 0)
            { throw new IOException("Default staging did not capture content in the private profile."); }
            service.DiscardAsync(info.Id).GetAwaiter().GetResult();
        }
        finally { File.Delete(payload); }
        string id = Guid.NewGuid().ToString("N");
        catalog.Track(id, source + ".sectorworks-journal-" + id);
        RecoveryItem item = catalog.DiscoverAsync().GetAwaiter().GetResult().Items.Single();
        if (!item.CatalogOnly) { throw new IOException("Expected the fresh catalog-only registration."); }
        catalog.CleanAsync(item).GetAwaiter().GetResult();
        using (FileStream input = File.OpenRead(source))
        {
            if (Convert.ToHexString(SHA256.HashData(input)) != original)
            { throw new IOException("Fresh-profile staging or catalog cleanup changed the image."); }
        }
        string[] expectedFiles = ["preferences.json", "layout.json", "explorer.jsonl"];
        if (expectedFiles.Any(name => !File.Exists(Path.Combine(application, name)))
            || Directory.Exists(Path.Combine(Environment.CurrentDirectory, "SectorWorks")))
        { throw new IOException("Default files escaped the configured profile or were not written."); }
        File.WriteAllText(result, JsonSerializer.Serialize(new
        {
            Status = "passed",
            ApplicationDirectory = application,
            SourceSha256 = original,
            RelativeFallbackCreated = false,
            DefaultStagingExercised = true,
            RecoveryRegistrations = catalog.DiscoverAsync().GetAwaiter().GetResult().Items.Count
        }));
    }

    private sealed class PauseProgress(string stage, string marker) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            if (value.Stage != stage) { return; }
            string temporary = marker + ".tmp";
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                using var writer = new StreamWriter(file, leaveOpen: true);
                writer.Write(value.Stage);
                writer.Flush();
                file.Flush(true);
            }
            File.Move(temporary, marker, overwrite: false);
            // The test parent kills this exact owned process. Timeout refuses
            // to continue the transaction if the parent disappears.
            Thread.Sleep(TimeSpan.FromSeconds(60));
            throw new TimeoutException("Interruption parent did not terminate the probe within its finite barrier.");
        }
    }
}
