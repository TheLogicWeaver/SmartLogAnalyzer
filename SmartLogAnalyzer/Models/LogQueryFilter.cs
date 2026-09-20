using SmartLogAnalyzer.Storage;

namespace SmartLogAnalyzer.Models
{
    public class LogQueryFilter
    {
        public string? Level { get; set; }

        public string? DeviceId { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        // Nullable so [AsParameters] treats these as optional rather than required query values.
        public int? Top { get; set; }

        public int? Limit { get; set; }

        public int ResolvedTop => Top is > 0 ? Top.Value : 10;

        public int ResolvedLimit => Limit is > 0 ? Limit.Value : 100;

        public LogScope ToScope() => new(DeviceId, StartTime, EndTime);

        public IEnumerable<LogRecord> ApplyTo(IEnumerable<LogRecord> logs)
        {
            logs = ApplyNonDeviceFilters(logs);

            if (!string.IsNullOrWhiteSpace(DeviceId))
                logs = logs.Where(log => log.EffectiveDeviceId.Contains(DeviceId, StringComparison.OrdinalIgnoreCase));

            return logs;
        }

        public IEnumerable<LogRecord> ApplyNonDeviceFilters(IEnumerable<LogRecord> logs)
        {
            if (!string.IsNullOrWhiteSpace(Level))
                logs = logs.Where(log => string.Equals(log.Level, Level, StringComparison.OrdinalIgnoreCase));

            if (StartTime.HasValue)
                logs = logs.Where(log => log.Timestamp >= StartTime.Value);

            if (EndTime.HasValue)
                logs = logs.Where(log => log.Timestamp <= EndTime.Value);

            return logs;
        }
    }
}