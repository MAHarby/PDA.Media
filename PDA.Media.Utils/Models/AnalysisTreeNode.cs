using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Data.Services;

namespace PDA.Media.Utils.Models;

/// <summary>What a node in the Analysis tab's tree is (for its icon).</summary>
public enum AnalysisNodeKind
{
    /// <summary>A media category's root folder: the top of the tree.</summary>
    Category,
    Artist,
    Album,
    Track,
    Source,

    /// <summary>A folder holding several movies' files (e.g. "Star Trek").</summary>
    Collection,
    Movie,
    Show,
    Season,
    Episode,

    /// <summary>"Loading..." (or an error) until a branch's children have been read.</summary>
    Message,
}

/// <summary>
/// A node in the Analysis tab's tree, with the totals of the media files under it (<see cref="Stats"/>) and its share of its parent's size. Its children
/// are loaded when it is first opened (<see cref="LoadChildrenAsync"/>); until then it shows a "Loading..." child so it can be opened.
/// </summary>
public partial class AnalysisTreeNode : ObservableObject
{
    private readonly Func<AnalysisTreeNode, Task<List<AnalysisTreeNode>>>? _loader;
    private Func<AnalysisTreeNode, Task<List<AnalysisTreeNode>>>? _loadChildren;
    private Task? _loading;

    public AnalysisTreeNode(AnalysisNodeKind kind, string title, MediaAnalysisScope? scope = null, MediaStats? stats = null,
        Func<AnalysisTreeNode, Task<List<AnalysisTreeNode>>>? loadChildren = null)
    {
        Kind = kind;
        Title = title;
        Scope = scope;
        Stats = stats ?? MediaStats.Empty;
        _loader = _loadChildren = loadChildren;
        if (loadChildren != null) Children.Add(Message("Loading..."));
    }

    public static AnalysisTreeNode Message(string text) => new(AnalysisNodeKind.Message, text);

    /// <summary>A node for a child read by <see cref="MediaAnalysisService.GetChildren"/>.</summary>
    public static AnalysisTreeNode From(MediaAnalysisNode node, Func<AnalysisTreeNode, Task<List<AnalysisTreeNode>>>? loadChildren) =>
        new(KindOf(node.Scope), node.Name, node.Scope, node.Stats, node.IsLeaf ? null : loadChildren)
        {
            Folder = node.Folder,
            FileName = node.FileName,
            IsDeleted = node.IsDeleted,
            SearchTitle = node.Title,
        };

    public static AnalysisNodeKind KindOf(MediaAnalysisScope scope) => scope.Level switch
    {
        MediaAnalysisLevel.Root => AnalysisNodeKind.Category,
        MediaAnalysisLevel.Artist => AnalysisNodeKind.Artist,
        MediaAnalysisLevel.Album => AnalysisNodeKind.Album,
        MediaAnalysisLevel.Source => AnalysisNodeKind.Source,
        MediaAnalysisLevel.Collection => AnalysisNodeKind.Collection,
        MediaAnalysisLevel.Show => AnalysisNodeKind.Show,
        MediaAnalysisLevel.Season => AnalysisNodeKind.Season,
        _ => scope.Kind switch
        {
            MediaAnalysisKind.Music => AnalysisNodeKind.Track,
            MediaAnalysisKind.Movies => AnalysisNodeKind.Movie,
            _ => AnalysisNodeKind.Episode,
        },
    };

    public AnalysisNodeKind Kind { get; }
    public string Title { get; }

    /// <summary>The media files the node covers; null for a message.</summary>
    public MediaAnalysisScope? Scope { get; }

    /// <summary>Its folder on disk (for a file: the folder holding it).</summary>
    public string? Folder { get; init; }

    public string? FileName { get; init; }
    public bool IsDeleted { get; init; }

    /// <summary>The row's own name (no track or episode number), to look for on the Search tab; null for a category or source folder.</summary>
    public string? SearchTitle { get; init; }

    /// <summary>Its place among its siblings as they were read (by name or number), to go back to after sorting by size.</summary>
    public int ReadIndex { get; set; }

