namespace PDA.Media.Data.Entities;

public class Track : IEntity, ISoftDeletable, IAuditable, IMediaFile
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public int ArtistId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int TrackNo { get; set; } = 0;
    public int Length { get; set; } = 0;
    public string? Folder { get; set; }
    public string? OriginalFilename { get; set; }
    public string? CoverArtFilename { get; set; }

    // Read from the media file by the scan (see IMediaFile).
    public long? FileSize { get; set; }
    public DateTime? FileDate { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;
    public string? MusicBrainzId { get; set; }

    // Audit fields: set by DataContext when changes are saved.
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = null!;
    public DateTime ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    public virtual Album Album { get; set; } = null!;
}
