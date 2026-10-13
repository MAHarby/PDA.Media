using PDA.Media.Data.Entities;

namespace PDA.Media.Scanner.Services;

/// <summary>A media category as offered for scanning: whether it can be scanned, and how its last scan went.</summary>
public sealed record MediaScanCategory(MediaCategory Category, MediaKind Kind, string? CannotScanReason, MediaCategoryScanResult? LastScan)
{
    public int Id => Category.Id;
    public string Name => Category.Name;

    /// <summary>A known media class and a root folder (whether the folder can be reached is found out when it is scanned).</summary>
    public bool CanScan => CannotScanReason == null;
}
