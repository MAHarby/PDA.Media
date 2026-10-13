using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Data.Entities;

namespace PDA.Media.Utils.Models;

/// <summary>Where a row of a batch tidy has got to.</summary>
public enum TidyRowStatus
{
    /// <summary>Checked and can be renamed.</summary>
    Ready,

    /// <summary>The check found a problem (it is unticked).</summary>
    Problem,

    Done,
    Failed,

    /// <summary>Unticked when the batch was applied.</summary>
    Skipped,
}

/// <summary>An item in a batch tidy: its current name, the suggested (editable) new name, whether to rename it, and how it went.</summary>
public partial class TidyRow : ObservableObject
{
    public TidyRow(MediaTreeNode node, string kind, string currentName, string newName, int depth)
    {
        Node = node;
        Kind = kind;
        CurrentName = currentName;
        NewName = newName;
        Depth = depth;
    }

    public MediaTreeNode Node { get; }
    public IEntity Entity => Node.Entity!;

    /// <summary>"Movie", "TV show", "Episode", ...</summary>
    public string Kind { get; }

    public string CurrentName { get; }

    /// <summary>How deep the item is in the tree: deeper items are renamed first, so a folder renamed later doesn't move them.</summary>
    public int Depth { get; }

    [ObservableProperty] public partial string NewName { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProblem), nameof(IsDone), nameof(IsFailed))]
    public partial TidyRowStatus Status { get; set; }

    /// <summary>The problem, failure or result.</summary>
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool IsProblem => Status == TidyRowStatus.Problem;
    public bool IsDone => Status == TidyRowStatus.Done;
    public bool IsFailed => Status == TidyRowStatus.Failed;
}
