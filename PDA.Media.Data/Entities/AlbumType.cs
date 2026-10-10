namespace PDA.Media.Data.Entities;

public class AlbumType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    
    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();

    // public AlbumType(string name, string? description)
    // {
    //     Name = name;
    //     Description = description;
    // }
}