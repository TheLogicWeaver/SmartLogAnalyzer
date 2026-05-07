
public class LogIngestionService
{
    private readonly LogProcessingService _processingService;
    private readonly AppDbContext _db;

    public LogIngestionService(
        LogProcessingService processingService,
        AppDbContext db)
    {
        _processingService = processingService;
        _db = db;
    }

    public async Task<int> ProcessFileAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);

        var batch = new List<LogEntryEntity>();
        const int batchSize = 500;
        int totalInserted = 0;

        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            var parsed = _processingService.ProcessLine(line);

            if (parsed == null)
                continue;

            var entity = MapToEntity(parsed, line);

            batch.Add(entity);

            if (batch.Count >= batchSize)
            {
                await SaveBatchAsync(batch);
                totalInserted += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await SaveBatchAsync(batch);
            totalInserted += batch.Count;
        }

        return totalInserted;
    }

    private async Task SaveBatchAsync(List<LogEntryEntity> batch)
    {
        _db.Logs.AddRange(batch);
        await _db.SaveChangesAsync();
    }

    private LogEntryEntity MapToEntity(LogEntry entry, string rawLine)
    {
        return new LogEntryEntity
        {
            Timestamp = entry.Timestamp,
            Level = entry.Level,
            Message = entry.Message,
            DeviceId = entry.DeviceId ?? string.Empty,
            Source = entry.Source ?? string.Empty,
            EventId = entry.EventId,
            RawLine = rawLine,
            Metadata = entry.Metadata.Select(m => new LogMetadataEntity
            {
                Key = m.Key,
                Value = m.Value
            }).ToList()
        };
    }
}