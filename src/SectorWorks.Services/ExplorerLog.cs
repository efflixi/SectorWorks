using System.Text.Json;

namespace SectorWorks.Services;

public interface IExplorerLog
{
    void Write(string stage, string path, string message, string level = "Information");
}

public sealed class JsonExplorerLog(string? path = null) : IExplorerLog
{
    private readonly object gate = new();
    private readonly string path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SectorWorks", "explorer.jsonl");

    public void Write(string stage, string path, string message, string level = "Information")
    {
        var item = new { TimestampUtc = DateTime.UtcNow, Level = level, Stage = stage, Path = path, Message = message };
        string line = JsonSerializer.Serialize(item);
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(this.path))!);
            if (File.Exists(this.path) && new FileInfo(this.path).Length > 4 * 1024 * 1024)
            {
                File.Move(this.path, this.path + ".previous", overwrite: true);
            }
            File.AppendAllText(this.path, line + Environment.NewLine);
        }
    }
}
