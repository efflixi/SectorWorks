namespace SectorWorks.Platform;

public enum BrowserSortField { Name, Size, Type, Modified, Attributes, Offset }
public enum BrowserSortDirection { Ascending, Descending }

public sealed record ApplicationPreferences(
    BrowserSortField SortField = BrowserSortField.Name,
    BrowserSortDirection SortDirection = BrowserSortDirection.Ascending,
    bool FoldersFirst = true)
{
    public void Validate()
    {
        if (!Enum.IsDefined(SortField) || !Enum.IsDefined(SortDirection))
        {
            throw new InvalidDataException("SectorWorks preferences contain an unsupported browser sorting option.");
        }
    }
}

public interface IPreferencesStore
{
    ApplicationPreferences Load();
    void Save(ApplicationPreferences preferences);
}
