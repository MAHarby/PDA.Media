using Microsoft.Extensions.Logging;

namespace PDA.Media.Scanner.Services;

public class MediaScanner
{
    private readonly ILogger<MediaScanner> _logger;
    
    public MediaScanner(ILogger<MediaScanner> logger)
    {
        _logger = logger;
    }

    public bool ScanAllMediaCategories()
    {
        return true;
    }
}