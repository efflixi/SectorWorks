using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UI;

public sealed partial class WorkspaceView : UserControl
{
    private WorkspaceViewModel? model;
    private bool restoringSelection;
    public WorkspaceView()
    {
        InitializeComponent();
        FileGrid.AddHandler(KeyDownEvent, FilesKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        FileGrid.AddHandler(DragDrop.DragOverEvent, FilesDragOver);
        FileGrid.AddHandler(DragDrop.DropEvent, FilesDrop);
        SearchList.AddHandler(KeyDownEvent, SearchKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) => { SaveLayout(); RestoreLayout(); };
        DetachedFromVisualTree += (_, _) => SaveLayout();
        CenterGrid.SizeChanged += (_, _) => UpdateToolHeight();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible) { RestoreLayout(); }
        };
    }

    private void RestoreLayout()
    {
        restoringSelection = true;
        UnsubscribeModel();
        model = DataContext as WorkspaceViewModel;
        if (model is null) { restoringSelection = false; return; }
        LayoutSettings layout = model.Layout;
        PaneGrid.ColumnDefinitions[0].Width = new GridLength(layout.LeftWidth);
        PaneGrid.ColumnDefinitions[4].Width = new GridLength(layout.RightWidth);
        CenterGrid.RowDefinitions[2].Height = new GridLength(layout.ToolVisible ? layout.ToolHeight : 0);
        model.ToolVisible = layout.ToolVisible;
        model.PropertyChanged += ModelPropertyChanged;
        model.EntriesReplacing += EntriesReplacing;
        model.EntriesReplaced += EntriesReplaced;
        UpdateSortHeaders();
        RestoreFileSelection();
    }

    private void RestoreFileSelection()
    {
        if (model is null) { return; }
        WorkspaceViewModel current = model;
        Dispatcher.UIThread.Post(() =>
        {
            if (model != current) { return; }
            restoringSelection = true;
            FileGrid.SelectedItems.Clear();
            if (current.Selection.Count > 0 && current.Selection.Count == current.Entries.Count)
            {
                FileGrid.SelectAll();
            }
            else if (current.Selection.Count > 0)
            {
                var rows = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
                foreach (FileEntry row in current.Entries) { rows.TryAdd(row.Path, row); }
                foreach (FileEntry entry in current.Selection)
                {
                    if (rows.TryGetValue(entry.Path, out FileEntry? row)) { FileGrid.SelectedItems.Add(row); }
                }
            }
            restoringSelection = false;
        });
    }

    private void ModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.ToolVisible)) { UpdateToolHeight(); }
        if (e.PropertyName == nameof(WorkspaceViewModel.BrowserPreferences)) { UpdateSortHeaders(); }
    }

    private void EntriesReplacing(object? sender, EventArgs e) => restoringSelection = true;
    private void EntriesReplaced(object? sender, EventArgs e) => RestoreFileSelection();

    private void UnsubscribeModel()
    {
        if (model is null) { return; }
        model.PropertyChanged -= ModelPropertyChanged;
        model.EntriesReplacing -= EntriesReplacing;
        model.EntriesReplaced -= EntriesReplaced;
    }

    private void UpdateSortHeaders()
    {
        if (model is null) { return; }
        string[] names = ["Name", "Size (bytes)", "Type", "Modified", "Attributes", "Offset"];
        for (int i = 0; i < FileGrid.Columns.Count; i++)
        {
            FileGrid.Columns[i].Header = names[i] + ((int)model.BrowserPreferences.SortField == i
                ? model.BrowserPreferences.SortDirection == BrowserSortDirection.Ascending ? " ↑" : " ↓" : "");
        }
    }

    private void ColumnSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        int index = FileGrid.Columns.IndexOf(e.Column);
        if (index >= 0) { model?.SortBy((BrowserSortField)index); }
    }

    private void UpdateToolHeight()
    {
        if (model is null) { return; }
        if (!model.ToolVisible && CenterGrid.RowDefinitions[2].ActualHeight >= 100)
        {
            model.Layout = model.Layout with { ToolHeight = CenterGrid.RowDefinitions[2].ActualHeight };
        }
        CenterGrid.RowDefinitions[2].Height = new GridLength(model.ToolVisible
            ? Math.Clamp(CenterGrid.RowDefinitions[2].ActualHeight > 0 ? CenterGrid.RowDefinitions[2].ActualHeight : model.Layout.ToolHeight,
                100, Math.Max(100, CenterGrid.Bounds.Height - 110))
            : 0);
    }

    public void SaveLayout()
    {
        if (model is null) { return; }
        UnsubscribeModel();
        model.Layout = new LayoutSettings(
            Math.Clamp(PaneGrid.ColumnDefinitions[0].ActualWidth, 160, 600),
            Math.Clamp(PaneGrid.ColumnDefinitions[4].ActualWidth, 180, 600),
            Math.Clamp(CenterGrid.RowDefinitions[2].ActualHeight > 0 ? CenterGrid.RowDefinitions[2].ActualHeight : model.Layout.ToolHeight, 100, 600),
            model.ToolVisible);
        if (this.FindAncestorOfType<MainWindow>()?.DataContext is MainWindowViewModel main)
        {
            main.SaveLayout(model.Layout);
        }
    }

    private void TreeSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (!restoringSelection && model is not null && StructureTree.SelectedItem is StructureNode node && node != model.SelectedNode)
        {
            restoringSelection = true;
            StructureTree.SelectedItem = model.SelectedNode;
            restoringSelection = false;
            model.SelectNodeCommand.Execute(node);
        }
    }
    private void FilesSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (!restoringSelection) { model?.SelectEntries(FileGrid.SelectedItems.OfType<FileEntry>()); }
    }
    private void ToolSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (args.Source == sender && sender is TabControl { SelectedIndex: 1 } && model is not null && !model.HexCommand.IsRunning)
        {
            model.HexCommand.Execute(null);
        }
    }
    private void FileActivated(object? sender, TappedEventArgs args) => ActivateFile();
    private void OpenSelected(object? sender, RoutedEventArgs args) => ActivateFile();
    private void ActivateFile() => model?.ActivateCommand.Execute(FileGrid.SelectedItem as FileEntry);
    private void FilesDragOver(object? sender, DragEventArgs args)
    {
        args.DragEffects &= model?.CanAdd == true && args.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private async void FilesDrop(object? sender, DragEventArgs args)
    {
        FilesDragOver(sender, args);
        if (args.DragEffects == DragDropEffects.Copy && model is { } workspace)
        {
            await workspace.ImportFilesAsync(() => Task.FromResult(
                args.DataTransfer.TryGetFiles()?.Select(item => item is IStorageFile ? item.TryGetLocalPath() : null).ToArray() ?? []));
        }
    }

    private void FilesKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { ActivateFile(); args.Handled = true; }
        if (args.Key == Key.Back) { model?.UpCommand.Execute(null); args.Handled = true; }
        if (args.Key == Key.Delete && model?.DeleteCommand.CanExecute(null) == true) { model.DeleteCommand.Execute(null); args.Handled = true; }
        if (args.Key == Key.F2 && model?.RenameCommand.CanExecute(null) == true) { model.RenameCommand.Execute(null); args.Handled = true; }
    }
    private void BreadcrumbClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: Breadcrumb crumb }) { model?.NavigateCommand.Execute(crumb.Path); }
    }
    private void SearchActivated(object? sender, TappedEventArgs args) => ActivateSearch();
    private void ActivateSearch() => model?.NavigateHitCommand.Execute(SearchList.SelectedItem as SearchHit);
    private void SearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { ActivateSearch(); args.Handled = true; }
    }
}
