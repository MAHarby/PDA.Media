using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;

namespace PDA.Media.Utils.Models;

/// <summary>A media category's card in the Analysis tab's summary strip.</summary>
/// <param name="Counts">"1,234 tracks · 98 albums · 45 artists".</param>
/// <param name="LastScan">"Last scan 12/10/2026 21:04", or that it hasn't been scanned.</param>
/// <param name="Warning">Files without a size, soft-deleted rows; null when there is nothing to say.</param>
public sealed record AnalysisSummaryItem(string Name, MediaKind Kind, string Counts, string SizeText, string LastScan, string? Warning)
{
    public bool IsTotal => Kind == MediaKind.Unknown;
    public bool HasWarning => Warning != null;
}

/// <summary>A file, album or show in one of the Analysis tab's lists (largest, a housekeeping check).</summary>
public sealed class AnalysisItemRow(MediaAnalysisItem item, int rank = 0, double sharePercent = 0)
{
    public MediaAnalysisItem Item { get; } = item;
    public int Rank { get; } = rank;
    public string RankText => Rank > 0 ? $"{Rank}." : "";
    public string Name => Item.Name;
    public string? Parent => Item.Parent;
    public string SizeText => Item.Size is long size ? FileSize.Format(size) : "";
    public string? Note => Item.Note;
    public string? Path => Item.Path;
    public bool IsDeleted => Item.IsDeleted;

    /// <summary>Its size as a percentage of the largest in the list (for the bar).</summary>
    public double SharePercent { get; } = sharePercent;

    /// <summary>"Bohemian Rhapsody · Queen · A Night at the Opera" for the second line.</summary>
    public string Detail => string.Join(" · ", new[] { Item.Parent, Item.Note, Item.Items > 1 ? $"{Item.Items:N0} files" : null }.Where(p => !string.IsNullOrEmpty(p)));
}

/// <summary>The files of one extension under the selected node.</summary>
public sealed class AnalysisFileTypeRow(MediaFileType type, double sharePercent)
{
    public string Extension => type.Extension;
    public string ItemsText => type.Items.ToString("N0");
    public string SizeText => FileSize.Format(type.Size);

    /// <summary>Its share of the node's total size.</summary>
    public double SharePercent { get; } = sharePercent;

    public string ShareText => $"{SharePercent:0}%";
}

/// <summary>A housekeeping check for the selected node, opened to show what it found.</summary>
public partial class AnalysisCheckItem(MediaCheck check) : ObservableObject
{
    public string Title => check.Title;
    public string Description => check.Description;
    public int Count => check.Count;
    public bool IsClear => check.Count == 0;
    public string CountText => check.Count == 0 ? "OK" : check.Count.ToString("N0");
    public IReadOnlyList<AnalysisItemRow> Rows { get; } = check.Rows.Select(r => new AnalysisItemRow(r)).ToList();

    /// <summary>"Showing the first 500" when there were more.</summary>
    public string? MoreText => check.Count > check.Rows.Count ? $"Showing the first {check.Rows.Count:N0} of {check.Count:N0}" : null;

    public bool HasMore => MoreText != null;

    [ObservableProperty] public partial bool IsExpanded { get; set; }
}

/// <summary>A difference the live disk check found.</summary>
/// <param name="folder">The folder that was checked: paths are shown from there.</param>
public sealed class DiskIssueRow(MediaDiskIssue issue, string folder)
{
    public MediaDiskIssueKind Kind => issue.Kind;
    public string KindText => KindName(issue.Kind);
    public string Path => issue.Path;

    /// <summary>The path from the checked folder ("Up (2009)/Up (2009).mkv"), or the full path when it isn't under it.</summary>
    public string RelativePath => issue.Path.Length > folder.Length + 1 && issue.Path.StartsWith(folder, System.StringComparison.OrdinalIgnoreCase)
        ? issue.Path[(folder.TrimEnd('\\', '/').Length + 1)..] : issue.Path;

    public string Detail => issue.Detail;
    public bool IsProblem => issue.Kind is MediaDiskIssueKind.MissingFromDisk or MediaDiskIssueKind.Unreadable;

    public static string KindName(MediaDiskIssueKind kind) => kind switch
    {
        MediaDiskIssueKind.NotInDatabase => "Not stored",
        MediaDiskIssueKind.MissingFromDisk => "Missing",
        MediaDiskIssueKind.Changed => "Changed",
        MediaDiskIssueKind.Extra => "Extra",
        MediaDiskIssueKind.EmptyFolder => "Empty folder",
        _ => "Unreadable",
    };
}

/// <summary>Asks the Search tab to look for a row the Analysis tab showed.</summary>
/// <param name="Root">The category's root folder (only that category is searched).</param>
/// <param name="Text">The row's name.</param>
/// <param name="IncludeDeleted">The row is soft-deleted, so the search must include deleted rows.</param>
public sealed record AnalysisSearchRequest(string Root, string Text, bool IncludeDeleted);
