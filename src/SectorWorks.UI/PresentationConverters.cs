using System.Globalization;
using Avalonia.Data.Converters;
using SectorWorks.Core;

namespace SectorWorks.UI;

public static class PresentationConverters
{
    public static FuncValueConverter<FileEntry, string> EntryType { get; } = new(entry => entry is null ? "" : FormatType(entry));
    public static FuncValueConverter<DateTime?, string> Modified { get; } = new(FormatModified);
    public static FuncValueConverter<bool, string> ChangeMarker { get; } = new(changed => changed ? "[staged]" : "");
    public static string FormatType(FileEntry entry) => entry.IsDirectory ? "Folder" :
        Path.GetExtension(entry.Name) is { Length: > 1 } extension ? extension[1..].ToUpperInvariant() + " file" : "File";

    public static string FormatModified(DateTime? modified)
    {
        if (modified is not DateTime value) { return "Not reported"; }
        string format = value.Kind switch
        {
            DateTimeKind.Utc => "yyyy-MM-dd HH:mm:ss 'UTC'",
            DateTimeKind.Local => "yyyy-MM-dd HH:mm:ss zzz '(local)'",
            _ => "yyyy-MM-dd HH:mm:ss '(timezone unknown)'"
        };
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

}
