using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using PDA.Media.Data.Entities;
using PDA.Media.Data.Entities.EntityMaps;
using Serilog;

namespace PDA.Media.Data.Contexts;

public partial class DataContext : DbContext
{
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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("Logs/Media.ProcessMediaData.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        Log.Information("Configuring database context ...");
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer(connectionString).LogTo(Console.WriteLine, LogLevel.Information);
        }
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AlbumEntityMap());
        modelBuilder.ApplyConfiguration(new AlbumTypeEntityMap());
        modelBuilder.ApplyConfiguration(new ArtistEntityMap());
        modelBuilder.ApplyConfiguration(new MediaCategoryEntityMap());
        modelBuilder.ApplyConfiguration(new MovieEntityMap());
        modelBuilder.ApplyConfiguration(new MovieTypeEntityMap());
        modelBuilder.ApplyConfiguration(new SettingEntityMap());
        modelBuilder.ApplyConfiguration(new TrackEntityMap());
        modelBuilder.ApplyConfiguration(new TVShowEntityMap());
        modelBuilder.ApplyConfiguration(new TVShowTypeEntityMap());
        modelBuilder.ApplyConfiguration(new TVShowEpisodeEntityMap());

        OnModelCreatingPartial(modelBuilder);
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
    
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
