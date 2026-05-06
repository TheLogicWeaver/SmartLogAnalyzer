public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public required string DeviceId { get; set; }
    public required string Source { get; set; }
    public int EventId { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}