    public AnalysisTreeNode? Parent { get; private set; }
    public ObservableCollection<AnalysisTreeNode> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemsText), nameof(FoldersText), nameof(SizeText), nameof(ToolTip))]
    public partial MediaStats Stats { get; set; }

    [ObservableProperty] public partial bool IsExpanded { get; set; }

    /// <summary>Its size as a percentage of its parent's (0 to 100).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareText))]
    public partial double SharePercent { get; set; }

    /// <summary>The file's full path, or the folder for a branch.</summary>
    public string? Path => Folder == null ? null : FileName == null ? Folder : System.IO.Path.Combine(Folder, FileName);

    public string ItemsText => IsMessage ? "" : Stats.Items.ToString("N0");
    public string FoldersText => IsMessage ? "" : Stats.Folders.ToString("N0");

    /// <summary>"12.5 GB", with a "+" when some files have no size yet.</summary>
    public string SizeText => IsMessage ? "" : FileSize.Format(Stats.Size) + (Stats.WithoutSize > 0 ? "+" : "");

    public string ShareText => IsMessage || Parent == null ? "" : $"{SharePercent:0}%";

    public string? ToolTip => IsMessage ? null
        : Stats.WithoutSize > 0 ? $"{Path}\n{Stats.WithoutSize:N0} files have no size yet: scan the category again" : Path;

    // For the icon (one style class per kind).
    public bool IsCategory => Kind == AnalysisNodeKind.Category;
    public bool IsArtist => Kind == AnalysisNodeKind.Artist;
    public bool IsAlbum => Kind == AnalysisNodeKind.Album;
    public bool IsTrack => Kind == AnalysisNodeKind.Track;
    public bool IsSource => Kind == AnalysisNodeKind.Source;
    public bool IsCollection => Kind == AnalysisNodeKind.Collection;
    public bool IsMovie => Kind == AnalysisNodeKind.Movie;
    public bool IsShow => Kind == AnalysisNodeKind.Show;
    public bool IsSeason => Kind == AnalysisNodeKind.Season;
    public bool IsEpisode => Kind == AnalysisNodeKind.Episode;
    public bool IsMessage => Kind == AnalysisNodeKind.Message;

    /// <summary>True once the children are there (or there was nothing to load).</summary>
    public bool AreChildrenLoaded => _loadChildren == null;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = LoadChildrenAsync();
    }

    /// <summary>Reads the children if they haven't been yet (once, however often it is called).</summary>
    public Task LoadChildrenAsync() => _loadChildren == null ? Task.CompletedTask : _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        List<AnalysisTreeNode> children;
        try
        {
            children = await _loadChildren!(this);
        }
        catch (Exception ex)
        {
            children = [Message($"Couldn't load: {ex.GetBaseException().Message}")];
        }

        _loadChildren = null;
        Children.Clear();
        foreach (AnalysisTreeNode child in children)
        {
            child.Parent = this;
            child.SharePercent = Stats.Size > 0 ? 100.0 * child.Stats.Size / Stats.Size : 0;
            Children.Add(child);
        }
    }

    /// <summary>Puts the loaded children (and theirs) in order: largest first, or as read (<see cref="ReadIndex"/>).</summary>
    public void SortChildren(bool bySize)
    {
        if (!AreChildrenLoaded) return;
        List<AnalysisTreeNode> sorted = Sorted(Children, bySize);
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = Children.IndexOf(sorted[i]);
            if (current != i) Children.Move(current, i);
        }
        foreach (AnalysisTreeNode child in Children) child.SortChildren(bySize);
    }

    public static List<AnalysisTreeNode> Sorted(IEnumerable<AnalysisTreeNode> nodes, bool bySize) => bySize
        ? nodes.OrderByDescending(c => c.Stats.Size).ThenBy(c => c.ReadIndex).ToList()
        : nodes.OrderBy(c => c.ReadIndex).ToList();

    /// <summary>The node and its parents, from the top of the tree.</summary>
    public IEnumerable<AnalysisTreeNode> PathFromTop()
    {
        var path = new List<AnalysisTreeNode>();
        for (AnalysisTreeNode? node = this; node != null; node = node.Parent) path.Insert(0, node);
        return path;
    }
}
