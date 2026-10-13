using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDA.Media.Data.Entities;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Models;
using PDA.Media.Utils.Services;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// The batch "Tidy names" panel over the Search tab, for a branch of the results tree: each item under it whose tidy name (MediaNameTidy) differs from
/// its name, with the suggestion (editable) and a tick box. Each row is checked first (problems are shown and unticked), and Apply renames the ticked rows
/// one at a time, deepest first, each with its own check and rollback (MediaRenameService), so one failure doesn't stop the others.
/// </summary>
public partial class TidyNamesViewModel : ViewModelBase
{
    /// <summary>The most items looked at (a whole category could be many thousands).</summary>
    public const int MaxItems = 1000;

    private readonly MediaRenameService _renameService;
    private readonly MediaTreeNode _branch;
    private readonly Action<bool> _onClose;
    private bool _renamedAny;

    public TidyNamesViewModel(MediaRenameService renameService, MediaTreeNode branch, Action<bool> onClose)
    {
        _renameService = renameService;
        _branch = branch;
        _onClose = onClose;
        Title = $"Tidy names in {branch.Title}";
    }

    public string Title { get; }
    public ObservableCollection<TidyRow> Rows { get; } = [];

    /// <summary>Rename movie files and episode files with their folders (as Rename's "rename the files" option).</summary>
    [ObservableProperty] public partial bool RenameFiles { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial double ProgressMaximum { get; set; } = 1;

    public int SelectedCount => Rows.Count(r => r.IsSelected);

    /// <summary>Finds the items to tidy under the branch (opening its branches) and checks each suggested rename.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy || Rows.Count > 0) return;
        IsBusy = true;
        try
        {
            StatusText = "Looking for names to tidy...";
            List<(MediaTreeNode Node, int Depth)> items = await CollectAsync(_branch, 0, []);
            foreach ((MediaTreeNode node, int depth) in items)
            {
                IEntity entity = node.Entity!;
                string current = MediaRenameService.CurrentName(entity), tidy = MediaNameTidy.Suggest(entity);
                if (tidy == current || entity is ISoftDeletable { IsDeleted: true }) continue;
                var row = new TidyRow(node, KindOf(entity), current, tidy, depth);
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TidyRow.IsSelected)) ApplyCommand.NotifyCanExecuteChanged();
                };
                Rows.Add(row);
            }

            ProgressMaximum = Math.Max(1, Rows.Count);
            ProgressValue = 0;
            foreach (TidyRow row in Rows)
            {
                StatusText = $"Checking {row.CurrentName}...";
                MediaRenamePlan plan = await Task.Run(() => _renameService.PlanAsync(Request(row)));
                row.Status = plan.CanApply ? TidyRowStatus.Ready : TidyRowStatus.Problem;
                row.Message = plan.CanApply ? "" : string.Join(" ", plan.Problems);
                row.IsSelected = plan.CanApply;
                ProgressValue++;
            }

            StatusText = Rows.Count == 0 ? "Nothing to tidy: every name is tidy already"
                : $"{Rows.Count} name(s) to tidy" + (items.Count >= MaxItems ? $" (only the first {MaxItems} items were looked at)" : "")
                  + (Rows.Any(r => r.IsProblem) ? $"; {Rows.Count(r => r.IsProblem)} can't be renamed as suggested" : "");
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't look for names to tidy: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The media rows under <paramref name="node"/> (and itself), opening branches as needed, up to <see cref="MaxItems"/>.</summary>
    private static async Task<List<(MediaTreeNode, int)>> CollectAsync(MediaTreeNode node, int depth, List<(MediaTreeNode, int)> found)
    {
        if (found.Count >= MaxItems) return found;
        if (node.Entity != null && node.Kind != MediaNodeKind.Message) found.Add((node, depth));
        await node.LoadChildrenAsync();
        foreach (MediaTreeNode child in node.Children.ToList()) await CollectAsync(child, depth + 1, found);
        return found;
    }

    private MediaRenameRequest Request(TidyRow row) => new(row.Entity, row.NewName, RenameFolder: true, RenameFiles: RenameFiles);

    private static string KindOf(IEntity entity) => entity switch
    {
        Movie => "Movie",
        TVShow => "TV show",
        TVShowEpisode => "Episode",
        Track => "Track",
        Album => "Album",
        _ => "Artist",
    };

    private bool CanApply() => !IsBusy && Rows.Any(r => r.IsSelected);

    /// <summary>Renames the ticked rows one at a time, deepest first; each is checked again just before (the disk may have changed).</summary>
    [RelayCommand(CanExecute = nameof(CanApply), IncludeCancelCommand = true)]
    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        List<TidyRow> rows = Rows.Where(r => r.IsSelected).OrderByDescending(r => r.Depth).ToList();
        foreach (TidyRow row in Rows.Where(r => !r.IsSelected && r.Status == TidyRowStatus.Ready)) row.Status = TidyRowStatus.Skipped;
        IsBusy = true;
        ProgressMaximum = Math.Max(1, rows.Count);
        ProgressValue = 0;
        int done = 0, failed = 0;
        try
        {
            foreach (TidyRow row in rows)
            {
                if (cancellationToken.IsCancellationRequested) break;
                StatusText = $"Renaming {row.CurrentName}...";
                MediaRenamePlan plan = await Task.Run(() => _renameService.PlanAsync(Request(row)));
                MediaRenameResult result = plan.CanApply
                    ? await Task.Run(() => _renameService.ApplyAsync(plan))
                    : new MediaRenameResult(false, string.Join(" ", plan.Problems), []);
                row.Status = result.Succeeded ? TidyRowStatus.Done : TidyRowStatus.Failed;
                row.Message = result.Succeeded ? "Renamed" : result.Message;
                row.IsSelected = false;
                if (result.Succeeded) done++;
                else failed++;
                ProgressValue++;
            }
        }
        finally
        {
            IsBusy = false;
            _renamedAny |= done > 0;
        }
        StatusText = $"{done} renamed" + (failed > 0 ? $", {failed} failed (see the list)" : "") + (cancellationToken.IsCancellationRequested ? "; cancelled" : "");
    }

    [RelayCommand]
    private void Close()
    {
        if (ApplyCommand.IsRunning) ApplyCancelCommand.Execute(null);
        _onClose(_renamedAny);
    }
}
