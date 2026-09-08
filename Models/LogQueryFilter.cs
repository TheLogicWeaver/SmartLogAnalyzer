public class LogQueryFilter
{
    public string? Level { get; set; }

    public string? DeviceId { get; set; }

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public int Top { get; set; } = 10;

    public int Limit { get; set; } = 100;
}