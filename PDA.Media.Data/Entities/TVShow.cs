namespace PDA.Media.Data.Entities;

public class TVShow : IEntity, ISoftDeletable, IAuditable
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

    // Audit fields: set by DataContext when changes are saved.
    // ModifiedOn / ModifiedBy are stored in the UpdatedOn / UpdatedBy columns (see TVShowEntityMap).
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    public virtual TVShowType TvShowType { get; set; } = null!;
    public virtual ICollection<TVShowEpisode> Episodes { get; set; } = new List<TVShowEpisode>();
}
