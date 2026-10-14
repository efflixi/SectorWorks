using SectorWorks.Platform;

namespace SectorWorks.ServicesTests;

public sealed class PreferencesTests : IDisposable
{
    private readonly string directory = Path.GetFullPath(Path.Combine("artifacts", "preferences-tests", Guid.NewGuid().ToString("N")));
    private string SettingsPath => Path.Combine(directory, "preferences.json");

    [Fact]
    public void MissingPreferencesUseFoldersFirstNameAscending()
    {
        var store = new JsonPreferencesStore(SettingsPath);
        Assert.Equal(new ApplicationPreferences(BrowserSortField.Name, BrowserSortDirection.Ascending, true), store.Load());
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void AllSortingOptionsRoundTripAndReplaceWithoutLeavingScratchFiles()
    {
        var store = new JsonPreferencesStore(SettingsPath);
        foreach (BrowserSortField field in Enum.GetValues<BrowserSortField>())
        {
            foreach (BrowserSortDirection direction in Enum.GetValues<BrowserSortDirection>())
            {
                foreach (bool foldersFirst in new[] { true, false })
                {
                    var preferences = new ApplicationPreferences(field, direction, foldersFirst);
                    store.Save(preferences);
                    Assert.Equal(preferences, new JsonPreferencesStore(SettingsPath).Load());
                    Assert.Single(Directory.GetFiles(directory));
                }
            }
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"SortField\":\"Invalid\"}")]
    [InlineData("{\"SortDirection\":123}")]
    [InlineData("{\"FoldersFirst\":null}")]
    [InlineData("{\"UnknownOption\":true}")]
    public void InvalidFilesAreNotSilentlyReplaced(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, json);
        Assert.Throws<InvalidDataException>(() => new JsonPreferencesStore(SettingsPath).Load());
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void InvalidSaveDoesNotReplaceExistingPreferences()
    {
        var store = new JsonPreferencesStore(SettingsPath);
        var valid = new ApplicationPreferences(BrowserSortField.Size, BrowserSortDirection.Descending, false);
        store.Save(valid);
        string original = File.ReadAllText(SettingsPath);
        Assert.Throws<InvalidDataException>(() => store.Save(valid with { SortField = (BrowserSortField)999 }));
        Assert.Throws<InvalidDataException>(() => store.Save(valid with { SortDirection = (BrowserSortDirection)999 }));
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void ReplacementFailureIsReportedAndReplacementFileIsCleaned()
    {
        Directory.CreateDirectory(SettingsPath);
        Assert.ThrowsAny<IOException>(() => new JsonPreferencesStore(SettingsPath).Save(new ApplicationPreferences()));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void ExistingDirectoryIsAReportedLoadFailureNotANewInstallation()
    {
        Directory.CreateDirectory(SettingsPath);
        Assert.Throws<UnauthorizedAccessException>(() => new JsonPreferencesStore(SettingsPath).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
    }
}
