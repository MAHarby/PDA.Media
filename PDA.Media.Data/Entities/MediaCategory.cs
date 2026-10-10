namespace PDA.Media.Data.Entities;

public class MediaCategory : IEntity, ISoftDeletable
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? RootFolder { get; set; }
    public string MediaClass { get; set; } = null!;
    public int AlbumTypeId { get; set; } = 5;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
}
