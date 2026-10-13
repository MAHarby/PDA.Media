using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDA.Media.Data.Entities;
using PDA.Media.Scanner.Services;
using PDA.Media.Utils.Services;

namespace PDA.Media.Utils.ViewModels;

/// <summary>
/// The Rename panel over the Search tab: a new name (Tidy name suggests one), the options for the kind of item, and a preview of every folder and file
/// renamed and every database change (worked out by MediaRenameService, again whenever the name or an option changes). Apply renames on disk and in the
/// database together, putting the disk back if anything fails.
/// </summary>
public partial class MediaRenameViewModel : ViewModelBase
{
    private readonly MediaRenameService _renameService;
    private readonly Action<MediaRenameResult> _onRenamed;
    private readonly Action _onClose;
    private CancellationTokenSource? _previewDelay;

    public MediaRenameViewModel(MediaRenameService renameService, IEntity item, Action<MediaRenameResult> onRenamed, Action onClose)
    {
        _renameService = renameService;
        Item = item;
        _onRenamed = onRenamed;
        _onClose = onClose;
        CurrentName = MediaRenameService.CurrentName(item);
        NewName = CurrentName;
        RenameFolder = true;
        RenameFiles = true;
    }

    public IEntity Item { get; }
    public string CurrentName { get; }

    public string Title => "Rename " + Item switch
    {
        Movie => "movie",
        TVShow => "TV show",
        TVShowEpisode => "episode",
        Track => "track",
        Album => "album",
        _ => "artist",
    };

    /// <summary>What the name is: a show's name with its year, or a track's / episode's title (its number stays).</summary>
    public string NameHint => Item switch
    {
        TVShow => "Name (Year), as the show folder is named",
        TVShowEpisode => "The episode title (the show and sNNeNN part of the file name stay)",
        Track => "The track title (its number stays)",
        Movie => "Title (Year), as the movie folder is named",
        _ => "The folder name",
    };

    public bool HasFolderOption => Item is Movie or TVShow;
    public bool HasFilesOption => Item is Movie or TVShow;
    public string FolderOptionText => Item is TVShow ? "Rename the show folder" : "Rename the movie folder";
    public string FilesOptionText => Item is TVShow ? "Rename the episode files' show name" : "Rename the files inside (video, samples, subtitles, artwork)";

    [ObservableProperty] public partial string NewName { get; set; }
    [ObservableProperty] public partial bool RenameFolder { get; set; }
    [ObservableProperty] public partial bool RenameFiles { get; set; }

    partial void OnNewNameChanged(string value) => SchedulePreview();
    partial void OnRenameFolderChanged(bool value) => SchedulePreview();
    partial void OnRenameFilesChanged(bool value) => SchedulePreview();

    // Preview.
    // ==================================================================================================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Steps), nameof(DatabaseChanges), nameof(Problems), nameof(Warnings), nameof(HasProblems), nameof(HasWarnings), nameof(HasPlan))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial MediaRenamePlan? Plan { get; set; }

    public bool HasPlan => Plan != null;
    public IReadOnlyList<MediaRenameStep> Steps => Plan?.Steps ?? [];
    public IReadOnlyList<string> DatabaseChanges => Plan?.DatabaseChanges ?? [];
    public IReadOnlyList<string> Problems => Plan?.Problems ?? [];
    public IReadOnlyList<string> Warnings => Plan?.Warnings ?? [];
    public bool HasProblems => Problems.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>The result of Apply when it failed (and anything that couldn't be put back).</summary>
    [ObservableProperty] public partial string ResultText { get; set; } = "";

    /// <summary>Previews again shortly after the name or an option changes (typing doesn't read the folders on every key).</summary>
    private async void SchedulePreview()
    {
        Plan = null;
        _previewDelay?.Cancel();
        _previewDelay = new CancellationTokenSource();
        CancellationToken token = _previewDelay.Token;
        try
        {
            await Task.Delay(400, token);
            await PreviewAsync();
        }
        catch (OperationCanceledException)
        {
            // Changed again.
        }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var request = new MediaRenameRequest(Item, NewName, RenameFolder, RenameFiles);
        IsBusy = true;
        try
        {
            MediaRenamePlan plan = await Task.Run(() => _renameService.PlanAsync(request));
            // Only show it if nothing has changed since it was asked for.
            if (request == new MediaRenameRequest(Item, NewName, RenameFolder, RenameFiles)) Plan = plan;
        }
        catch (Exception ex)
        {
            ResultText = $"Couldn't work out the rename: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Puts the tidy name (MediaNameTidy) in the box, to edit or apply.</summary>
    [RelayCommand]
    private void Tidy() => NewName = MediaNameTidy.Suggest(Item);

    // Apply and close.
    // ==================================================================================================

    private bool CanApply() => Plan is { CanApply: true } && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        MediaRenamePlan? plan = Plan;
        if (plan == null) return;
        IsBusy = true;
        ResultText = "";
        try
        {
            MediaRenameResult result = await Task.Run(() => _renameService.ApplyAsync(plan));
            if (result.Succeeded)
            {
                _onRenamed(result);
                return;
            }
            ResultText = result.Message + (result.RollbackFailures.Count > 0 ? "\n" + string.Join("\n", result.RollbackFailures) : "");
            await PreviewAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        _previewDelay?.Cancel();
        _onClose();
    }
}
