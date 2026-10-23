using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Services;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// The media library's Analysis tab, from the sizes and dates the scan stored: a summary card per category, a tree of the categories
/// (Artist → Album → Track, source folder → Movie, Show → Season → Episode) with the files, folders, size and share of each node (branches load when
/// opened), and for the selected node its totals, the 20 largest files (or albums / shows), its file types and housekeeping checks. A node's folder can
/// be read from disk now and compared with the database (<see cref="MediaDiskCheckService"/>), and its files exported to CSV.
/// </summary>
public partial class MediaAnalysisViewModel : ViewModelBase
{
    public const int LargestCount = 20;

    private readonly MediaScanner _scanner;
    private readonly MediaAnalysisService _analysisService;
    private readonly MediaDiskCheckService? _diskCheckService;
    private readonly ILogger<MediaAnalysisViewModel> _logger;
    private CancellationTokenSource? _detailsLoad;
    private CancellationTokenSource? _diskCheck;

    public MediaAnalysisViewModel(MediaScanner scanner, MediaAnalysisService analysisService, MediaDiskCheckService? diskCheckService = null,
        ILogger<MediaAnalysisViewModel>? logger = null)
    {
        _scanner = scanner;
        _analysisService = analysisService;
        _diskCheckService = diskCheckService;
        _logger = logger ?? NullLogger<MediaAnalysisViewModel>.Instance;
    }

    /// <summary>Raised to look for a row on the Search tab (Show in Search).</summary>
    public event Action<AnalysisSearchRequest>? ShowInSearchRequested;

    // Summary and tree.
    // ==================================================================================================

    /// <summary>A card for the whole library, then one per category.</summary>
    public ObservableCollection<AnalysisSummaryItem> Summaries { get; } = [];

