namespace PDA.Media.Data.Entities;

public class Track
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
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;
    public string? MusicBrainzId { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "API";
    public DateTime ModifiedOn { get; set; } = DateTime.Now;
    public string? ModifiedBy { get; set; }

    public virtual Album Album { get; set; } = null!;

    // public Track(int albumId, int artistId, string name, int trackNo, string? folder, string? originalFilename)
    // {
    //     AlbumId = albumId;
    //     ArtistId = artistId;
    //     Name = name;
    //     Description = name;
    //     Folder = folder;
    //     OriginalFilename = originalFilename;
    //     TrackNo = trackNo;
    // }
}