using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Scanner.Services;

/// <summary>What <see cref="MediaRenameService.PlanAsync"/> is asked to rename.</summary>
/// <param name="Item">The artist, album, track, movie, TV show or episode (only its type and id are used: the row is read again).</param>
/// <param name="NewName">
/// The new name as a folder or file would show it: <c>Heat (1995)</c>, <c>Firefly (2002)</c> (a TV show's year goes in brackets), or a track's or episode's
/// title (its number stays).
/// </param>
/// <param name="RenameFolder">Rename the item's folder (movie in its own folder, TV show; always for an artist or album).</param>
/// <param name="RenameFiles">Rename the files inside to match (movie, TV show episodes); a track's or episode's own file is always renamed.</param>
public sealed record MediaRenameRequest(IEntity Item, string NewName, bool RenameFolder = true, bool RenameFiles = true);

public enum MediaRenameStepKind
{
    Folder,
    File,
}

/// <summary>A folder or file renamed on disk (full paths).</summary>
public sealed record MediaRenameStep(MediaRenameStepKind Kind, string From, string To)
{
    public string FromName => Path.GetFileName(From);
    public string ToName => Path.GetFileName(To);

    public override string ToString() => $"{Kind}: {Path.GetFileName(From)} → {Path.GetFileName(To)}";
}

/// <summary>
/// What a rename would do, worked out before anything is changed: the disk steps (files first, then folders), the database changes, and any problems
/// (which stop it) or warnings. Apply it with <see cref="MediaRenameService.ApplyAsync"/>.
/// </summary>
public sealed class MediaRenamePlan
{
    internal MediaRenamePlan(MediaRenameRequest request, IEntity? item, string oldName)
    {
        Request = request;
        Item = item;
        OldName = oldName;
    }

    public MediaRenameRequest Request { get; }

    /// <summary>The row as stored when the plan was made; null if it isn't there.</summary>
    public IEntity? Item { get; }

    public string OldName { get; }
    public string NewName => Request.NewName.Trim();

    /// <summary>The Name the row will have (it comes from the folder or file, so it only changes when they do).</summary>
    public string NewDatabaseName { get; internal set; } = "";

    public int? NewReleaseYear { get; internal set; }

    public List<MediaRenameStep> Steps { get; } = [];

    /// <summary>The database changes, as text for the preview.</summary>
    public List<string> DatabaseChanges { get; } = [];

    /// <summary>Why it can't be applied (a name Windows doesn't allow, a folder that already exists, another row with that name, ...).</summary>
    public List<string> Problems { get; } = [];

    /// <summary>Things to know that don't stop it (Plex re-scans, long paths, files left as they are).</summary>
    public List<string> Warnings { get; } = [];

    public bool CanApply => Problems.Count == 0 && Steps.Count > 0;

    internal MediaRenameChanges? Changes { get; set; }
}

/// <summary>How <see cref="MediaRenameService.ApplyAsync"/> went.</summary>
/// <param name="RollbackFailures">Disk renames that couldn't be put back after a failure (empty when everything was put back, or nothing failed).</param>
public sealed record MediaRenameResult(bool Succeeded, string Message, IReadOnlyList<string> RollbackFailures);
