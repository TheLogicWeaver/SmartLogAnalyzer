public interface IAnomalyDetector
{
    Task<List<AnomalyDetectionResult>> DetectAsync(
        string level,
        int lookbackHours,
        int currentWindowMinutes);
}