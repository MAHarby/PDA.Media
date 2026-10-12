namespace PDA.Media.Data.Services;

/// <summary>What <see cref="DataService{TEntity}.AddOrRestoreRecord"/> did.</summary>
public enum AddOutcome
{
    /// <summary>A live record with the same natural key already existed; it is returned unchanged.</summary>
    Existing,

    /// <summary>The record was added.</summary>
    Added,

    /// <summary>A soft-deleted record with the same natural key was brought back (IsDeleted = false), keeping its id and everything stored with it.</summary>
    Restored,
}
