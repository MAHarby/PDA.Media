namespace PDA.Media.Data.Entities;

public class Album : IAuditable
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int AlbumTypeId { get; set; } = 1;
    public int TrackCount { get; set; } = 1;
    public int Length { get; set; }
    public string? Folder { get; set; }
    public string? OriginalFilename { get; set; }
    public string? CoverArtFilename { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsFavourite { get; set; }
    public string? MusicBrainzId { get; set; }

    // Audit fields: set by DataContext when changes are saved.
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    public virtual AlbumType AlbumType { get; set; } = null!;
    public virtual Artist Artist { get; set; } = null!;
    public virtual ICollection<Track> Tracks { get; set; } = new List<Track>();
}
