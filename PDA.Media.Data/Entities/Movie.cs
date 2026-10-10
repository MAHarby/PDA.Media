namespace PDA.Media.Data.Entities;

public class Movie : IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int MovieTypeId { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;
    public string? TMDB_Id { get; set; }

    // Audit fields: set by DataContext when changes are saved.
    // ModifiedOn / ModifiedBy are stored in the UpdatedOn / UpdatedBy columns (see MovieEntityMap).
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    public string? Folder { get; set; }
    public string? OriginalFilename { get; set; }
    public string? CoverArtFilename { get; set; }

    public virtual MovieType MovieType { get; set; } = null!;
}
