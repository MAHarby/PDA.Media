namespace PDA.Media.Data.Entities;

public class Album
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int AlbumTypeId { get; set; }
    public int TrackCount { get; set; }
    public int Length { get; set; }
    public string? Folder { get; set; }
    public string? OriginalFilename { get; set; }
    public string? CoverArtFilename { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsFavourite { get; set; }
    public string? MusicBrainzId { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = null!;
    public DateTime ModifiedOn { get; set; } = DateTime.Now;
    public string ModifiedBy { get; set; } = "API";

    public virtual AlbumType AlbumType { get; set; } = null!;
    public virtual Artist Artist { get; set; } = null!;
    public virtual ICollection<Track> Tracks { get; set; } = new List<Track>();
        
    // public Album(int artistId, int albumType, string name, string? description)
    // {
    //     ArtistId = artistId;
    //     AlbumType = albumType;
    //     Name = name;
    //     Description = description;
    // }
}