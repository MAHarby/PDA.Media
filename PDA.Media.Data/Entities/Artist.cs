namespace PDA.Media.Data.Entities;

public class Artist : IEntity, ISoftDeletable, IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Folder { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;
    public string? Notes { get; set; }
    public string? MusicBrainzId { get; set; }

    // Audit fields: set by DataContext when changes are saved.
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
}
