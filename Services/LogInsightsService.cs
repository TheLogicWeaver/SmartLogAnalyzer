using Microsoft.EntityFrameworkCore;

public class LogInsightsService
{
    private readonly AppDbContext _db;
    private readonly MessageNormalizer _normalizer;

    public LogInsightsService(AppDbContext db, MessageNormalizer normalizer)
    {
        _db = db;
        _normalizer = normalizer;
    }

    public async Task<object> GetSummaryAsync()
    {
        var total = await _db.Logs.CountAsync();

        var errors = await _db.Logs.CountAsync(x => x.Level == "Error");
        var warnings = await _db.Logs.CountAsync(x => x.Level == "Warning");

        return new
        {
            TotalLogs = total,
            Errors = errors,
            Warnings = warnings
        };
    }

    public async Task<List<object>> GetTopMessagesByLevelAsync(string level, int top = 10)
    {
        return await _db.Logs
            .Where(x => x.Level == level)
            .GroupBy(x => x.Message)
            .Select(g => new
            {
                Message = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .Cast<object>()
            .ToListAsync();
    }

    public async Task<List<object>> GetTopMessagesByLevelAndDeviceIdAsync(string level, string deviceId, int top = 10)
    {
        return await _db.Logs
            .Where(x => x.Level == level && x.DeviceId == deviceId)
            .GroupBy(x => x.Message)
            .Select(g => new
            {
                Message = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .Cast<object>()
            .ToListAsync();
    }

    public async Task<List<object>> GetLogsByLevelAsync(string level)
    {
        return await _db.Logs
            .Where(x => x.Level == level)
            .OrderByDescending(x => x.Timestamp)
            .Take(100)
            .Cast<object>()
            .ToListAsync();
    }

    public async Task<List<object>> GetLogsByLevelDeviceIdAsync(string level, string deviceId)
    {
        return await _db.Logs
        .Where(x => x.Level == level && x.DeviceId == deviceId)
        .OrderByDescending(x => x.Timestamp)
        .Take(1000)
        .Cast<object>()
        .ToListAsync();
    }

    public async Task<List<LogIssueGroup>> GetSmartGroupsAsync(
        string level,
        int top)
    {
        var logs = await _db.Logs
            .Where(x => x.Level == level)
            .ToListAsync();

        var grouped = logs
            .GroupBy(x => _normalizer.Normalize(x.Message))
            .Select(g => new LogIssueGroup
            {
                Pattern = g.Key,
                Count = g.Count(),
                Level = level,
                FirstOccurrence = g.Min(x => x.Timestamp),
                LastOccurrence = g.Max(x => x.Timestamp)
            })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToList();

        return grouped;
    }

    public async Task<List<LogIssueGroup>> GetSmartGroupsByDeviceId(string level, int top, string deviceId)
    {
        var logs = await _db.Logs
            .Where(x => x.Level == level && x.DeviceId == deviceId)
            .ToListAsync();

        var grouped = logs
            .GroupBy(x => _normalizer.Normalize(x.Message))
            .Select(g => new LogIssueGroup
            {
                Pattern = g.Key,
                Count = g.Count(),
                Level = level,
                FirstOccurrence = g.Min(x => x.Timestamp),
                LastOccurrence = g.Max(x => x.Timestamp)
            })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToList();

        return grouped;
    }
}