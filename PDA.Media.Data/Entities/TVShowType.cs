namespace PDA.Media.Data.Entities;

public class TVShowType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public virtual ICollection<TVShow> TVShows { get; set; } = new List<TVShow>();
    
    // public TVShowType(string name, string? description)
    // {
    //     Name = name;
    //     Description = description;
    // }
}