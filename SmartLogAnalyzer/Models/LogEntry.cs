public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string? DeviceId { get; set; }
    public string? Source { get; set; }
    public int EventId { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}