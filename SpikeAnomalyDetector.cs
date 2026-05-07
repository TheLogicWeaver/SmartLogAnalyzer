using Microsoft.EntityFrameworkCore;

public class SpikeAnomalyDetector : IAnomalyDetector
{
    private readonly AppDbContext _db;
    private readonly MessageNormalizer _normalizer;

    public SpikeAnomalyDetector(
        AppDbContext db,
        MessageNormalizer normalizer)
    {
        _db = db;
        _normalizer = normalizer;
    }

    public async Task<List<AnomalyDetectionResult>> DetectAsync(
        string level,
        int lookbackHours,
        int currentWindowMinutes)
    {
        var now = DateTime.UtcNow;

        var currentWindowStart =
            now.AddMinutes(-currentWindowMinutes);

        var historicalWindowStart =
            now.AddHours(-lookbackHours);

        var logs = await _db.Logs
            .Where(x =>
                x.Level == level &&
                x.Timestamp >= historicalWindowStart)
            .ToListAsync();

        var normalizedLogs = logs
            .Select(x => new
            {
                Pattern = _normalizer.Normalize(x.Message),
                x.DeviceId,
                x.Timestamp
            })
            .ToList();

        var currentWindowLogs = normalizedLogs
            .Where(x => x.Timestamp >= currentWindowStart)
            .ToList();

        var groupedCurrent = currentWindowLogs
            .GroupBy(x => x.Pattern);

        var anomalies = new List<AnomalyDetectionResult>();

        foreach (var group in groupedCurrent)
        {
            var historicalLogs = normalizedLogs
            .Where(x => x.Timestamp < currentWindowStart)
            .ToList();

            var historicalCount = historicalLogs
            .Count(x => x.Pattern == group.Key);
            
            var averagePerHour =
                (double)historicalCount / lookbackHours;

            var currentCount = group.Count();

            double spikeFactor;
            
            if (averagePerHour <= 0)
            {
                spikeFactor = currentCount;
            }
            else
            {
                spikeFactor = currentCount / averagePerHour;
            }

            if (spikeFactor < 1.2)
                continue;

            anomalies.Add(new AnomalyDetectionResult
            {
                Pattern = group.Key,
                Level = level,
                CurrentCount = currentCount,
                HistoricalAverage = averagePerHour,
                SpikeFactor = spikeFactor,
                AffectedDevices = group
                    .Select(x => x.DeviceId)
                    .Distinct()
                    .Count(),
                FirstOccurrence = group.Min(x => x.Timestamp),
                LastOccurrence = group.Max(x => x.Timestamp)
            });
        }

        return anomalies
            .OrderByDescending(x => x.SpikeFactor)
            .ToList();
    }
}