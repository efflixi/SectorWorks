using System.Text.Json;

namespace SectorWorks.Platform;

public sealed class JsonLayoutStore : ILayoutStore
{
    private readonly string path;

    public JsonLayoutStore(string? path = null)
    {
        this.path = path ?? Path.Combine(WorkspaceDataFiles.ApplicationDataDirectory(), "layout.json");
    }

    public LayoutSettings Load()
    {
        if (!File.Exists(path))
        {
            return new LayoutSettings();
        }

        LayoutSettings settings = JsonSerializer.Deserialize<LayoutSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("SectorWorks layout settings contain no object.");
        Validate(settings);
        return settings;
    }

    public void Save(LayoutSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void Validate(LayoutSettings settings)
    {
        if (!double.IsFinite(settings.LeftWidth) || settings.LeftWidth is < 160 or > 800
            || !double.IsFinite(settings.RightWidth) || settings.RightWidth is < 180 or > 800
            || !double.IsFinite(settings.ToolHeight) || settings.ToolHeight is < 100 or > 800)
        {
            throw new InvalidDataException("SectorWorks pane dimensions are outside supported bounds.");
        }
    }
}
