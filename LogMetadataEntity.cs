public class LogMetadataEntity
{
    public long Id { get; set; }
    public long LogEntryId { get; set; }
    public LogEntryEntity LogEntry { get; set; } = null!;
    public required string Key { get; set; } = null!;
    public required string Value { get; set; } = null!;
}