using Microsoft.EntityFrameworkCore;
using PDA.Media.Data.Contexts;
using PDA.Media.Data.Entities;

namespace PDA.Media.Data.Services;

public class AlbumService : IDataService<Album>
{
    private readonly DataContext _dbContext;
        
    public AlbumService()
    {
        _dbContext = new DataContext();    
    }
    public AlbumService(DataContext dbContext)
    {
        _dbContext = dbContext;    
    }
    public AlbumService(string connectionString)
    {
        _dbContext = new DataContext(connectionString);    
    }

    public Album? GetRecordById(int id)
    {
        Album? album = _dbContext.Albums.Find(id);
        return album;  
    }
    public Album? GetRecordByArtistIdAndName(int artistId, string name)
    {
        Album? album = _dbContext.Albums.FirstOrDefault(a => a.ArtistId == artistId && a.Name == name);
        return album;  
    }

    public List<Album> GetAllRecords()
    {
        List<Album> albums = _dbContext.Albums.Where(album => album.IsDeleted == false).ToList();
        return albums; 
    }

    public List<Album> GetAllRecords(string searchTerm)
    {
        return GetAllRecords(searchTerm, false);
    }
    public List<Album> GetAllRecords(string searchTerm, bool includeArtists)
    {
        // TODO: I have hard-coded the option to include the artist in the search.
        // Really needs to be a parameter.
        
        // Make sure we have a valid searchTerm.
        string cleanSearchTerm = searchTerm.Trim();
        if (string.IsNullOrWhiteSpace(cleanSearchTerm)) return GetAllRecords();

        // Do we have a wildcard anywhere in the searchTerm?
        List<Album> albums;
        if (cleanSearchTerm.Contains('*'))
        {
            // Make sure we have at least one character other than the wildcard.
            if (cleanSearchTerm.Length == 1) return GetAllRecords();
            if (cleanSearchTerm.StartsWith('*'))
            {
                // Is the wildcard at the beginning of the string.
                // We need to use the EndsWith method.
                cleanSearchTerm = cleanSearchTerm.Replace("*", "");
                albums = _dbContext.Albums.Where(album => album.IsDeleted == false && album.Name.EndsWith(cleanSearchTerm)).Include(a=>a.Artist).ToList();
            }
            else
            {
                if (cleanSearchTerm.EndsWith('*'))
                {
                    // Is the wildcard at the end of the string.
                    // We need to use the StartsWith method.
                    cleanSearchTerm = cleanSearchTerm.Replace("*", "");
                    albums = _dbContext.Albums.Where(album => album.IsDeleted == false && album.Name.StartsWith(cleanSearchTerm)).Include(a=>a.Artist).ToList();
                }
                else
                {
                    // The wildcard must be in the middle of the string.
                    albums = _dbContext.Albums.Where(album => album.IsDeleted == false && album.Name.Contains(cleanSearchTerm)).Include(a=>a.Artist).ToList();
                }
            }
        }
        else
        {
            // No wildcards, so do a simple Contains search.
            albums = _dbContext.Albums.Where(album => album.IsDeleted == false && album.Name.Contains(searchTerm)).Include(a=>a.Artist).ToList();
        }
        
        return albums; 
    }
    public List<Album> GetAllRecordsByArtistId(int artistId)
    {
        List<Album> albums = _dbContext.Albums.Where(album => album.IsDeleted == false && album.ArtistId == artistId).ToList();
        return albums; 
    }

    public Album? AddRecord(Album? record)
    {
        if (record is null) return null;

        // Check if the record already exists.
        Album? existingRecord = _dbContext.Albums.FirstOrDefault(a => a.ArtistId == record.ArtistId && a.Name == record.Name);
        if (existingRecord is not null) return existingRecord;
            
        // Add the record to the database.
        _dbContext.Albums.Add(record);
        _dbContext.SaveChanges();
            
        return record;
    }
    public Album? UpdateRecord(int id, Album? record)
    {
        if (record is null) return null;
            
        var album = _dbContext.Albums.Find(id);
        if (album is null) return null;

        album.ArtistId = record.ArtistId;
        album.Name = record.Name;
        album.Description = record.Description;
        album.Folder = record.Folder;
        album.MusicBrainzId = record.MusicBrainzId;
        album.Notes = record.Notes;
        album.IsFavourite = record.IsFavourite;
        album.IsDeleted = record.IsDeleted;
        album.ModifiedOn = DateTime.Now;
        album.ModifiedBy = "API";   
        
        int recordsAffected = _dbContext.SaveChanges();
        return album;
    }

