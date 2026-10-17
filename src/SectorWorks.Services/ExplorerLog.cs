using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SectorWorks.Services;

public interface IExplorerLog
{
    void Write(string stage, string path, string message, string level = "Information");
}

public sealed class JsonExplorerLog(string? path = null) : IExplorerLog
{
    private readonly string path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SectorWorks", "explorer.jsonl"));

    public void Write(string stage, string path, string message, string level = "Information")
    {
        var item = new { TimestampUtc = DateTime.UtcNow, Level = level, Stage = stage, Path = path, Message = message };
        string line = JsonSerializer.Serialize(item);
        using var mutex = new Mutex(false, MutexNameFor(this.path));
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException error)
            {
                acquired = true;
                throw new IOException("A log writer was interrupted; inspect the log before retrying.", error);
            }
            if (!acquired) { throw new IOException("Timed out waiting for another SectorWorks log writer: " + this.path); }
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            if (File.Exists(this.path) && new FileInfo(this.path).Length > 4 * 1024 * 1024)
            {
                File.Move(this.path, this.path + ".previous", overwrite: true);
            }
            File.AppendAllText(this.path, line + Environment.NewLine);
        }
        finally
        {
            if (acquired) { mutex.ReleaseMutex(); }
        }
    }

    internal static string MutexNameFor(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string identity = OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
        return @"Global\SectorWorks.ExplorerLog." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
