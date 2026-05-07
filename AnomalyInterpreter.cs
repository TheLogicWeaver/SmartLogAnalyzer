public class AnomalyInterpreter
{
    public List<LogAnomaly> Interpret(
        List<AnomalyDetectionResult> results)
    {
        return results.Select(result =>
        {
            var increasePercentage =
                CalculateIncreasePercentage(
                    result.CurrentCount,
                    result.HistoricalAverage);

            return new LogAnomaly
            {
                Pattern = result.Pattern,
                Level = result.Level,
                Severity = GetSeverity(result.SpikeFactor),
                Summary = BuildSummary(
                    result.Pattern,
                    increasePercentage,
                    result.AffectedDevices),
                CurrentCount = result.CurrentCount,
                BaselineAverage = result.HistoricalAverage,
                IncreasePercentage = increasePercentage,
                AffectedDevices = result.AffectedDevices,
                FirstOccurrence = result.FirstOccurrence,
                LastOccurrence = result.LastOccurrence
            };
        })
        .ToList();
    }

    private double CalculateIncreasePercentage(
        int currentCount,
        double average)
    {
        if (average <= 0)
            return 0;

        return ((currentCount - average) / average) * 100;
    }

    private string GetSeverity(double spikeFactor)
    {
        if (spikeFactor >= 10)
            return "Critical";

        if (spikeFactor >= 5)
            return "High";

        if (spikeFactor >= 3)
            return "Medium";

        return "Low";
    }

    private string BuildSummary(
        string pattern,
        double increasePercentage,
        int affectedDevices)
    {
        return
            $"Issue frequency increased by {Math.Round(increasePercentage, 2)}% " +
            $"affecting {affectedDevices} device(s).";
    }
}