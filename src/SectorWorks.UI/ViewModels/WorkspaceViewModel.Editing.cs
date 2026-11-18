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
    private bool requiresReopen;

    public IReadOnlyList<PendingImageChange> PendingChanges => EditingSnapshot.Changes;
    public bool IsModified => EditingSnapshot.IsModified;
    public string EditingState => IsOpticalRebuild ? IsModified ? "Rebuild Required — changes staged" : "Rebuild Required"
        : IsModified ? "Modified — staged, not committed" : "Ready — no staged changes";
    public bool IsOpticalRebuild => Info.Capabilities.Rebuild.Available;
    public bool CanRebuild => CanSaveAs && IsOpticalRebuild;
    public string SaveAsLabel => Info.Metadata.Format == "BIN/CUE" && IsOpticalRebuild ? "Rebuild to New BIN/CUE Set…" : IsOpticalRebuild ? "Rebuild to New Image…" : "Save As New Image…";
    public string SaveAsToolbarLabel => IsOpticalRebuild ? "Rebuild" : "Save As";
    public string PendingSummary => $"{PendingChanges.Count:N0} pending changes; estimated output {EditingSnapshot.EstimatedOutputBytes:N0} bytes. Source remains read-only.";
    private bool EditingReady => !disposed && !requiresReopen && !IsBusy && explorer is IImageEditingService && dialogs is IImageEditingDialogs;
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
    public bool CanCommit => EditingReady && IsModified && explorer is IImageCommitService
        && Info.Capabilities.CommitInPlace.Available
        && PendingChanges.All(change => Info.Volumes.Single(v => v.Id == change.VolumeId).Capabilities?.CommitInPlace.Available == true);
    public string CommitReason
    {
        get
        {
            if (explorer is not IImageCommitService) { return "The commit service is unavailable."; }
            VolumeInfo? blocked = Info.Volumes.FirstOrDefault(v => v.Capabilities?.CommitInPlace.Available != true
                && PendingChanges.Any(c => c.VolumeId == v.Id));
            Capability capability = blocked is null ? Info.Capabilities.CommitInPlace
                : blocked.Capabilities?.CommitInPlace ?? new(false, "This volume has no commit qualification.");
            string reason = EditingReason(capability,
                IsModified ? "Commit staged changes to the SOURCE image after confirmation, backup and independent validation (Ctrl+S)."
                    : "Stage changes before committing to the source image.");
            return blocked is null ? reason : blocked.Id + ": " + reason;
        }
    }
    public string AddReason => EditingReason(SelectedCapabilities.AddFiles, "Stage a host file in the current image directory.");
    public string ReplaceReason => EditingReason(SelectedCapabilities.ReplaceFiles, "Select one regular file to stage replacement content.");
    public string DeleteReason => EditingReason(SelectedCapabilities.DeleteFiles, "Select one entry to stage deletion, including directory descendants.");
    public string RenameReason => EditingReason(SelectedCapabilities.RenameFiles, "Select one entry to rename within its image directory.");
    public string NewDirectoryReason => EditingReason(SelectedCapabilities.CreateDirectories, "Stage a new directory in the current image directory.");
    public string SaveAsReason => EditingReason(Info.Capabilities.SaveAs, Info.Metadata.Format == "BIN/CUE"
        ? "Build a new folder containing a validated CUE and its companion files. The source stays unchanged." : IsOpticalRebuild
        ? "Rebuild to a NEW optical image and validate it. Source and pending changes remain; bootability and exact layout are not guaranteed."
        : "Save staged changes to a NEW image, then reopen and validate it. The source and pending changes are retained.");

    private string EditingReason(Capability capability, string available) =>
        requiresReopen ? "This workspace is unavailable. Close it and reopen the source; inspect recovery information first."
        : IsBusy ? "Wait for the current operation or cancel it."
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
            nameof(CanUndo), nameof(CanRedo), nameof(CanDiscard), nameof(CanSaveAs), nameof(CanCommit), nameof(CommitReason),
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
        RebuildCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();
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

    public Task ImportFilesAsync(Func<Task<string?[]>> readSourcePaths)
    {
        if (!CanAdd || Volume is null) { return Task.CompletedTask; }
        string volumeId = Volume.Id;
        string parent = CurrentPath == "/" ? "" : CurrentPath;
        var editor = (IImageEditingService)explorer;
        return RunOperationAsync(async token =>
        {
            string?[] sources = await readSourcePaths();
            token.ThrowIfCancellationRequested();
            if (sources.Length == 0)
            {
                Status = "No files to import. Copy or drag files from your file manager.";
                return;
            }

            int staged = 0;
            int rejected = 0;
            bool cancelled = false;
            var failures = new List<string>();
            Status = $"Importing {sources.Length:N0} files into {parent}/…";
            try
            {
                foreach (string? source in sources)
                {
                    token.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(source) ?? "";
                    try
                    {
                        if (source is null || !Path.IsPathFullyQualified(source))
                        {
                            throw new ArgumentException("Only local files can be imported, not folders or remote items.");
                        }
                        ValidateEntryName(name);
                        ApplyEditingSnapshot(await editor.StageAsync(Info.Id,
                            new(ImageChangeKind.AddFile, volumeId, parent + "/" + name, SourcePath: source), token));
                        staged++;
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
                    {
                        rejected++;
                        if (failures.Count < 20) { failures.Add($"{(name.Length == 0 ? "File" : name)}: {error.Message}"); }
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; }
            finally
            {
                if (staged > 0 && !disposed) { await RefreshStagedViewAsync(lifetime.Token); }
            }
            if (disposed) { return; }
            if (rejected > failures.Count) { failures.Add($"Showing the first {failures.Count:N0} of {rejected:N0} failures."); }
            Error = string.Join("\n", failures);
            Status = $"{(cancelled ? "Import cancelled: " : "")}{staged:N0} staged, {rejected:N0} not added — source unchanged.";
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
        string action = IsOpticalRebuild ? "Rebuild" : "Save As or Commit Changes";
        Status = $"Staged {request.Kind}: {request.Path} — source unchanged; review before {action}.";
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

    [RelayCommand(CanExecute = nameof(CanRebuild))]
    private Task RebuildAsync() => SaveAsAsync();

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task SaveAsAsync()
    {
        if (!CanSaveAs) { return; }
        ReviewChanges();
        string action = IsOpticalRebuild ? "Rebuild" : "Save As";
        await RunOperationAsync(async token =>
        {
            string? destination = await ((IImageEditingDialogs)dialogs).PickNewImagePathAsync(
                Path.GetFileNameWithoutExtension(Info.Metadata.Path) + (IsOpticalRebuild ? "-rebuilt" : "-modified") + (Info.Metadata.Format switch
                {
                    "VHD" => ".vhd",
                    "VHDX" => ".vhdx",
                    "ISO" => ".iso",
                    "UDF" => ".udf",
                    "BIN/CUE" => ".cue",
                    _ => ".img"
                }), token);
            if (destination is null) { Status = $"{action} cancelled — pending changes retained."; return; }
            SaveResult = $"{action} in progress — no validated output yet.";
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
                Status = $"{action} complete — new output reopened and validated; source unchanged.";
                ToolVisible = true;
                ToolIndex = 4;
            }
            catch (Exception ex)
            {
                SaveResult = $"No successful {action} result. Source unchanged; pending changes retained.\n{ex.Message}\nDestination: {destination}\nIncomplete output cleanup is attempted by the service; inspect any reported residual path before retrying.";
                Validation = SaveResult;
                SaveProgress = ex is OperationCanceledException ? $"{action} cancelled." : $"{action} failed.";
                throw;
            }
            finally { reporting = false; }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (!CanCommit) { return; }
        ReviewChanges();
        await RunOperationAsync(async token =>
        {
            if (!await ((IImageEditingDialogs)dialogs).ConfirmAsync("Commit changes to source image",
                $"Replace the source image with {PendingChanges.Count:N0} staged changes?\n\n{Info.Metadata.Path}\n\n"
                + "SectorWorks will lock the source, create a recovery backup beside it, write and independently validate a temporary image, then atomically replace the source. "
                + "Successful completion clears staged history. Save As New Image instead creates a separate copy. "
                + "Cancellation is checked before final replacement; once replacement starts it must finish. Backups and journals may remain on failure.",
                "Commit to source", token))
            {
                Status = "Commit cancelled — pending changes retained; source unchanged.";
                return;
            }
            bool reporting = true;
            try
            {
                SaveResult = "Commit in progress — no success reported yet.";
                Validation = SaveResult;
                var progress = new Progress<OperationProgress>(p =>
                {
                    if (disposed || !reporting) { return; }
                    SaveProgress = $"{p.Stage}: {p.BytesCompleted:N0} bytes — {p.Path}";
                    Status = SaveProgress;
                });
                CommitResult result = await ((IImageCommitService)explorer).CommitAsync(Info.Id, progress, token);
                ApplyEditingSnapshot(await ((IImageEditingService)explorer).GetEditingSnapshotAsync(Info.Id, lifetime.Token));
                Info = Info with { Metadata = Info.Metadata with { ContainerLength = result.Length } };
                await RefreshStagedViewAsync(lifetime.Token);
                UpdateInspector();
                SaveResult = $"Committed source: {result.Path}\n{result.Length:N0} bytes; {result.FilesValidated:N0} files validated.\n"
                    + result.ValidationSummary + $"\nTransaction: {result.TransactionId}\nStaged history cleared; recovery artifacts removed on success.";
                Status = "Commit complete — source replaced and reopened; no pending changes.";
                SaveProgress = "Commit validation and workspace refresh complete.";
            }
            catch (Exception error)
            {
                // Finalization can succeed before bookkeeping/reopen fails. Never promise
                // an unchanged source or retained staging merely from an exception.
                SaveResult = "Commit did not report success. The source may already have been replaced.\n"
                    + error.Message + "\nReview the source, retained backup/journal and Recovery before retrying. Never restore an old backup over a changed source.";
                Error = error.Message;
                SaveProgress = error is OperationCanceledException ? "Commit cancellation requested." : "Commit failed.";
                try
                {
                    ApplyEditingSnapshot(await ((IImageEditingService)explorer).GetEditingSnapshotAsync(Info.Id, lifetime.Token));
                    await RefreshStagedViewAsync(lifetime.Token);
                }
                catch (Exception refreshError)
                {
                    requiresReopen = true;
                    NotifyEditingCommands();
                    Error += "\nClose and reopen the workspace: " + refreshError.Message;
                }
                Status = "Commit stopped — inspect validation/recovery information before retrying.";
            }
            finally
            {
                reporting = false;
                Validation = SaveResult;
                ToolVisible = true;
                ToolIndex = 4;
            }
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
        if (requiresReopen) { return await ConfirmUnavailableCloseAsync(); }
        if (explorer is IImageEditingService editing)
        {
            bool refreshed = false;
            await RunOperationAsync(async token =>
            {
                ApplyEditingSnapshot(await editing.GetEditingSnapshotAsync(Info.Id, token));
                refreshed = true;
            });
            if (disposed) { return false; }
            if (!refreshed)
            {
                requiresReopen = true;
                NotifyEditingCommands();
                return await ConfirmUnavailableCloseAsync();
            }
        }
        if (!IsModified) { return true; }
        if (dialogs is not IImageEditingDialogs editingDialogs)
        {
            Status = "Close blocked — pending changes require a discard confirmation dialog.";
            return false;
        }
        bool confirmed = await editingDialogs.ConfirmAsync("Close modified image",
            $"{Path.GetFileName(Info.Metadata.Path)} has {PendingChanges.Count:N0} staged changes. Close and discard them? Closing does not apply staged changes. Save As New Image first if you want a modified copy.",
            "Close and discard", lifetime.Token);
        if (!confirmed) { Status = "Close cancelled — pending changes retained."; }
        return confirmed;
    }

    private async Task<bool> ConfirmUnavailableCloseAsync()
    {
        if (dialogs is not IImageEditingDialogs editingDialogs) { return false; }
        bool confirmed = await editingDialogs.ConfirmAsync("Close unavailable workspace",
            $"Close {Info.Metadata.Path} and discard any remaining in-memory staged changes? "
            + "The workspace could not be refreshed. A previous commit may already have replaced the source. "
            + "Retained recovery artifacts will stay on disk. Reopen and inspect the source and Recovery before further editing.",
            "Close workspace", lifetime.Token);
        if (!confirmed) { Status = "Close cancelled — workspace and recovery artifacts retained."; }
        return confirmed;
    }
}
