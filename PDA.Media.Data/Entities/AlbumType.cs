namespace PDA.Media.Data.Entities;

public class AlbumType : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
}
