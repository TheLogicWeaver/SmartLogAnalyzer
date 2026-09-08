namespace SmartLogAnalyzer.Models
{
    public class LogQueryFilter
    {
        public string? Level { get; set; }

        public string? DeviceId { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public int Top { get; set; } = 10;

        public int Limit { get; set; } = 100;

        public IQueryable<LogEntryEntity> ApplyTo(IQueryable<LogEntryEntity> query)
        {
            query = ApplyNonDeviceFilters(query);

            if (!string.IsNullOrWhiteSpace(DeviceId))
                query = query.Where(log => log.DeviceId == DeviceId || log.DeviceId.Contains(DeviceId));

            return query;
        }

        public IQueryable<LogEntryEntity> ApplyNonDeviceFilters(IQueryable<LogEntryEntity> query)
        {
            if (!string.IsNullOrWhiteSpace(Level))
            {
                query = query.Where(log =>
                    log.Level.ToLower() == Level.ToLower());
            }

            if (StartTime.HasValue)
                query = query.Where(log => log.Timestamp >= StartTime.Value);

            if (EndTime.HasValue)
                query = query.Where(log => log.Timestamp <= EndTime.Value);

            return query;
        }
    }
}