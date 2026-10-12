using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PDA.Media.Data.Entities;
using PDA.Media.Data.Services;

namespace PDA.Media.Scanner.Services;

/// <summary>
/// Scans the media folders of each active media category into the database.
/// </summary>
/// <remarks>
/// Everything it needs comes in through the constructor. In the app, dependency injection supplies it (see
/// <see cref="ScannerServiceCollectionExtensions.AddMediaScanner"/>); tests and tools can pass the services in
/// themselves. The data services are singletons that create a short-lived DataContext per call, so the scanner can
/// keep them for its whole life.
/// </remarks>
public class MediaScanner
{
    private readonly MediaCategoryService _categoryService;
    private readonly ArtistService _artistService;
    private readonly AlbumService _albumService;
    private readonly TrackService _trackService;
    private readonly MovieService _movieService;
    private readonly TVShowService _tvShowService;
    private readonly TVShowEpisodeService _episodeService;
    private readonly ILogger<MediaScanner> _logger;

    public MediaScanner(MediaCategoryService categoryService, ArtistService artistService, AlbumService albumService,
        TrackService trackService, MovieService movieService, TVShowService tvShowService, TVShowEpisodeService episodeService,
        ILogger<MediaScanner>? logger = null)
    {
        _categoryService = categoryService;
        _artistService = artistService;
        _albumService = albumService;
        _trackService = trackService;
        _movieService = movieService;
        _tvShowService = tvShowService;
        _episodeService = episodeService;
        _logger = logger ?? NullLogger<MediaScanner>.Instance;
    }

    /// <summary>
    /// Step through each media category and scan all media files based on the category's root folder.
    /// </summary>
    /// <param name="clearData">If true, then clear all existing media data before scanning.</param>
    /// <returns>The count of media files added.</returns>
    public bool ScanAllMediaCategories(bool clearData = false)
    {
        int mediaFilesAdded = 0;
        List<MediaCategory> categories = _categoryService.GetActiveRecords();
        _logger.LogInformation("Scanning {Count} active media categories", categories.Count);

        if (clearData) ClearAllData();
        
        foreach (MediaCategory category in categories)
        {
            _logger.LogInformation("Media category {Category} ({MediaClass}): {RootFolder}", category.Name, category.MediaClass, category.RootFolder ?? "(no root folder)");
            
            
            
        }





        return true;
    }

    private bool ClearAllData()
    {
        // _categoryService.ClearExistingData();
        
        return true;
    }
}
