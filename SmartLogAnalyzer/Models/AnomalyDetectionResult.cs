public class AnomalyDetectionResult
{
    public required string Pattern { get; set; }

    public required string Level { get; set; }

    public int CurrentCount { get; set; }

    public double HistoricalAverage { get; set; }

    public double SpikeFactor { get; set; }

    public int AffectedDevices { get; set; }

    public DateTime FirstOccurrence { get; set; }

    public DateTime LastOccurrence { get; set; }
}