using SmartLogAnalyzer.Models;
using SmartLogAnalyzer.Storage;

public class LogInsightsService
{
    private readonly ILogStore _store;
    private readonly MessageNormalizer _normalizer;

    public LogInsightsService(
        ILogStore store,
        MessageNormalizer normalizer)
    {
        _store = store;
        _normalizer = normalizer;
    }

    public async Task<object> GetSummaryAsync(
        LogQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var logs = await LoadAsync(filter, cancellationToken);

        return new
        {
            TotalLogs = logs.Count,
            Errors = logs.Count(log => string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase)),
            Warnings = logs.Count(log => string.Equals(log.Level, "Warning", StringComparison.OrdinalIgnoreCase))
        };
    }

    public async Task<List<object>> GetTopMessagesAsync(
        LogQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var logs = await LoadAsync(filter, cancellationToken);

        return logs
            .GroupBy(log => log.Message)
            .Select(group => new
            {
                Message = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(entry => entry.Count)
            .Take(filter.ResolvedTop)
            .Cast<object>()
            .ToList();
    }

    public async Task<List<object>> GetLogsAsync(
        LogQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var logs = await LoadAsync(filter, cancellationToken);

        return logs
            .OrderByDescending(log => log.Timestamp)
            .Take(filter.ResolvedLimit)
            .Select(log => new
            {
                log.Timestamp,
                log.Level,
                log.Message,
                log.DeviceId,
                log.EffectiveDeviceId,
                log.Source,
                log.EventId,
                log.FilePath
            })
            .Cast<object>()
            .ToList();
    }

    public async Task<List<LogIssueGroup>> GetSmartGroupsAsync(
        LogQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var logs = await LoadAsync(filter, cancellationToken);

        return logs
            .GroupBy(log => _normalizer.Normalize(log.Message))
            .Select(group => new LogIssueGroup
            {
                Pattern = group.Key,
                Count = group.Count(),
                Level = filter.Level ?? "Unknown",
                FirstOccurrence = group.Min(log => log.Timestamp),
                LastOccurrence = group.Max(log => log.Timestamp)
            })
            .OrderByDescending(group => group.Count)
            .Take(filter.ResolvedTop)
            .ToList();
    }

    private async Task<List<LogRecord>> LoadAsync(LogQueryFilter filter, CancellationToken cancellationToken)
    {
        var logs = await _store.GetGatewayLogsAsync(filter.ToScope(), cancellationToken);
        return filter.ApplyTo(logs).ToList();
    }
}
