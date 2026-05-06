using Microsoft.EntityFrameworkCore;

public class LogInsightsService
{
    private readonly AppDbContext _db;

    public LogInsightsService(AppDbContext db)
    {
        _db = db;
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

    public async Task<List<object>> GetLogsByLevelAsync(string level)
    {
        return await _db.Logs
            .Where(x => x.Level == level)
            .OrderByDescending(x => x.Timestamp)
            .Take(100)
            .Cast<object>()
            .ToListAsync();
    }
}