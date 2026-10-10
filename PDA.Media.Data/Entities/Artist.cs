namespace PDA.Media.Data.Entities;

public class Artist
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Folder { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;
    public string? Notes { get; set; }
    public string? MusicBrainzId { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "API";
    public DateTime ModifiedOn { get; set; } = DateTime.Now;
    public string? ModifiedBy { get; set; } = null!;

    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
        
    // public Artist() {}
    // public Artist(string name, string? description)
    // {
    //     Name = name;
    //     Description = description;
    // }
}