public class LogAnomalyAnalysisService
{
    private readonly IAnomalyDetector _detector;
    private readonly AnomalyInterpreter _interpreter;

    public LogAnomalyAnalysisService(
        IAnomalyDetector detector,
        AnomalyInterpreter interpreter)
    {
        _detector = detector;
        _interpreter = interpreter;
    }

    public async Task<List<LogAnomaly>> AnalyzeAsync(
        string level)
    {
        var detections = await _detector.DetectAsync(
            level,
            lookbackHours: 48,
            currentWindowMinutes: 60);

        return _interpreter.Interpret(detections);
    }
}