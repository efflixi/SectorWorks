using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;
using SectorWorks.Platform;

namespace SectorWorks.UI.ViewModels;

public sealed partial class WorkspaceViewModel
{
    [ObservableProperty] private EditingSnapshot editingSnapshot = new([], false, false, 0);
    [ObservableProperty] private string saveResult = "No modified image has been saved.";
    [ObservableProperty] private string saveProgress = "";

    public IReadOnlyList<PendingImageChange> PendingChanges => EditingSnapshot.Changes;
    public bool IsModified => EditingSnapshot.IsModified;
    public string EditingState => IsModified ? "Modified — staged, not committed" : "Ready — no staged changes";
    public string PendingSummary => $"{PendingChanges.Count:N0} pending changes; estimated output {EditingSnapshot.EstimatedOutputBytes:N0} bytes. Source remains read-only.";
    private bool EditingReady => !disposed && !IsBusy && explorer is IImageEditingService && dialogs is IImageEditingDialogs;
    private bool SingleEntry => selection.Count == 1 && !selection[0].IsLink;
    public bool CanAdd => EditingReady && Volume is not null && SelectedCapabilities.AddFiles.Available;
    public bool CanReplace => EditingReady && SingleEntry && !selection[0].IsDirectory && SelectedCapabilities.ReplaceFiles.Available;
    public bool CanDelete => EditingReady && SingleEntry && SelectedCapabilities.DeleteFiles.Available;
    public bool CanRename => EditingReady && SingleEntry && SelectedCapabilities.RenameFiles.Available;
    public bool CanCreateDirectory => EditingReady && Volume is not null && SelectedCapabilities.CreateDirectories.Available;
    public bool CanUndo => EditingReady && EditingSnapshot.CanUndo;
    public bool CanRedo => EditingReady && EditingSnapshot.CanRedo;
    public bool CanDiscard => EditingReady && IsModified;
    public bool CanSaveAs => EditingReady && IsModified && Info.Capabilities.SaveAs.Available;
    public string AddReason => EditingReason(SelectedCapabilities.AddFiles, "Stage a host file in the current image directory.");
    public string ReplaceReason => EditingReason(SelectedCapabilities.ReplaceFiles, "Select one regular file to stage replacement content.");
    public string DeleteReason => EditingReason(SelectedCapabilities.DeleteFiles, "Select one entry to stage deletion, including directory descendants.");
    public string RenameReason => EditingReason(SelectedCapabilities.RenameFiles, "Select one entry to rename within its image directory.");
    public string NewDirectoryReason => EditingReason(SelectedCapabilities.CreateDirectories, "Stage a new directory in the current image directory.");
    public string SaveAsReason => EditingReason(Info.Capabilities.SaveAs, "Save staged changes to a NEW image, then reopen and validate it. The source and pending changes are retained.");

    private string EditingReason(Capability capability, string available) =>
        IsBusy ? "Wait for the current operation or cancel it."
        : explorer is not IImageEditingService || dialogs is not IImageEditingDialogs ? "Staged editing service or dialogs are unavailable."
        : !capability.Available ? capability.Reason : available;

