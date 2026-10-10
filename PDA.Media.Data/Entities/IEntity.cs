namespace PDA.Media.Data.Entities;

/// <summary>An entity with an int key and a name; what <see cref="Services.DataService{TEntity}"/> works with.</summary>
public interface IEntity
{
    int Id { get; set; }
    string Name { get; set; }
}

/// <summary>An entity that is soft-deleted (IsDeleted = true) rather than removed, and hidden by the query filter.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
