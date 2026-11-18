using System.Text.Json;
using System.Text.Json.Serialization;

namespace SectorWorks.Platform;

public sealed class JsonPreferencesStore : IPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private readonly string path;

    public JsonPreferencesStore(string? path = null) =>
        this.path = path ?? Path.Combine(WorkspaceDataFiles.ApplicationDataDirectory(), "preferences.json");

    public ApplicationPreferences Load()
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (FileNotFoundException) { return new ApplicationPreferences(); }
        catch (DirectoryNotFoundException) { return new ApplicationPreferences(); }
        try
        {
            ApplicationPreferences preferences = JsonSerializer.Deserialize<ApplicationPreferences>(json, Options)
                ?? throw new InvalidDataException("SectorWorks preferences contain no object.");
            preferences.Validate();
            return preferences;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("SectorWorks preferences are invalid.", ex);
        }
    }

    public void Save(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string replacement = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(replacement, JsonSerializer.Serialize(preferences, Options));
            File.Move(replacement, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(replacement)) { File.Delete(replacement); }
        }
    }

}