    /// <summary>The top of the tree: one node per category that can be scanned.</summary>
    public ObservableCollection<AnalysisTreeNode> Roots { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";

    /// <summary>Count soft-deleted rows too (the tree and details are read again).</summary>
    [ObservableProperty] public partial bool IncludeDeleted { get; set; }

    /// <summary>List each branch's children largest first, rather than by name.</summary>
    [ObservableProperty] public partial bool SortBySize { get; set; }

    /// <summary>A scan has finished since the figures were read: they may be out of date.</summary>
    [ObservableProperty] public partial bool IsStale { get; set; }

    partial void OnIncludeDeletedChanged(bool value) => _ = LoadCommand.ExecuteAsync(null);

    partial void OnSortBySizeChanged(bool value)
    {
        foreach (AnalysisTreeNode root in Roots) root.SortChildren(value);
    }

    /// <summary>Called when a scan has finished on the Scanner tab.</summary>
    public void MarkStale()
    {
        if (Roots.Count == 0) return;
        IsStale = true;
        StatusText = "A scan has finished: Refresh to see the new figures";
    }

    /// <summary>Reads the categories and their totals, and starts the tree again (also the Refresh button).</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Reading the library...";
        try
        {
            List<MediaScanCategory> categories = (await Task.Run(() => _scanner.GetCategoriesAsync())).Where(c => c.CanScan).ToList();
            var summaries = new List<(MediaScanCategory Category, MediaCategorySummary Summary)>();
            foreach (MediaScanCategory category in categories)
            {
                var scope = new MediaAnalysisScope(KindOf(category.Kind), category.Category.RootFolder!.Trim().TrimEnd('\\', '/')) { IncludeDeleted = IncludeDeleted };
                summaries.Add((category, await Task.Run(() => _analysisService.GetSummaryAsync(scope))));
            }

            SelectedNode = null;
            Roots.Clear();
            Summaries.Clear();
            foreach ((MediaScanCategory category, MediaCategorySummary summary) in summaries)
            {
                Roots.Add(new AnalysisTreeNode(AnalysisNodeKind.Category, category.Name, summary.Scope, summary.Stats, LoadChildrenAsync)
                {
                    Folder = summary.Scope.Root,
                });
            }

            long total = summaries.Sum(s => s.Summary.Stats.Size);
            int files = summaries.Sum(s => s.Summary.Stats.Items);
            int withoutSize = summaries.Sum(s => s.Summary.Stats.WithoutSize);
            Summaries.Add(new AnalysisSummaryItem("Library", MediaKind.Unknown, $"{files:N0} files in {Count(summaries.Count, "category", "categories")}",
                FileSize.Format(total), "", withoutSize > 0 ? $"{withoutSize:N0} files have no size yet: scan to fill them in" : null));
            foreach ((MediaScanCategory category, MediaCategorySummary summary) in summaries) Summaries.Add(SummaryOf(category, summary));

            IsStale = false;
            StatusText = summaries.Count == 0 ? "There are no media categories that can be scanned"
                : withoutSize > 0 ? $"{withoutSize:N0} files have no size yet: scan the categories again (after upgrade 003) to fill them in"
                : "Open a branch to see its figures; select a node for its details";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't read the media library's figures");
            StatusText = $"Couldn't read the library: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<List<AnalysisTreeNode>> LoadChildrenAsync(AnalysisTreeNode parent)
    {
        List<MediaAnalysisNode> children = await Task.Run(() => _analysisService.GetChildrenAsync(parent.Scope!));
        List<AnalysisTreeNode> nodes = children.Select((child, i) =>
        {
            AnalysisTreeNode node = AnalysisTreeNode.From(child, LoadChildrenAsync);
            node.ReadIndex = i;
            return node;
        }).ToList();
        return AnalysisTreeNode.Sorted(nodes, SortBySize);
    }

    private static AnalysisSummaryItem SummaryOf(MediaScanCategory category, MediaCategorySummary summary)
    {
        MediaStats stats = summary.Stats;
        string counts = category.Kind switch
        {
            MediaKind.Music => $"{Count(stats.Items, "track")} · {Count(summary.SubGroups, "album")} · {Count(summary.Groups, "artist")}",
            MediaKind.Movie => $"{Count(stats.Items, "movie")} · {Count(summary.Groups, "source folder")}",
            _ => $"{Count(stats.Items, "episode")} · {Count(summary.SubGroups, "season")} · {Count(summary.Groups, "show")}",
        };
        string lastScan = category.LastScan == null ? "Not scanned yet" : $"Last scan {category.LastScan.FinishedOn:g}";
        var warnings = new List<string>();
        if (stats.WithoutSize > 0) warnings.Add($"{stats.WithoutSize:N0} without a size");
        if (summary.Deleted > 0) warnings.Add($"{summary.Deleted:N0} deleted rows kept");
        return new AnalysisSummaryItem(category.Name, category.Kind, counts, FileSize.Format(stats.Size), lastScan, warnings.Count > 0 ? string.Join(" · ", warnings) : null);
    }

    private static MediaAnalysisKind KindOf(MediaKind kind) => kind switch
    {
        MediaKind.Music => MediaAnalysisKind.Music,
        MediaKind.Movie => MediaAnalysisKind.Movies,
        _ => MediaAnalysisKind.TV,
    };

    private static string Count(int count, string singular, string? plural = null) => $"{count:N0} {(count == 1 ? singular : plural ?? singular + "s")}";

    // Details of the selected node.
    // ==================================================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanRankGroups), nameof(RankGroupsText))]
    [NotifyCanExecuteChangedFor(nameof(CheckDiskCommand), nameof(OpenFolderCommand), nameof(ShowNodeInSearchCommand))]
    public partial AnalysisTreeNode? SelectedNode { get; set; }

    public bool HasSelection => SelectedNode is { IsMessage: false };

    [ObservableProperty] public partial string DetailsTitle { get; set; } = "";
    [ObservableProperty] public partial string DetailsSubtitle { get; set; } = "";
    public ObservableCollection<DetailRow> DetailRows { get; } = [];

    [ObservableProperty] public partial bool IsLoadingDetails { get; set; }

    /// <summary>Show the largest files (or albums / shows) under the selected node.</summary>
    [ObservableProperty] public partial bool ShowLargest { get; set; } = true;

    /// <summary>Rank whole albums (music) or shows (TV) instead of files.</summary>
    [ObservableProperty] public partial bool RankGroups { get; set; }

    /// <summary>Albums or shows can be ranked: music or TV above a single album or season.</summary>
    public bool CanRankGroups => SelectedNode?.Scope is { } scope && scope.Kind != MediaAnalysisKind.Movies
        && scope.Level is MediaAnalysisLevel.Root or MediaAnalysisLevel.Artist or MediaAnalysisLevel.Show;

    public string RankGroupsText => SelectedNode?.Scope?.Kind == MediaAnalysisKind.TV ? "Shows, not episodes" : "Albums, not tracks";

    public ObservableCollection<AnalysisItemRow> Largest { get; } = [];
    public ObservableCollection<AnalysisFileTypeRow> FileTypes { get; } = [];
    public ObservableCollection<AnalysisCheckItem> Checks { get; } = [];

    [ObservableProperty] public partial string ChecksText { get; set; } = "";

    partial void OnSelectedNodeChanged(AnalysisTreeNode? value)
    {
        _diskCheck?.Cancel();
        ClearDiskCheck();
        _ = LoadDetailsAsync(value);
    }

    partial void OnShowLargestChanged(bool value) => _ = LoadLargestAsync(SelectedNode, _detailsLoad?.Token ?? CancellationToken.None);

    partial void OnRankGroupsChanged(bool value) => _ = LoadLargestAsync(SelectedNode, _detailsLoad?.Token ?? CancellationToken.None);

    /// <summary>Reads the selected node's details (the totals are already in the node). Running again for another node drops the earlier load.</summary>
    public async Task LoadDetailsAsync(AnalysisTreeNode? node)
    {
        _detailsLoad?.Cancel();
        _detailsLoad = new CancellationTokenSource();
        CancellationToken token = _detailsLoad.Token;
        DetailRows.Clear();
        Largest.Clear();
        FileTypes.Clear();
        Checks.Clear();
        ChecksText = "";
        if (node?.Scope is not { } scope) return;

        DetailsTitle = node.Title;
        DetailsSubtitle = $"{KindText(node.Kind)} · {node.Path}";
        foreach (DetailRow row in TotalsOf(node)) DetailRows.Add(row);

        IsLoadingDetails = true;
        try
        {
            Task largest = LoadLargestAsync(node, token);
            List<MediaFileType> types = await Task.Run(() => _analysisService.GetFileTypesAsync(scope, token), token);
            if (token.IsCancellationRequested) return;
            long size = node.Stats.Size;
            foreach (MediaFileType type in types) FileTypes.Add(new AnalysisFileTypeRow(type, size > 0 ? 100.0 * type.Size / size : 0));

            List<MediaCheck> checks = await Task.Run(() => _analysisService.GetChecksAsync(scope, token), token);
            if (token.IsCancellationRequested) return;
            foreach (MediaCheck check in checks) Checks.Add(new AnalysisCheckItem(check));
            int problems = checks.Count(c => c.Count > 0);
            ChecksText = problems == 0 ? "Nothing to tidy up" : $"{Count(problems, "check")} found something";
            await largest;
        }
        catch (OperationCanceledException)
        {
            // Another node was selected.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't read the details of {Node}", node.Title);
            StatusText = $"Couldn't read the details: {ex.GetBaseException().Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested) IsLoadingDetails = false;
        }
    }

    private async Task LoadLargestAsync(AnalysisTreeNode? node, CancellationToken token)
    {
        Largest.Clear();
        if (!ShowLargest || node?.Scope is not { } scope || node.Kind is AnalysisNodeKind.Track or AnalysisNodeKind.Movie or AnalysisNodeKind.Episode) return;
        MediaLargestBy by = RankGroups && CanRankGroups ? MediaLargestBy.Groups : MediaLargestBy.Files;
        try
        {
            List<MediaAnalysisItem> items = await Task.Run(() => _analysisService.GetLargestAsync(scope, LargestCount, by, token), token);
            if (token.IsCancellationRequested || SelectedNode != node) return;
            Largest.Clear();
            long top = items.FirstOrDefault()?.Size ?? 0;
            for (int i = 0; i < items.Count; i++) Largest.Add(new AnalysisItemRow(items[i], i + 1, top > 0 ? 100.0 * (items[i].Size ?? 0) / top : 0));
        }
        catch (OperationCanceledException)
        {
            // Another node was selected.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't read the largest items of {Node}", node.Title);
            StatusText = $"Couldn't read the largest items: {ex.GetBaseException().Message}";
        }
    }

    private static IEnumerable<DetailRow> TotalsOf(AnalysisTreeNode node)
    {
        MediaStats stats = node.Stats;
        yield return new DetailRow("Files", stats.Items.ToString("N0"));
        yield return new DetailRow("Folders", stats.Folders.ToString("N0"));
        yield return new DetailRow("Total size", $"{FileSize.Format(stats.Size)} ({stats.Size:N0} bytes)");
        if (stats.Items > 1) yield return new DetailRow("Average file", FileSize.Format(stats.AverageSize));
        if (node.Parent != null) yield return new DetailRow("Share", $"{node.SharePercent:0.#}% of {node.Parent.Title}");
        if (stats.WithoutSize > 0) yield return new DetailRow("Without a size", $"{stats.WithoutSize:N0} (scan the category again to fill them in)");
        if (stats.Oldest is DateTime oldest)
        {
            yield return stats.Items == 1 ? new DetailRow("Modified", $"{oldest:g}") : new DetailRow("Files modified", $"{oldest:d} to {stats.Newest:d}");
        }
        if (node.IsDeleted) yield return new DetailRow("Deleted", "Yes: not in the media folders at the last scan");
    }

    private static string KindText(AnalysisNodeKind kind) => kind switch
    {
        AnalysisNodeKind.Category => "Category",
        AnalysisNodeKind.Source => "Source folder",
        _ => kind.ToString(),
    };

    // Actions: show in Search, open folder, export.
    // ==================================================================================================

    /// <summary>Looks for a row from a list on the Search tab.</summary>
    [RelayCommand]
    private void ShowInSearch(AnalysisItemRow? row)
    {
        if (row == null) return;
        ShowInSearchRequested?.Invoke(new AnalysisSearchRequest(row.Item.Scope.Root, row.Item.Title, row.IsDeleted));
    }

    private bool CanShowNodeInSearch() => SearchTitleOf(SelectedNode) != null;

    /// <summary>Looks for the selected node on the Search tab (a season: its show).</summary>
    [RelayCommand(CanExecute = nameof(CanShowNodeInSearch))]
    private void ShowNodeInSearch()
    {
        if (SelectedNode?.Scope is not { } scope || SearchTitleOf(SelectedNode) is not { } title) return;
        ShowInSearchRequested?.Invoke(new AnalysisSearchRequest(scope.Root, title, SelectedNode.IsDeleted));
    }

    private static string? SearchTitleOf(AnalysisTreeNode? node) => node?.Kind == AnalysisNodeKind.Season ? node.Parent?.SearchTitle : node?.SearchTitle;

    private bool CanOpenFolder() => SelectedNode?.Folder != null;

    /// <summary>Opens the node's folder in Explorer (selecting the file on Windows).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        if (SelectedNode?.Folder is not { } folder) return;
        try
        {
            if (OperatingSystem.IsWindows() && SelectedNode.FileName != null) Process.Start("explorer.exe", $"/select,\"{SelectedNode.Path}\"");
            else Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't open the folder: {ex.GetBaseException().Message}";
        }
    }

    /// <summary>Writes every file under the selected node to a CSV file in <paramref name="folder"/>; returns its path (null without a selection).</summary>
    public async Task<string?> ExportAsync(string folder)
    {
        if (SelectedNode?.Scope is not { } scope) return null;
        string title = SelectedNode.Title;
        try
        {
            List<MediaAnalysisItem> files = await Task.Run(() => _analysisService.GetFilesAsync(scope));
            string name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = Path.Combine(folder, $"PDA.Media analysis - {name} - {DateTime.Now:yyyy-MM-dd HHmmss}.csv");
            await File.WriteAllTextAsync(path, ToCsv(files), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _logger.LogInformation("Exported {Count} files under {Node} to {Path}", files.Count, title, path);
            StatusText = $"Exported {Count(files.Count, "file")} to {path}";
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't export the files under {Node}", title);
            StatusText = $"Couldn't export: {ex.GetBaseException().Message}";
            return null;
        }
    }

    /// <summary>One line per file (with a byte-order mark, so Excel reads non-Western names), sizes in bytes and as text.</summary>
    public static string ToCsv(IEnumerable<MediaAnalysisItem> files)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Name,Belongs to,Folder,File,Size (bytes),Size,Modified,Deleted");
        foreach (MediaAnalysisItem file in files)
        {
            string[] fields =
            [
                file.Name, file.Parent ?? "", file.Folder ?? "", file.FileName ?? "", file.Size?.ToString() ?? "", file.Size is long size ? FileSize.Format(size) : "",
                file.Date?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", file.IsDeleted ? "Yes" : "",
            ];
            csv.AppendLine(string.Join(",", fields.Select(Quote)));
        }
        return csv.ToString();

        static string Quote(string field) => field.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }

    // Live disk check.
    // ==================================================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckDiskCommand), nameof(CancelDiskCheckCommand))]
    public partial bool IsCheckingDisk { get; set; }

    [ObservableProperty] public partial string DiskStatusText { get; set; } = "";

    [ObservableProperty] public partial bool HasDiskResult { get; set; }

    public ObservableCollection<DetailRow> DiskRows { get; } = [];
    public ObservableCollection<DiskIssueRow> DiskIssues { get; } = [];

    /// <summary>The disk check can run: there is a check service and the node has a folder.</summary>
    public bool CanCheckDiskAtAll => _diskCheckService != null;

    private bool CanCheckDisk() => _diskCheckService != null && SelectedNode?.Folder != null && SelectedNode.Scope != null && !IsCheckingDisk;

    /// <summary>Reads the selected node's folder from disk now and compares it with the database.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckDisk))]
    private async Task CheckDiskAsync()
    {
        if (_diskCheckService == null || SelectedNode is not { Folder: { } folder, Scope: { } scope } node) return;
        _diskCheck = new CancellationTokenSource();
        CancellationToken token = _diskCheck.Token;
        ClearDiskCheck();
        IsCheckingDisk = true;
        DiskStatusText = $"Reading {folder}...";
        var progress = new UiProgress<int>(folders =>
        {
            if (IsCheckingDisk && SelectedNode == node) DiskStatusText = $"Reading {folder}... {Count(folders, "folder")}";
        });
        try
        {
            MediaDiskCheckResult result = await Task.Run(() => _diskCheckService.CheckAsync(scope.Kind, folder, progress, token), token);
            if (SelectedNode != node) return;
            ShowDiskCheck(result);
        }
        catch (OperationCanceledException)
        {
            if (SelectedNode == node) DiskStatusText = "Stopped";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't check {Folder} on disk", folder);
            DiskStatusText = $"Couldn't read the folder: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsCheckingDisk = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsCheckingDisk))]
    private void CancelDiskCheck() => _diskCheck?.Cancel();

    private void ShowDiskCheck(MediaDiskCheckResult result)
    {
        HasDiskResult = true;
        if (!result.FolderFound)
        {
            DiskStatusText = $"{result.Folder} can't be found or reached";
            return;
        }

        DiskRows.Add(new DetailRow("Folders read", $"{result.Folders:N0} in {result.Elapsed.TotalSeconds:0.0} s"));
        DiskRows.Add(new DetailRow("On disk", $"{Count(result.Files, "file")}, {FileSize.Format(result.Size)}"));
        DiskRows.Add(new DetailRow("Media files", $"{result.MediaFiles:N0}, {FileSize.Format(result.MediaSize)}"));
        DiskRows.Add(new DetailRow("Other files", $"{result.OtherFiles:N0}, {FileSize.Format(result.OtherSize)} (artwork, subtitles, extras...)"));
        DiskRows.Add(new DetailRow("In the database", $"{Count(result.StoredFiles, "file")}, {FileSize.Format(result.StoredSize)}"));
        foreach (MediaDiskIssue issue in result.Issues.OrderBy(i => i.Kind).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)) DiskIssues.Add(new DiskIssueRow(issue, result.Folder));

        DiskStatusText = result.IssueCount == 0 ? "The disk matches the database"
            : string.Join(", ", result.IssueCounts.OrderBy(c => c.Key).Select(c => $"{c.Value:N0} {DiskIssueRow.KindName(c.Key).ToLowerInvariant()}"))
              + (result.IssueCount > result.Issues.Count ? $" (showing the first {result.Issues.Count:N0})" : "");
    }

    private void ClearDiskCheck()
    {
        HasDiskResult = false;
        DiskRows.Clear();
        DiskIssues.Clear();
        DiskStatusText = "";
    }
}
