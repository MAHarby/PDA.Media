namespace PDA.Media.Data.Entities;

public class TVShow
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int TVShowTypeId { get; set; } = 0;
    public int ReleaseYear { get; set; } = 0;
    public string? Notes { get; set; }
    public string? Folder { get; set; }
    public string? CoverArtFilename { get; set; }
    public string? TMDB_Id { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;

    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "API";
    public DateTime UpdatedOn { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }

    public virtual TVShowType TvShowType { get; set; } = null!;
    public virtual ICollection<TVShowEpisode> Episodes { get; set; } = new List<TVShowEpisode>(); 
}