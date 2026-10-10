namespace PDA.Media.Data.Entities;

public class TVShowEpisode : IAuditable
{
    public int Id { get; set; }
    public int TVShowId { get; set; }
    public int SeasonNo { get; set; } = 0;
    public int EpisodeNo { get; set; } = 0;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? TMDB_Id { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? Folder { get; set; }
    public string? OriginalFilename { get; set; }
    public string? CoverArtFilename { get; set; }

    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;

    // Audit fields: set by DataContext when changes are saved.
    // ModifiedOn / ModifiedBy are stored in the UpdatedOn / UpdatedBy columns (see TVShowEpisodeEntityMap).
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    public virtual TVShow TVShow { get; set; } = null!;
}
