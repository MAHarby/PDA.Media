namespace PDA.Media.Data.Entities;

/// <summary>
/// An entity with audit fields. <see cref="Contexts.DataContext"/> sets them when changes are saved.
/// </summary>
public interface IAuditable
{
    DateTime CreatedOn { get; set; }
    string CreatedBy { get; set; }
    DateTime ModifiedOn { get; set; }
    string? ModifiedBy { get; set; }
}
