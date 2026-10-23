using Microsoft.Extensions.DependencyInjection;
using PDA.Media.Scanner.Services;

namespace PDA.Media.Scanner;

/// <summary>
/// Registers the scanner with dependency injection. It needs the data layer, so register that too:
/// <c>services.AddMediaData(...).AddMediaScanner();</c>
/// </summary>
public static class ScannerServiceCollectionExtensions
{
    public static IServiceCollection AddMediaScanner(this IServiceCollection services)
    {
        services.AddSingleton<MediaScanner>();
        services.AddSingleton<MediaRenameService>();
        services.AddSingleton<MediaDiskCheckService>();
        return services;
    }
}
