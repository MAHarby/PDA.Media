namespace PDA.Media.Data.Services;

/// <summary>
/// What <see cref="MediaCacheService.ClearMediaCache"/> removed: the number of rows deleted from each media cache table.
/// The property names match the columns dbo.ClearMediaCache returns.
/// </summary>
public sealed class MediaCacheCleared
{
    public int Tracks { get; init; }
    public int Albums { get; init; }
    public int TVShowEpisodes { get; init; }
    public int TVShows { get; init; }
    public int Movies { get; init; }
    public int Artists { get; init; }

    public int Total => Tracks + Albums + TVShowEpisodes + TVShows + Movies + Artists;
}
