using Microsoft.Extensions.Options;

using NpmCdn.Api.Configuration;
using NpmCdn.Storage;

namespace NpmCdn.Api.Services;

public partial class EvictionBackgroundService(
    IStorageProvider storageProvider,
    IOptions<EvictionOptions> options,
    ILogger<EvictionBackgroundService> logger) : BackgroundService
{
    private readonly IStorageProvider _storageProvider = storageProvider;
    private readonly ILogger<EvictionBackgroundService> _logger = logger;
    private readonly EvictionOptions _options = options.Value;

    [LoggerMessage(LogLevel.Information, "Cache Eviction Service started. Interval: {Interval}, MaxInactivity: {MaxInactivity}")]
    private partial void LogServiceStarted(TimeSpan interval, TimeSpan maxInactivity);

    [LoggerMessage(LogLevel.Information, "Looking for stale packages last accessed before {CutoffDate}")]
    private partial void LogLookingForStalePackages(DateTime cutoffDate);

    [LoggerMessage(LogLevel.Information, "Found {Count} stale packages to evict.")]
    private partial void LogFoundStalePackages(int count);

    [LoggerMessage(LogLevel.Information, "Evicting {PackageName}@{Version}")]
    private partial void LogEvictingPackage(string packageName, string version);

    [LoggerMessage(LogLevel.Error, "Failed to evict {PackageName}@{Version}")]
    private partial void LogFailedToEvictPackage(Exception ex, string packageName, string version);

    [LoggerMessage(LogLevel.Debug, "No stale packages found.")]
    private partial void LogNoStalePackagesFound();

    [LoggerMessage(LogLevel.Error, "An error occurred during cache eviction execution.")]
    private partial void LogErrorDuringEviction(Exception ex);

    [LoggerMessage(LogLevel.Information, "Cache Eviction Service stopping.")]
    private partial void LogServiceStopping();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarted(_options.RunInterval, _options.MaxInactivityPeriod);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.Subtract(_options.MaxInactivityPeriod);
                LogLookingForStalePackages(cutoffDate);

                var stalePackages = (await _storageProvider.GetStalePackagesAsync(cutoffDate, stoppingToken)).ToList();

                if (stalePackages.Count > 0)
                {
                    LogFoundStalePackages(stalePackages.Count);

                    foreach (var (packageName, version) in stalePackages)
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        LogEvictingPackage(packageName, version);
                        try
                        {
                            await _storageProvider.DeletePackageVersionAsync(packageName, version, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            LogFailedToEvictPackage(ex, packageName, version);
                        }
                    }
                }
                else
                {
                    LogNoStalePackagesFound();
                }
            }
            catch (Exception ex)
            {
                LogErrorDuringEviction(ex);
            }

            // Wait for the next interval before running the sweep again
            await Task.Delay(_options.RunInterval, stoppingToken);
        }

        LogServiceStopping();
    }
}