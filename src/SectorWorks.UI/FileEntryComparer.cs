using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.UI;

public sealed class FileEntryComparer(ApplicationPreferences preferences) : IComparer<FileEntry>
{
    public int Compare(FileEntry? x, FileEntry? y)
    {
        if (ReferenceEquals(x, y)) { return 0; }
        if (x is null) { return -1; }
        if (y is null) { return 1; }
        if (preferences.FoldersFirst && x.IsDirectory != y.IsDirectory) { return x.IsDirectory ? -1 : 1; }
        int result = preferences.SortField switch
        {
            BrowserSortField.Size => x.Length.CompareTo(y.Length),
            BrowserSortField.Type => StringComparer.OrdinalIgnoreCase.Compare(PresentationConverters.FormatType(x), PresentationConverters.FormatType(y)),
            BrowserSortField.Modified => Nullable.Compare(x.ModifiedUtc, y.ModifiedUtc),
            BrowserSortField.Attributes => StringComparer.OrdinalIgnoreCase.Compare(x.Attributes, y.Attributes),
            BrowserSortField.Offset => Nullable.Compare(x.Offset, y.Offset),
            _ => StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name)
        };
        if (result == 0) { result = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name); }
        if (result == 0) { result = StringComparer.Ordinal.Compare(x.Name, y.Name); }
        if (result == 0) { result = StringComparer.Ordinal.Compare(x.Path, y.Path); }
        return preferences.SortDirection == BrowserSortDirection.Descending ? -Math.Sign(result) : result;
    }
}
