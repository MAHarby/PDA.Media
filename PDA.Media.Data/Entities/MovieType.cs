namespace PDA.Media.Data.Entities;

public class MovieType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    
    public virtual ICollection<Movie> Movies { get; set; } = new List<Movie>();
    
    // public MovieType(string name, string? description)
    // {
    //     Name = name;
    //     Description = description;
    // }
}