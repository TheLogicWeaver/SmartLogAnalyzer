using Microsoft.EntityFrameworkCore;
using SmartLogAnalyzer.Models;

public class LogInsightsService
{
    private readonly AppDbContext _db;
    private readonly MessageNormalizer _normalizer;

    public LogInsightsService(
        AppDbContext db,
        MessageNormalizer normalizer)
    {
        _db = db;
        _normalizer = normalizer;
    }

    public async Task<object> GetSummaryAsync()
    {
        var total = await _db.Logs.CountAsync();

        var errors = await _db.Logs
            .CountAsync(x => x.Level == "Error");

        var warnings = await _db.Logs
            .CountAsync(x => x.Level == "Warning");

        return new
        {
            TotalLogs = total,
            Errors = errors,
            Warnings = warnings
        };
    }

    public async Task<List<object>> GetTopMessagesAsync(
        LogQueryFilter filter)
    {
        var query = filter.ApplyTo(_db.Logs);

        return await query
            .GroupBy(x => x.Message)
            .Select(g => new
            {
                Message = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(filter.Top)
            .Cast<object>()
            .ToListAsync();
    }

    public async Task<List<object>> GetLogsAsync(
        LogQueryFilter filter)
    {
        var query = filter.ApplyTo(_db.Logs);

        return await query
            .OrderByDescending(x => x.Timestamp)
            .Take(filter.Limit)
            .Cast<object>()
            .ToListAsync();
    }

    public async Task<List<LogIssueGroup>> GetSmartGroupsAsync(
        LogQueryFilter filter)
    {
        var query = filter.ApplyTo(_db.Logs);

        var logs = await query.ToListAsync();

        return logs
            .GroupBy(x =>
                _normalizer.Normalize(x.Message))
            .Select(g => new LogIssueGroup
            {
                Pattern = g.Key,
                Count = g.Count(),
                Level = filter.Level ?? "Unknown",
                FirstOccurrence = g.Min(x => x.Timestamp),
                LastOccurrence = g.Max(x => x.Timestamp)
            })
            .OrderByDescending(x => x.Count)
            .Take(filter.Top)
            .ToList();
    }

}