    public bool DeleteRecord(int id)
    {
        Album? album = _dbContext.Albums.Find(id);
        return DeleteRecord(album);
    }
    public bool DeleteRecord(Album? record)
    {
        if (record is null) return false;
            
        record.IsDeleted = true;
        int recordsAffected = _dbContext.SaveChanges();
            
        return recordsAffected > 0;          
    }

    public bool TruncateTable()
    {
        try
        {
            string sqlString = "DBCC CHECKIDENT ('Albums', RESEED, 0)";
            
            _dbContext.Albums.ExecuteDelete();
            _dbContext.Database.ExecuteSqlRaw(sqlString);
            
            return true;
        }
        catch { return false; }
    }

    public async Task<Album?> GetRecordByIdAsync(int id)
    {
        Album? album = await _dbContext.Albums.FindAsync(id).ConfigureAwait(false);
        return album; 
    }
    public async Task<List<Album>> GetAllRecordsAsync()
    {
        List<Album> albums = await _dbContext.Albums.Where(a => a.IsDeleted == false).ToListAsync().ConfigureAwait(false);
        return albums;
    }
    public async Task<List<Album>> GetAllRecordsAsync(string searchTerm)
    {
        List<Album> albums = await _dbContext.Albums.Where(a => a.IsDeleted == false && a.Name.Contains(searchTerm)).ToListAsync().ConfigureAwait(false);
        return albums;
    }
    public async Task<List<Album>> GetAllRecordsByArtistIdAsync(int artistId)
    {
        List<Album> albums = await _dbContext.Albums.Where(a => a.IsDeleted == false && a.ArtistId == artistId).ToListAsync().ConfigureAwait(false);
        return albums;
    }

    public async Task<Album?> AddRecordAsync(Album? record)
    {
        return await AddRecordAsync(record, true);
    }
    public async Task<Album?> AddRecordAsync(Album? record, bool saveChanges)
    {
        if (record is null) return null;
        
        // Check if the record already exists.
        Album? existingRecord = await _dbContext.Albums.FirstOrDefaultAsync(a => a.ArtistId == record.ArtistId && a.Name == record.Name).ConfigureAwait(false);
        if (existingRecord is not null) return existingRecord;

        // Add the record to the database.
        await _dbContext.Albums.AddAsync(record).ConfigureAwait(false);
        if (saveChanges) await _dbContext.SaveChangesAsync().ConfigureAwait(false);
        
        return record;
    }
    public async Task<Album?> UpdateRecordAsync(int id, Album? record)
    {
        if (record is null) return null;
            
        var album = await _dbContext.Albums.FindAsync(id).ConfigureAwait(false);
        if (album is null) return null;

        album.ArtistId = record.ArtistId;
        album.Name = record.Name;
        album.Description = record.Description;
        album.Folder = record.Folder;
        album.MusicBrainzId = record.MusicBrainzId;
        album.Notes = record.Notes;
        album.IsFavourite = record.IsFavourite;
        album.IsDeleted = record.IsDeleted;
        album.ModifiedOn = DateTime.Now;
        album.ModifiedBy = "API";   
        
        int recordsAffected = await _dbContext.SaveChangesAsync().ConfigureAwait(false);
        return album;    
    }
    public async Task<bool> DeleteRecordAsync(int id)
    {
        Album? album = await _dbContext.Albums.FindAsync(id).ConfigureAwait(false);
        return DeleteRecord(album);
    }
    public async Task<bool> DeleteRecordAsync(Album? record)
    {
        if (record is null) return false;
            
        record.IsDeleted = true;
        int recordsAffected = await _dbContext.SaveChangesAsync().ConfigureAwait(false);
            
        return recordsAffected > 0;   
    }

    public async Task<bool> TruncateTableAsync()
    {
        try
        {
            string sqlString = "DBCC CHECKIDENT ('Albums', RESEED, 0)";
            
            await _dbContext.Albums.ExecuteDeleteAsync().ConfigureAwait(false);
            await _dbContext.Database.ExecuteSqlRawAsync(sqlString).ConfigureAwait(false);
            
            return true;
        }
        catch { return false; }   
    }
}