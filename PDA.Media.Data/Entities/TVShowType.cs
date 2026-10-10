namespace PDA.Media.Data.Entities;

public class TVShowType : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public virtual ICollection<TVShow> TVShows { get; set; } = new List<TVShow>();
}
