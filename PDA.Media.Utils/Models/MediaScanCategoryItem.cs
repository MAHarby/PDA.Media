using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PDA.Media.Scanner.Services;

namespace PDA.Media.Utils.Models;

/// <summary>A media category in the media scanner's list: ticked to be scanned, with its last scan.</summary>
public partial class MediaScanCategoryItem : ObservableObject
{
    public MediaScanCategoryItem(MediaScanCategory category, bool isSelected)
    {
        Category = category;
        IsSelected = isSelected && category.CanScan;
        LastScan = category.LastScan;
    }

    public MediaScanCategory Category { get; }
    public int Id => Category.Id;
    public string Name => Category.Name;
    public bool CanScan => Category.CanScan;
    public bool IsActive => Category.Category.IsActive;

    /// <summary>"Movie · \\nas\Movies", with "(inactive)" for an inactive category.</summary>
    public string DetailText => $"{Category.Category.MediaClass} · {Category.Category.RootFolder ?? "no root folder"}" + (IsActive ? "" : " (inactive)");

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastScanText), nameof(IsLastScanProblem))]
    public partial MediaCategoryScanResult? LastScan { get; set; }

    /// <summary>Why it can't be scanned, or how its last scan went.</summary>
    public string LastScanText => !CanScan ? $"Can't be scanned: {Category.CannotScanReason}"
        : LastScan == null ? "Never scanned"
        : $"Last scan {LastScan.FinishedOn.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture)} · {LastScan.SummaryText}";

    /// <summary>True when the last scan was skipped, failed or held back its deletions (shown as a warning).</summary>
    public bool IsLastScanProblem => LastScan is { Status: not MediaCategoryScanStatus.Scanned };
}
