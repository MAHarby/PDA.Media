namespace PDA.Media.Data.Entities;

public class Movie
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int MovieTypeId { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsFavourite { get; set; } = false;
    public string? TMDB_Id { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "API";
    public DateTime UpdatedOn { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }

    public string? Folder { get; set; }
    public string? OriginalFilename { get; set; }
    public string? CoverArtFilename { get; set; }

    public virtual MovieType MovieType { get; set; } = null!;

    // public Movie(int movieTypeId, string name, string? description, string? folder, string? originalFilename)
    // {
    //     MovieTypeId = movieTypeId;
    //     Name = name;
    //     Description = description;
    //     Folder = folder;
    //     OriginalFilename = originalFilename;
    // }
}