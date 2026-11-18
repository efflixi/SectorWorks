using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.UI.ViewModels;

public sealed record Breadcrumb(string Title, string Path);

public sealed partial class WorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly IExplorerService explorer;
    private readonly IPlatformDialogs dialogs;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? navigation;
    private CancellationTokenSource? preview;
    private CancellationTokenSource? operation;
    private int busyCount;
    private int selectionVersion;
    private bool disposed;
    private IReadOnlyList<FileEntry> selection = [];
    private readonly Dictionary<(string Volume, string Path), StructureNode> directories = [];
    private ApplicationPreferences browserPreferences;
    public event EventHandler? EntriesReplacing;
    public event EventHandler? EntriesReplaced;

    public WorkspaceViewModel(IExplorerService explorer, IPlatformDialogs dialogs, WorkspaceInfo info, LayoutSettings layout,
        ApplicationPreferences? preferences = null)
    {
        this.explorer = explorer;
        this.dialogs = dialogs;
        browserPreferences = preferences ?? new ApplicationPreferences();
        browserPreferences.Validate();
        Info = info;
        Layout = layout;
        Volume = info.Volumes.FirstOrDefault();
        var root = new StructureNode(Title, info.Metadata.Format is "ISO" or "UDF" or "BIN/CUE" ? "Optical" : "Disk");
        root.Children.Add(new StructureNode("Image information", "Info"));
        foreach (IGrouping<string, PartitionInfo> table in info.Partitions.GroupBy(p => p.Table))
        {
            var tableNode = new StructureNode(table.Key + " partition table", "Partition");
            foreach (PartitionInfo partition in table)
            {
                var node = new StructureNode($"Partition {partition.Index}: {partition.Name} ({partition.Type})", "Partition");
                foreach (VolumeInfo volume in info.Volumes.Where(v => v.Partition?.Index == partition.Index))
                {
                    node.Children.Add(DirectoryNode(volume, "/"));
                }
                tableNode.Children.Add(node);
            }
            root.Children.Add(tableNode);
        }
        if (info.OpticalDisc is { } disc) { AddDiscNodes(root, disc); }
        else
        {
            foreach (VolumeInfo volume in info.Volumes.Where(v => v.Partition is null))
            {
                root.Children.Add(DirectoryNode(volume, "/"));
            }
        }
        if (info.Metadata.Format is "ISO" or "UDF")
        {
            root.Children.Add(new StructureNode("Optical sessions / tracks — unavailable", "Info")
            {
                Description = "No sessions or tracks have been enumerated. Open a CUE sheet to browse its track layout or play supported CD audio.\n"
                    + "You can still browse supported optical filesystems."
            });
        }
        if (info.Partitions.Count > 0)
        {
            long end = 0;
            var ranges = new StructureNode("Unpartitioned ranges (may contain disk metadata)", "Partition");
            foreach (PartitionInfo partition in info.Partitions.OrderBy(p => p.Offset))
            {
                if (partition.Offset > end)
                {
                    ranges.Children.Add(new StructureNode($"Offset {end:N0} — {partition.Offset - end:N0} bytes", "Info"));
                }
                end = Math.Max(end, checked(partition.Offset + partition.Length));
            }
            if (end < info.Metadata.VirtualLength)
            {
                ranges.Children.Add(new StructureNode($"Offset {end:N0} — {info.Metadata.VirtualLength - end:N0} bytes", "Info"));
            }
            if (ranges.Children.Count > 0) { root.Children.Add(ranges); }
        }
        root.Children.Add(new StructureNode("Sector / hex viewer", "Hex"));
        root.IsExpanded = true;
        Roots.Add(root);
        Validation = info.Warnings.Count == 0
            ? "No opening warnings reported. This is not a full filesystem consistency or bootability validation."
            : string.Join(Environment.NewLine, info.Warnings);
        UpdateInspector();
    }

    public WorkspaceInfo Info { get; private set; }
    public ApplicationPreferences BrowserPreferences => browserPreferences;
    public string Title => Path.GetFileName(Info.Metadata.Path) + (IsModified ? " *" : "");
    public Geometry Icon => Icons.Get(Info.Metadata.Format is "ISO" or "UDF" or "BIN/CUE" ? "Optical" : "Disk");
    public LayoutSettings Layout { get; set; }
    public ObservableCollection<StructureNode> Roots { get; } = [];
    public ObservableCollection<Breadcrumb> Breadcrumbs { get; } = [];
    [ObservableProperty] private IReadOnlyList<FileEntry> entries = [];
    [ObservableProperty] private IReadOnlyList<SearchHit> searchResults = [];
    public IReadOnlyList<FileEntry> Selection => selection;
    public WorkspaceCapabilities SelectedCapabilities => Volume?.Capabilities ?? Info.Capabilities;
    public bool CanExtract => !disposed && Info.Capabilities.Extract.Available && SelectedCapabilities.Extract.Available
        && Volume is not null && selection.Count > 0 && !IsBusy;
    public bool CanPreview => Info.Capabilities.Preview.Available && SelectedCapabilities.Preview.Available && Volume is not null;
    public bool HasPreviousHex => HexOffset > 0;
    public bool HasNextHex => HexOffset < Math.Max(0,
        (selection.Count == 1 && !selection[0].IsDirectory ? selection[0].Length : RawHexLength) - 4096);
    public string ContextStatus => $"{Title} | {Info.Metadata.VirtualLength:N0} bytes | {Volume?.FileSystem ?? "No filesystem"} | {selection.Count} selected | Sector {Info.Metadata.SectorSize} B | {EditingState} | Source read-only";
    private CdTrack? HexTrack => SelectedNode?.Kind == "Hex" ? null
        : SelectedNode?.Track ?? (Volume is null ? null : CdTracks.FirstOrDefault(track => track.VolumeId == Volume.Id));
    private long RawHexLength => HexTrack is { } track ? track.SectorSize * (long)track.SectorCount
        : Info.Metadata.HexLength ?? Info.Metadata.VirtualLength;
    [ObservableProperty] private VolumeInfo? volume;
    [ObservableProperty] private StructureNode? selectedNode;
    [ObservableProperty] private string currentPath = "/";
    [ObservableProperty] private string status = "Ready";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string previewText = "Select a file for bounded, non-executing preview.";
    [ObservableProperty] private string hexText = "Select Hex View to read a bounded page.";
    [ObservableProperty] private long hexOffset;
    [ObservableProperty] private string properties = "";
    [ObservableProperty] private string inspector = "";
    [ObservableProperty] private string validation = "";
    [ObservableProperty] private string query = "";
    [ObservableProperty] private int toolIndex;
    [ObservableProperty] private bool toolVisible = true;

    partial void OnVolumeChanged(VolumeInfo? value)
    {
        OnPropertyChanged(nameof(SelectedCapabilities));
        OnPropertyChanged(nameof(CanExtract));
        OnPropertyChanged(nameof(CanPreview));
        ExtractCommand.NotifyCanExecuteChanged();
        NotifyEditingCommands();
    }

    partial void OnHexOffsetChanged(long value)
    {
        NextHexCommand.NotifyCanExecuteChanged();
        PreviousHexCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExtract));
        ExtractCommand.NotifyCanExecuteChanged();
        ExportCdAudioCommand.NotifyCanExecuteChanged();
        NotifyEditingCommands();
    }

    public async Task InitializeAsync()
    {
        if (explorer is IImageEditingService editing)
        {
            ApplyEditingSnapshot(await editing.GetEditingSnapshotAsync(Info.Id, lifetime.Token));
        }
        if (Volume is not null) { await NavigateAsync("/"); }
    }

    private StructureNode DirectoryNode(VolumeInfo volume, string path, CdTrack? track = null)
    {
        if (directories.TryGetValue((volume.Id, path), out StructureNode? existing)) { return existing; }
        string title = track is not null ? TrackTitle(track)
            : path == "/" ? $"{volume.FileSystem} — {volume.Label} /" : path.Split('/').Last();
        var result = new StructureNode(title, track is null ? "Folder" : "Optical", volume.Id, path,
            async node =>
            {
                try
                {
                    IReadOnlyList<FileEntry> children = await explorer.ReadDirectoryAsync(Info.Id, volume.Id, path, lifetime.Token);
                    if (disposed) { return false; }
                    SetDirectoryChildren(node, volume, children);
                    return true;
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { Error = ex.Message; return false; }
            })
        { Track = track };
        directories.Add((volume.Id, path), result);
        return result;
    }

    private void SetDirectoryChildren(StructureNode node, VolumeInfo volume, IReadOnlyList<FileEntry> entries) =>
        node.SetLoadedChildren(entries.Where(e => e.IsDirectory && !e.IsLink)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => DirectoryNode(volume, e.Path)).ToArray());

    private async Task<StructureNode> ResolveDirectoryAsync(VolumeInfo volume, string path, CancellationToken token)
    {
        StructureNode node = DirectoryNode(volume, "/");
        string prefix = "";
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!await node.EnsureLoadedAsync(token))
            {
                throw new IOException("Could not load directory ancestry for " + path);
            }
            token.ThrowIfCancellationRequested();
            prefix += "/" + part;
            node = node.Children.FirstOrDefault(n => n.Path == prefix && n.VolumeId == volume.Id)
                ?? throw new IOException("Directory is not present in the image structure: " + prefix);
        }
        return node;
    }

    public void ApplyPreferences(ApplicationPreferences preferences)
    {
        preferences.Validate();
        browserPreferences = preferences;
        OnPropertyChanged(nameof(BrowserPreferences));
        ReplaceEntries(Entries, preserveSelection: true);
    }

    public void SortBy(BrowserSortField field)
    {
        BrowserSortDirection direction = browserPreferences.SortField == field
            && browserPreferences.SortDirection == BrowserSortDirection.Ascending
            ? BrowserSortDirection.Descending : BrowserSortDirection.Ascending;
        ApplyPreferences(browserPreferences with { SortField = field, SortDirection = direction });
    }

    private void ReplaceEntries(IReadOnlyList<FileEntry> entries, bool preserveSelection)
    {
        FileEntry[] sorted = entries.Order(new FileEntryComparer(browserPreferences)).ToArray();
        EntriesReplacing?.Invoke(this, EventArgs.Empty);
        Entries = sorted;
        if (!preserveSelection) { SelectEntries([]); }
        EntriesReplaced?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SelectNodeAsync(StructureNode? node)
    {
        if (node is null || disposed) { return; }
        if (node.Track is { } track)
        {
            await SelectTrackNodeAsync(node, track);
            return;
        }
        if (node.VolumeId is not null && node.Path is not null)
        {
            await NavigateToVolumeAsync(Info.Volumes.First(v => v.Id == node.VolumeId), node.Path);
            OnPropertyChanged(nameof(SelectedNode));
        }
        else if (node.Kind == "Hex")
        {
            SelectedNode = node;
            SelectEntries([]);
            HexOffset = 0;
            await HexAsync();
        }
        else
        {
            if (node.Description is not null) { Properties = node.Description; }
            UpdateInspector();
            ToolVisible = true;
            ToolIndex = 2;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task NavigateAsync(string? path) => NavigateToVolumeAsync(Volume, path);

    private async Task NavigateToVolumeAsync(VolumeInfo? targetVolume, string? path)
    {
        if (targetVolume is null || disposed || path is null) { return; }
        navigation?.Cancel();
        preview?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        navigation = source;
        await RunAsync(async token =>
        {
            IReadOnlyList<FileEntry> entries = await explorer.ReadDirectoryAsync(Info.Id, targetVolume.Id, path, token);
            token.ThrowIfCancellationRequested();
            StructureNode destination = await ResolveDirectoryAsync(targetVolume, path, token);
            token.ThrowIfCancellationRequested();
            if (source != navigation || disposed) { return; }
            Volume = targetVolume;
            CurrentPath = path;
            SetDirectoryChildren(destination, targetVolume, entries);
            ReplaceEntries(entries, preserveSelection: false);
            SelectedNode = destination;
            for (StructureNode? ancestor = destination.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                ancestor.IsExpanded = true;
            }
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new Breadcrumb("/", "/"));
            string prefix = "";
            foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                prefix += "/" + part;
                Breadcrumbs.Add(new Breadcrumb(part, prefix));
            }
            Status = $"Ready — {Entries.Count:N0} items";
        }, source.Token, () => navigation == source);
        OnPropertyChanged(nameof(SelectedNode));
        if (navigation == source) { navigation = null; }
        source.Dispose();
    }

    [RelayCommand]
    private Task UpAsync() => NavigateAsync(CurrentPath == "/" ? "/" :
        CurrentPath[..Math.Max(0, CurrentPath.LastIndexOf('/'))] is { Length: > 0 } parent ? parent : "/");

    [RelayCommand]
    private Task RefreshAsync() => NavigateAsync(CurrentPath);

    [RelayCommand]
    private Task ActivateAsync(FileEntry? entry)
    {
        if (entry?.IsLink == true)
        {
            Error = "SectorWorks does not follow links in image filesystems.";
            return Task.CompletedTask;
        }
        return entry is { IsDirectory: true } ? NavigateAsync(entry.Path) : PreviewAsync();
    }

    public void SelectEntries(IEnumerable<FileEntry> entries)
    {
        selection = entries.ToArray();
        selectionVersion++;
        preview?.Cancel();
        HexOffset = 0;
        PreviewText = selection.Count == 1 && selection[0].IsLink
            ? "Links are not previewed or executed."
            : !CanPreview ? "Preview unavailable: " + SelectedCapabilities.Preview.Reason
            : "Select a regular file for bounded, non-executing preview.";
        HexText = "Select Hex View to read a bounded page for the current selection.";
        UpdateSelectionState();
        if (selection.Count == 1 && !selection[0].IsDirectory)
        {
            PreviewCommand.Execute(null);
        }
    }

    private void UpdateSelectionState()
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(CanExtract));
        OnPropertyChanged(nameof(ContextStatus));
        ExtractCommand.NotifyCanExecuteChanged();
        NextHexCommand.NotifyCanExecuteChanged();
        PreviousHexCommand.NotifyCanExecuteChanged();
        NotifyEditingCommands();
        Properties = selection.Count == 0 ? "No file selected." : string.Join("\n\n", selection.Take(32).Select(e =>
            $"Name: {e.Name}\nPath: {e.Path}\nType: {(e.IsDirectory ? "Directory" : "File")}\nSize: {e.Length:N0} bytes\nModified: {PresentationConverters.FormatModified(e.ModifiedUtc)}\nAttributes: {e.Attributes}\nShort name: {e.ShortName ?? "Not reported"}\nOffset: {e.Offset?.ToString() ?? "Not reported"}\nLink: {e.IsLink}"))
            + (selection.Count > 32 ? $"\n\nShowing properties for the first 32 of {selection.Count:N0} selected entries." : "");
        UpdateInspector();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PreviewAsync()
    {
        FileEntry? entry = selection.Count == 1 ? selection[0] : null;
        VolumeInfo? targetVolume = Volume;
        if (entry is null || entry.IsDirectory || entry.IsLink || targetVolume is null || !CanPreview) { return; }
        preview?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        preview = source;
        await RunAsync(async token =>
        {
            PreviewResult result = await explorer.PreviewAsync(Info.Id, targetVolume.Id, entry.Path, token);
            token.ThrowIfCancellationRequested();
            if (source != preview || disposed) { return; }
            PreviewText = result.Description + (result.Truncated ? " — truncated" : "") + "\n\n" + result.Text;
            ToolVisible = true;
            ToolIndex = 0;
        }, source.Token, () => preview == source);
        if (preview == source) { preview = null; }
        source.Dispose();
    }

    [RelayCommand]
    private async Task HexAsync()
    {
        preview?.Cancel();
        FileEntry? entry = selection.Count == 1 && !selection[0].IsDirectory ? selection[0] : null;
        int version = selectionVersion;
        await RunOperationAsync(async token =>
        {
            PreviewResult result = entry is null && HexTrack is { } track && explorer is ICdTrackSource source
                ? await source.ReadTrackHexAsync(Info.Id, track.Number, HexOffset, token)
                : await explorer.ReadHexAsync(Info.Id, entry is null ? null : Volume?.Id, entry?.Path, HexOffset, token);
            token.ThrowIfCancellationRequested();
            if (version != selectionVersion) { return; }
            HexText = $"{result.Description}\nOffset: {HexOffset:N0} bytes\n\n{result.Text}";
            ToolVisible = true;
            ToolIndex = 1;
        });
    }

    [RelayCommand(CanExecute = nameof(HasNextHex))]
    private async Task NextHexAsync() { HexOffset = checked(HexOffset + 4096); await HexAsync(); }
    [RelayCommand(CanExecute = nameof(HasPreviousHex))]
    private async Task PreviousHexAsync() { HexOffset = Math.Max(0, HexOffset - 4096); await HexAsync(); }
    [RelayCommand]
    private void PropertiesView() { ToolVisible = true; ToolIndex = 2; }
    [RelayCommand]
    private void ToggleTools() => ToolVisible = !ToolVisible;

    [RelayCommand]
    private async Task SearchAsync()
    {
        string query = Query;
        if (string.IsNullOrWhiteSpace(query)) { return; }
        await RunOperationAsync(async token =>
        {
            Status = "Searching all readable volumes…";
            IReadOnlyList<SearchHit> hits = await explorer.SearchAsync(Info.Id, query, token);
            token.ThrowIfCancellationRequested();
            SearchResults = hits;
            ToolVisible = true;
            ToolIndex = 3;
            Status = $"Search complete — {hits.Count:N0} matches across this image";
        });
    }

    [RelayCommand]
    private async Task NavigateHitAsync(SearchHit? hit)
    {
        if (hit is null) { return; }
        VolumeInfo targetVolume = Info.Volumes.First(v => v.Id == hit.VolumeId);
        string path = hit.Entry.IsDirectory ? hit.Entry.Path : hit.Entry.Path[..Math.Max(0, hit.Entry.Path.LastIndexOf('/'))];
        path = path.Length == 0 ? "/" : path;
        await NavigateToVolumeAsync(targetVolume, path);
        if (!hit.Entry.IsDirectory && Volume == targetVolume && CurrentPath == path && Error.Length == 0)
        {
            SelectEntries([hit.Entry]);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractAsync()
    {
        FileEntry[] selected = selection.ToArray();
        VolumeInfo? targetVolume = Volume;
        if (targetVolume is null) { return; }
        await RunOperationAsync(async token =>
        {
            string? destination = await dialogs.PickExtractionFolderAsync(token);
            if (destination is null) { Status = "Extraction cancelled."; return; }
            Status = "Extracting — existing files are never overwritten…";
            var progress = new Progress<OperationProgress>(p =>
            {
                if (!disposed && !token.IsCancellationRequested)
                {
                    Status = $"{p.Stage}: {p.FilesCompleted:N0} files, {p.BytesCompleted:N0} bytes — {p.Path}";
                }
            });
            ExtractionResult result = await explorer.ExtractAsync(Info.Id, targetVolume.Id, selected, destination, progress, token);
            token.ThrowIfCancellationRequested();
            Status = $"Extraction finished: {result.FilesCompleted:N0} files, {result.BytesCompleted:N0} bytes; {result.Failures.Count:N0} failures.";
            Error = string.Join("\n", result.Failures.Take(100).Select(f => $"{f.Path}: {f.Error}"))
                + (result.Failures.Count > 100 ? $"\nShowing the first 100 of {result.Failures.Count:N0} failures." : "");
        });
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> action)
    {
        operation?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = source;
        await RunAsync(action, source.Token, () => operation == source);
        if (operation == source) { operation = null; }
        source.Dispose();
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token, Func<bool>? isCurrent = null)
    {
        if (disposed) { return; }
        busyCount++;
        IsBusy = true;
        Error = "";
        try { await action(token); }
        catch (OperationCanceledException) { if (!disposed && isCurrent?.Invoke() != false) { Status = "Cancelled — source remains unchanged; completed extracted files may remain. Save As never modifies the source."; } }
        catch (Exception ex) { if (!disposed && isCurrent?.Invoke() != false) { Error = ex.Message; Status = "Operation failed — source remains read-only."; } }
        finally { busyCount--; IsBusy = busyCount > 0; }
    }

    private void UpdateInspector()
    {
        var text = new StringBuilder();
        text.AppendLine("IMAGE DETAILS");
        text.AppendLine($"Path: {Info.Metadata.Path}\nFormat: {Info.Metadata.Format}\nState: {EditingState}; source read-only\nContainer: {Info.Metadata.ContainerLength:N0} bytes\nVirtual: {Info.Metadata.VirtualLength:N0} bytes\nSector: {Info.Metadata.SectorSize} bytes");
        foreach ((string key, string value) in Info.Metadata.Properties) { text.AppendLine($"{key}: {value}"); }
        foreach (PartitionInfo partition in Info.Partitions)
        {
            text.AppendLine($"\n{partition.Table} PARTITION {partition.Index}\nName: {partition.Name}\nType: {partition.Type}\nOffset: {partition.Offset:N0}\nLength: {partition.Length:N0}\nActive: {partition.IsActive}\nUnique ID: {partition.UniqueId}");
        }
        if (Volume is not null)
        {
            text.AppendLine($"\nFILESYSTEM\n{Volume.FileSystem}: {Volume.Label}");
            foreach ((string key, string value) in Volume.Properties) { text.AppendLine($"{key}: {value}"); }
        }
        text.AppendLine("\nCAPABILITIES — SELECTED VOLUME / IMAGE STACK");
        WorkspaceCapabilities c = SelectedCapabilities;
        Capability audio = SelectedCdTrack?.Playback ?? c.PlayAudioTrack;
        foreach ((string name, Capability cap) in new (string, Capability)[]
        {
            ("Browse", c.Browse), ("Extract", c.Extract), ("Preview", c.Preview), ("Validate", c.Validate),
            ("Modify", c.Modify), ("Add files", c.AddFiles), ("Replace files", c.ReplaceFiles),
            ("Delete files", c.DeleteFiles), ("Rename files", c.RenameFiles),
            ("Create directories", c.CreateDirectories), ("Edit metadata", c.EditMetadata),
            ("Save as new image", c.SaveAs), ("Commit in place", c.CommitInPlace),
            ("Rebuild", c.Rebuild), ("Audio playback", audio),
            ("Preserve bootability", c.PreserveBootability), ("Preserve exact layout", c.PreserveExactLayout)
        })
        {
            text.AppendLine($"{name}: {(cap.Available ? "Yes" : "No")} — {cap.Reason}");
        }
        text.AppendLine("\nSELECTED ENTRY\n" + Properties);
        Inspector = text.ToString();
        OnPropertyChanged(nameof(ContextStatus));
    }

    public void Cancel() { navigation?.Cancel(); preview?.Cancel(); operation?.Cancel(); }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        StopAudioForWorkspace();
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
