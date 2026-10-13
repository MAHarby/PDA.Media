using PDA.Media.Data.Entities;

namespace PDA.Media.Utils.Models;

/// <summary>The fields media rows share but no interface has (favourite, folder, file, cover art), read from any media entity.</summary>
public static class MediaEntityInfo
{
    public static bool IsFavourite(IEntity? entity) => entity switch
    {
        Artist a => a.IsFavourite,
        Album a => a.IsFavourite,
        Track t => t.IsFavourite,
        Movie m => m.IsFavourite,
        TVShow s => s.IsFavourite,
        TVShowEpisode e => e.IsFavourite,
        _ => false,
    };

    public static string? Folder(IEntity? entity) => entity switch
    {
        Artist a => a.Folder,
        Album a => a.Folder,
        Track t => t.Folder,
        Movie m => m.Folder,
        TVShow s => s.Folder,
        TVShowEpisode e => e.Folder,
        _ => null,
    };

    /// <summary>The media file's name (tracks, movies, episodes); null for folders (artists, albums, shows).</summary>
    public static string? FileName(IEntity? entity) => entity switch
    {
        Track t => t.OriginalFilename,
        Movie m => m.OriginalFilename,
        TVShowEpisode e => e.OriginalFilename,
        _ => null,
    };

    public static string? CoverArt(IEntity? entity) => entity switch
    {
        Album a => a.CoverArtFilename,
        Track t => t.CoverArtFilename,
        Movie m => m.CoverArtFilename,
        TVShow s => s.CoverArtFilename,
        TVShowEpisode e => e.CoverArtFilename,
        _ => null,
    };

    /// <summary>The media file's full path, or the folder for an artist, album or show; null without a folder.</summary>
    public static string? Path(IEntity? entity)
    {
        string? folder = Folder(entity), file = FileName(entity);
        return folder == null ? null : file == null ? folder : System.IO.Path.Combine(folder, file);
    }
}
