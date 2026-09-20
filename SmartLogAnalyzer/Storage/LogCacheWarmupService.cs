using Microsoft.Extensions.Options;

namespace SmartLogAnalyzer.Storage;

/// <summary>
/// Pre-loads recent collections so that API calls are served from memory instead of waiting on
/// blob storage round-trips.
/// </summary>
public sealed class LogCacheWarmupService : BackgroundService
{
    private readonly ILogStore _store;
    private readonly LogStorageOptions _options;
    private readonly ILogger<LogCacheWarmupService> _logger;

    public LogCacheWarmupService(
        ILogStore store,
        IOptions<LogStorageOptions> options,
        ILogger<LogCacheWarmupService> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WarmupEnabled || !_options.IsConfigured)
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(60, _options.WarmupIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    var fileCount = await _store.WarmAsync(_options.WarmupRecentDays, stoppingToken);
                    _logger.LogInformation("Warmed log cache with {FileCount} files.", fileCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Log cache warm-up failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }
}
