namespace PDA.Media.Data.Entities;

public class TVShowEpisode
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
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "API";
    public DateTime UpdatedOn { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; } = null!;

    public virtual TVShow TVShow { get; set; } = null!;
}