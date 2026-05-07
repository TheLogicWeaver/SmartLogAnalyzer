public class LogAnomaly
{
    public required string Pattern { get; set; }

    public required string Level { get; set; }

    public required string Severity { get; set; }

    public required string Summary { get; set; }

    public int CurrentCount { get; set; }

    public double BaselineAverage { get; set; }

    public double IncreasePercentage { get; set; }

    public int AffectedDevices { get; set; }

    public DateTime FirstOccurrence { get; set; }

    public DateTime LastOccurrence { get; set; }
}