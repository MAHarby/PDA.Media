using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Data.Entities;

namespace PDA.Media.Utils.Models;

/// <summary>What a node in the media library's search tree shows.</summary>
public enum MediaNodeKind
{
    /// <summary>"Music", "Movies" or "TV shows": the top of the tree.</summary>
    Group,
    Artist,
    Album,
    Track,

    /// <summary>A movie source folder (e.g. "Compressed Blu-Rays").</summary>
    Source,
    Movie,
    Show,
    Season,
    Episode,

    /// <summary>"Loading..." (or an error) until a branch's children have been read.</summary>
    Message,
}

/// <summary>
/// A node in the media library's search tree: a group, a media row (artist, album, ...) or a source / season heading.
/// Its children can be loaded when it is first opened (<see cref="LoadChildrenAsync"/>); until then it shows a "Loading..." child so it can be opened.
/// </summary>
public partial class MediaTreeNode : ObservableObject
{
    private readonly Func<Task<List<MediaTreeNode>>>? _loader;
    private Func<Task<List<MediaTreeNode>>>? _loadChildren;
    private Task? _loading;

    public MediaTreeNode(MediaNodeKind kind, string title, IEntity? entity = null, Func<Task<List<MediaTreeNode>>>? loadChildren = null)
    {
        Kind = kind;
        Title = title;
        Entity = entity;
        _loader = _loadChildren = loadChildren;
        if (loadChildren != null) Children.Add(new MediaTreeNode(MediaNodeKind.Message, "Loading..."));
    }

    public MediaNodeKind Kind { get; }
    public MediaTreeNode? Parent { get; private set; }
    public ObservableCollection<MediaTreeNode> Children { get; } = [];

    [ObservableProperty] public partial string Title { get; set; }

    /// <summary>A short note after the title ("12 tracks", "s01e02").</summary>
    [ObservableProperty] public partial string? Subtitle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavourite), nameof(IsDeleted))]
    public partial IEntity? Entity { get; set; }

    /// <summary>The row itself matched the search (shown in bold).</summary>
    [ObservableProperty] public partial bool IsMatch { get; set; }

    [ObservableProperty] public partial bool IsExpanded { get; set; }

    /// <summary>A match is below this node (an album holding a matching track, a season holding a matching episode): opened after a search.</summary>
    public bool IsOnMatchPath { get; init; }

    public bool IsFavourite => MediaEntityInfo.IsFavourite(Entity);
    public bool IsDeleted => Entity is ISoftDeletable { IsDeleted: true };

    // For the icon (one style class per kind).
    public bool IsGroup => Kind == MediaNodeKind.Group;
    public bool IsArtist => Kind == MediaNodeKind.Artist;
    public bool IsAlbum => Kind == MediaNodeKind.Album;
    public bool IsTrack => Kind == MediaNodeKind.Track;
    public bool IsSource => Kind == MediaNodeKind.Source;
    public bool IsMovie => Kind == MediaNodeKind.Movie;
    public bool IsShow => Kind == MediaNodeKind.Show;
    public bool IsSeason => Kind == MediaNodeKind.Season;
    public bool IsEpisode => Kind == MediaNodeKind.Episode;
    public bool IsMessage => Kind == MediaNodeKind.Message;

    /// <summary>True once the children are there (or there was nothing to load).</summary>
    public bool AreChildrenLoaded => _loadChildren == null;

    public void AddChild(MediaTreeNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = LoadChildrenAsync();
    }

    /// <summary>Reads the children if they haven't been yet (once, however often it is called).</summary>
    public Task LoadChildrenAsync() => _loadChildren == null ? Task.CompletedTask : _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        List<MediaTreeNode> children;
        try
        {
            children = await _loadChildren!();
        }
        catch (Exception ex)
        {
            children = [new MediaTreeNode(MediaNodeKind.Message, $"Couldn't load: {ex.GetBaseException().Message}")];
        }

        _loadChildren = null;
        Children.Clear();
        foreach (MediaTreeNode child in children) AddChild(child);
    }

    /// <summary>Forgets the children read so far (e.g. after a rename moved their folders): they are read again when the node is next opened.</summary>
    public void ResetChildren()
    {
        if (_loader == null) return;
        _loadChildren = _loader;
        _loading = null;
        Children.Clear();
        Children.Add(new MediaTreeNode(MediaNodeKind.Message, "Loading..."));
        if (IsExpanded) _ = LoadChildrenAsync();
    }

    /// <summary>The node and its parents, from the top of the tree.</summary>
    public IEnumerable<MediaTreeNode> Path()
    {
        var path = new List<MediaTreeNode>();
        for (MediaTreeNode? node = this; node != null; node = node.Parent) path.Insert(0, node);
        return path;
    }
}
