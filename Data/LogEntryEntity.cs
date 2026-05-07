public class LogEntryEntity
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public required string Level { get; set; } = null!;
    public required string Message { get; set; } = null!;
    public required string DeviceId { get; set; } = null!;
    public required string Source { get; set; } = null!;
    public int? EventId { get; set; }
    public required string RawLine { get; set; } = null!;
    public ICollection<LogMetadataEntity> Metadata { get; set; } = new List<LogMetadataEntity>();
}