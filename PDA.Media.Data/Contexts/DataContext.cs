using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Contexts;

public class DataContext : DbContext
{
    /// <summary>
    /// Name of the global query filter that hides soft-deleted rows (IsDeleted = 1).
    /// Use <c>IgnoreQueryFilters([DataContext.SoftDeleteFilter])</c> to include them.
    /// </summary>
    public const string SoftDeleteFilter = "SoftDelete";

    // TODO: Need to move this out when we release the app.
    private readonly string connectionString = "Server=PDA-Main;Database=Media.Master;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=Yes";

    public DataContext() { }
    public DataContext(string connectionString) => this.connectionString = connectionString;
    public DataContext(DbContextOptions<DataContext> options) : base(options) { }

    public virtual DbSet<Album> Albums { get; set; }
    public virtual DbSet<AlbumType> AlbumTypes { get; set; }
    public virtual DbSet<Artist> Artists { get; set; }
    public virtual DbSet<MediaCategory> MediaCategories { get; set; }
    public virtual DbSet<Movie> Movies { get; set; }
    public virtual DbSet<MovieType> MovieTypes { get; set; }
    public virtual DbSet<Setting> Settings { get; set; }
    public virtual DbSet<Track> Tracks { get; set; }
    public virtual DbSet<TVShow> TVShows { get; set; }
    public virtual DbSet<TVShowType> TVShowTypes { get; set; }
    public virtual DbSet<TVShowEpisode> TVShowEpisodes { get; set; }

    /// <summary>
    /// The name written to CreatedBy / ModifiedBy when changes are saved.
    /// </summary>
    public string AuditUser { get; set; } = "API";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Only used by the parameterless and connection string constructors. When options are passed in
        // (DbContextOptions<DataContext>), the caller has already chosen the provider and logging.
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer(connectionString).LogTo(Console.WriteLine, LogLevel.Information);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Applies every IEntityTypeConfiguration<T> (the EntityMaps) in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataContext).Assembly);
    }

    // SaveChanges() and SaveChangesAsync() without arguments call these overloads, so every save is stamped.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Sets the audit fields of every added or modified <see cref="IAuditable"/> entity, so callers never set them.
    /// Times are local (DateTime.Now), matching the GETDATE() defaults already in the database.
    /// </summary>
    private void StampAuditFields()
    {
        DateTime now = DateTime.Now;

        // Entries() runs DetectChanges first, so entities changed since they were loaded show as Modified.
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedOn = now;
                    entry.Entity.CreatedBy = AuditUser;
                    entry.Entity.ModifiedOn = now;
                    entry.Entity.ModifiedBy = AuditUser;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedOn = now;
                    entry.Entity.ModifiedBy = AuditUser;

                    // Never overwrite when and by whom the row was created.
                    entry.Property(e => e.CreatedOn).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    break;
            }
        }
    }

    public bool CanConnect()
    {
        bool canConnect = Database.CanConnect();
        return canConnect;
    }
    public async Task<bool> CanConnectAsync()
    {
        bool canConnect = await Database.CanConnectAsync().ConfigureAwait(false);
        return canConnect;
    }
}
