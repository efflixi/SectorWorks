using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SectorWorks.Core;

namespace SectorWorks.UI.ViewModels;

public sealed partial class StructureNode : ViewModelBase
{
    private readonly Func<StructureNode, Task<bool>>? loader;
    private bool loaded;
    private Task<bool>? loading;
    private string title;
    public StructureNode(string title, string kind, string? volumeId = null, string? path = null,
        Func<StructureNode, Task<bool>>? loader = null)
    {
        this.title = title;
        Kind = kind;
        VolumeId = volumeId;
        Path = path;
        this.loader = loader;
        Children.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (StructureNode child in e.NewItems) { child.Parent = this; }
            }
        };
        if (loader is not null)
        {
            Children.Add(new StructureNode("Expand to load directories", "Info"));
        }
    }

    public string Title { get => title; internal set => SetProperty(ref title, value); }
    public string Kind { get; }
    public string? VolumeId { get; }
    public string? Path { get; }
    public string? Description { get; init; }
    public CdTrack? Track { get; init; }
    public bool IsAudioTrack => Track?.IsAudio == true;
    public bool IsDataTrack => Track is { IsAudio: false };
    [ObservableProperty] private bool isPlayingTrack;
    public StructureNode? Parent { get; private set; }
    public Geometry Icon => Icons.Get(Kind);
    public ObservableCollection<StructureNode> Children { get; } = [];
    [ObservableProperty] private bool isExpanded;
    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !loaded && loader is not null)
        {
            LoadCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await EnsureLoadedAsync();
    }

    public async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (loaded || loader is null) { return true; }
        if (loading is null || loading.IsCompleted) { loading = LoadCoreAsync(); }
        return await loading.WaitAsync(cancellationToken);
    }

    private async Task<bool> LoadCoreAsync()
    {
        loaded = await loader!(this);
        return loaded;
    }

    public void SetLoadedChildren(IEnumerable<StructureNode> children)
    {
        Children.Clear();
        foreach (StructureNode child in children) { Children.Add(child); }
        loaded = true;
    }

    public void InvalidateChildren()
    {
        Children.Clear();
        loaded = false;
        Children.Add(new StructureNode("Expand to load directories", "Info"));
    }
}

public static class Icons
{
    public static Geometry Get(string kind)
    {
        string key = "Icon" + kind;
        if (Avalonia.Application.Current?.TryGetResource(key, null, out object? resource) == true && resource is Geometry geometry)
        {
            return geometry;
        }
        return Geometry.Parse("M2,2 L22,2 22,22 2,22 Z");
    }
}
