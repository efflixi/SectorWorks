using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SectorWorks.Platform;

namespace SectorWorks.UI;

public sealed class AvaloniaPlatformDialogs(Window owner) : IPlatformDialogs, IImageEditingDialogs
{
    public async Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open image — read-only",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Disk and optical images") { Patterns = ["*.img", "*.ima", "*.raw", "*.vhd", "*.vhdx", "*.iso"] },
                FilePickerFileTypes.All
            ]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Select(file => file.TryGetLocalPath()
            ?? throw new NotSupportedException("Only local image paths are supported.")).ToArray();
    }

    public async Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Extract to folder — existing files are never overwritten",
            AllowMultiple = false
        });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath()
            ?? throw new NotSupportedException("Extraction requires a local destination folder.");
    }

    public async Task<string?> PickImportFileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Stage file from host — source image remains unchanged",
            AllowMultiple = false
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath()
            ?? throw new NotSupportedException("Import requires a local file.");
    }

    public async Task<string?> PickNewImagePathAsync(string suggestedName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As New Image — existing files cannot be overwritten",
            SuggestedFileName = suggestedName,
            DefaultExtension = "img",
            ShowOverwritePrompt = false,
            FileTypeChoices = [new FilePickerFileType("Raw disk image") { Patterns = ["*.img"] }]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file is null ? null : file.TryGetLocalPath()
            ?? throw new NotSupportedException("Save As requires a local destination.");
    }

    public async Task<string?> RequestNameAsync(string title, string initialName, CancellationToken cancellationToken = default)
    {
        var input = new TextBox { Text = initialName, PlaceholderText = "Image entry name" };
        var accept = new Button { Content = "Stage", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = CreateDialog(title, new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Enter one uppercase DOS 8.3 name, not a path: up to 8 characters plus an optional 3-character extension. Use A–Z, 0–9, _ or -. Imported names are not automatically changed; edit them to meet these limits. The image filesystem validates the name.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                input,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, accept } }
            }
        });
        accept.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) { dialog.Close(input.Text); } };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close(null)));
        string? result = await dialog.ShowDialog<string?>(owner);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string acceptLabel, CancellationToken cancellationToken = default)
    {
        var accept = new Button { Content = acceptLabel };
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };
        var dialog = CreateDialog(title, new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, accept } }
            }
        });
        accept.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close(false)));
        bool result = await dialog.ShowDialog<bool>(owner);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static Window CreateDialog(string title, Control content) => new()
    {
        Title = title,
        Width = 470,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new Border { Padding = new Avalonia.Thickness(18), Child = content }
    };
}