    private void ApplyEditingSnapshot(EditingSnapshot snapshot)
    {
        EditingSnapshot = snapshot;
        OnPropertyChanged(nameof(PendingChanges));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EditingState));
        OnPropertyChanged(nameof(PendingSummary));
        OnPropertyChanged(nameof(ContextStatus));
        NotifyEditingCommands();
        UpdateInspector();
    }

    private void NotifyEditingCommands()
    {
        foreach (string name in new[]
        {
            nameof(CanAdd), nameof(CanReplace), nameof(CanDelete), nameof(CanRename), nameof(CanCreateDirectory),
            nameof(CanUndo), nameof(CanRedo), nameof(CanDiscard), nameof(CanSaveAs),
            nameof(AddReason), nameof(ReplaceReason), nameof(DeleteReason), nameof(RenameReason),
            nameof(NewDirectoryReason), nameof(SaveAsReason)
        }) { OnPropertyChanged(name); }
        AddCommand.NotifyCanExecuteChanged();
        ReplaceCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        NewDirectoryCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ReviewChanges() { ToolVisible = true; ToolIndex = 8; }
    [RelayCommand]
    private void ShowValidation() { ToolVisible = true; ToolIndex = 4; }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        if (!CanAdd || Volume is null) { return; }
        string volumeId = Volume.Id;
        string directory = CurrentPath;
        await RunOperationAsync(async token =>
        {
            var editingDialogs = (IImageEditingDialogs)dialogs;
            string? source = await editingDialogs.PickImportFileAsync(token);
            if (source is null) { Status = "Add cancelled — nothing staged."; return; }
            string? name = await editingDialogs.RequestNameAsync("Add file to image", Path.GetFileName(source), token);
            if (name is null) { Status = "Add cancelled — nothing staged."; return; }
            ValidateEntryName(name);
            string path = directory == "/" ? "/" + name : directory + "/" + name;
            await StageAndRefreshAsync(new(ImageChangeKind.AddFile, volumeId, path, SourcePath: source), token);
        });
    }

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private async Task ReplaceAsync()
    {
        if (!CanReplace || Volume is null) { return; }
        string volumeId = Volume.Id;
        FileEntry entry = selection[0];
        await RunOperationAsync(async token =>
        {
            string? source = await ((IImageEditingDialogs)dialogs).PickImportFileAsync(token);
            if (source is null) { Status = "Replace cancelled — nothing staged."; return; }
            await StageAndRefreshAsync(new(ImageChangeKind.ReplaceFile, volumeId, entry.Path, SourcePath: source), token);
        });
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (!CanDelete || Volume is null) { return; }
        string volumeId = Volume.Id;
        FileEntry entry = selection[0];
        await RunOperationAsync(async token =>
        {
            if (!await ((IImageEditingDialogs)dialogs).ConfirmAsync("Stage deletion",
                $"Stage deletion of {entry.Path}{(entry.IsDirectory ? " and all its descendants" : "")}? The source image remains unchanged.",
                "Stage delete", token)) { Status = "Delete cancelled — nothing staged."; return; }
            await StageAndRefreshAsync(new(ImageChangeKind.Delete, volumeId, entry.Path), token);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync()
    {
        if (!CanRename || Volume is null) { return; }
        string volumeId = Volume.Id;
        FileEntry entry = selection[0];
        await RunOperationAsync(async token =>
        {
            string? name = await ((IImageEditingDialogs)dialogs).RequestNameAsync("Rename image entry", entry.Name, token);
            if (name is null) { Status = "Rename cancelled — nothing staged."; return; }
            await StageAndRefreshAsync(new(ImageChangeKind.Rename, volumeId, entry.Path, NewName: name), token);
        });
    }

    [RelayCommand(CanExecute = nameof(CanCreateDirectory))]
    private async Task NewDirectoryAsync()
    {
        if (!CanCreateDirectory || Volume is null) { return; }
        string volumeId = Volume.Id;
        string directory = CurrentPath;
        await RunOperationAsync(async token =>
        {
            string? name = await ((IImageEditingDialogs)dialogs).RequestNameAsync("New image directory", "NEWDIR", token);
            if (name is null) { Status = "New directory cancelled — nothing staged."; return; }
            ValidateEntryName(name);
            string path = directory == "/" ? "/" + name : directory + "/" + name;
            await StageAndRefreshAsync(new(ImageChangeKind.CreateDirectory, volumeId, path), token);
        });
    }

    private async Task StageAndRefreshAsync(ImageChangeRequest request, CancellationToken token)
    {
        EditingSnapshot snapshot = await ((IImageEditingService)explorer).StageAsync(Info.Id, request, token);
        ApplyEditingSnapshot(snapshot);
        await RefreshStagedViewAsync(lifetime.Token);
        ReviewChanges();
        Status = $"Staged {request.Kind}: {request.Path} — source unchanged; use Save As New Image.";
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/') || name.Contains('\\'))
        {
            throw new ArgumentException("Enter one image entry name, not a path.");
        }
    }

    private async Task RefreshStagedViewAsync(CancellationToken token)
    {
        SearchResults = [];
        preview?.Cancel();
        PreviewText = "Staged view changed. Select a file to preview current content.";
        HexText = "Staged view changed. Select Hex View to read current bytes.";
        if (Volume is null) { return; }
        string desiredPath = CurrentPath;
        directories.Clear();
        foreach (StructureNode node in Roots[0].Children) { ResetVolumeNodes(node); }
        string path = "/";
        IReadOnlyList<FileEntry> rows = await explorer.ReadDirectoryAsync(Info.Id, Volume.Id, path, token);
        StructureNode destination = DirectoryNode(Volume, path);
        SetDirectoryChildren(destination, Volume, rows);
        foreach (string component in desiredPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            FileEntry? child = rows.FirstOrDefault(e => e.IsDirectory && !e.IsLink && e.Name.Equals(component, StringComparison.OrdinalIgnoreCase));
            if (child is null) { break; }
            path = child.Path;
            destination = DirectoryNode(Volume, path);
            rows = await explorer.ReadDirectoryAsync(Info.Id, Volume.Id, path, token);
            SetDirectoryChildren(destination, Volume, rows);
        }
        CurrentPath = path;
        SelectedNode = destination;
        for (StructureNode? ancestor = destination.Parent; ancestor is not null; ancestor = ancestor.Parent) { ancestor.IsExpanded = true; }
        ReplaceEntries(rows, preserveSelection: false);
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new("/", "/"));
        string prefix = "";
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries)) { prefix += "/" + part; Breadcrumbs.Add(new(part, prefix)); }
    }

    private void ResetVolumeNodes(StructureNode node)
    {
        if (node.VolumeId is not null && node.Path == "/")
        {
            VolumeInfo volume = Info.Volumes.First(v => v.Id == node.VolumeId);
            directories[(volume.Id, "/")] = node;
            node.InvalidateChildren();
            return;
        }
        foreach (StructureNode child in node.Children) { ResetVolumeNodes(child); }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => !CanUndo ? Task.CompletedTask : ChangeHistoryAsync((editor, token) => editor.UndoAsync(Info.Id, token), "Undone");
    [RelayCommand(CanExecute = nameof(CanRedo))]
    private Task RedoAsync() => !CanRedo ? Task.CompletedTask : ChangeHistoryAsync((editor, token) => editor.RedoAsync(Info.Id, token), "Redone");

    private Task ChangeHistoryAsync(Func<IImageEditingService, CancellationToken, Task<EditingSnapshot>> change, string action) =>
        RunOperationAsync(async token =>
        {
            ApplyEditingSnapshot(await change((IImageEditingService)explorer, token));
            await RefreshStagedViewAsync(lifetime.Token);
            Status = $"{action} — source unchanged.";
        });

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private async Task DiscardAsync()
    {
        if (!CanDiscard) { return; }
        await RunOperationAsync(async token =>
        {
            if (!await ((IImageEditingDialogs)dialogs).ConfirmAsync("Discard pending changes",
                $"Discard all {PendingChanges.Count:N0} staged changes to {Path.GetFileName(Info.Metadata.Path)}? This cannot be undone.",
                "Discard changes", token)) { return; }
            ApplyEditingSnapshot(await ((IImageEditingService)explorer).DiscardAsync(Info.Id, token));
            await RefreshStagedViewAsync(lifetime.Token);
            Status = "Pending changes discarded — source unchanged.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task SaveAsAsync()
    {
        if (!CanSaveAs) { return; }
        ReviewChanges();
        await RunOperationAsync(async token =>
        {
            string? destination = await ((IImageEditingDialogs)dialogs).PickNewImagePathAsync(
                Path.GetFileNameWithoutExtension(Info.Metadata.Path) + "-modified.img", token);
            if (destination is null) { Status = "Save As cancelled — pending changes retained."; return; }
            SaveResult = "Save As in progress — no validated output yet.";
            Validation = SaveResult;
            SaveProgress = "Preparing new image…";
            bool reporting = true;
            try
            {
                var progress = new Progress<OperationProgress>(p =>
                {
                    if (disposed || token.IsCancellationRequested || !reporting) { return; }
                    SaveProgress = $"{p.Stage}: {p.BytesCompleted:N0} bytes, {p.FilesCompleted:N0} files — {p.Path}";
                    Status = SaveProgress;
                });
                SaveImageResult result = await ((IImageEditingService)explorer).SaveAsAsync(Info.Id, destination, progress, token);
                SaveResult = $"Validated new image: {result.Path}\n{result.Length:N0} bytes; {result.FilesValidated:N0} files validated.\n{result.ValidationSummary}\nSource unchanged. Pending changes retained for review/discard.";
                Validation = SaveResult;
                SaveProgress = "Output validation complete.";
                Status = "Save As complete — new output reopened and validated; source unchanged.";
                ToolVisible = true;
                ToolIndex = 4;
            }
            catch (Exception ex)
            {
                SaveResult = $"No successful Save As result. Source unchanged; pending changes retained.\n{ex.Message}\nDestination: {destination}\nIncomplete output cleanup is attempted by the service; inspect any reported residual path before retrying.";
                Validation = SaveResult;
                SaveProgress = ex is OperationCanceledException ? "Save As cancelled." : "Save As failed.";
                throw;
            }
            finally { reporting = false; }
        });
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (disposed) { return false; }
        if (IsBusy)
        {
            Status = "An operation is active. Cancel it or wait before closing this image.";
            return false;
        }
        if (explorer is IImageEditingService editing)
        {
            bool refreshed = false;
            await RunOperationAsync(async token =>
            {
                ApplyEditingSnapshot(await editing.GetEditingSnapshotAsync(Info.Id, token));
                refreshed = true;
            });
            if (!refreshed || disposed) { return false; }
        }
        if (!IsModified) { return true; }
        if (dialogs is not IImageEditingDialogs editingDialogs)
        {
            Status = "Close blocked — pending changes require a discard confirmation dialog.";
            return false;
        }
        bool confirmed = await editingDialogs.ConfirmAsync("Close modified image",
            $"{Path.GetFileName(Info.Metadata.Path)} has {PendingChanges.Count:N0} staged changes. Close and discard them? The source image is unchanged. Save As New Image first if you want a modified copy.",
            "Close and discard", lifetime.Token);
        if (!confirmed) { Status = "Close cancelled — pending changes retained."; }
        return confirmed;
    }
}
