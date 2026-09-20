namespace SmartLogAnalyzer.Storage;

/// <summary>
/// Coordinates used to prune which files have to be fetched from the lake.
/// </summary>
public sealed record LogScope(
    string? DeviceId = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null)
{
    public static readonly LogScope All = new();
}

public sealed record TranscriptDocument(
    string Path,
    string DeviceId,
    DateOnly LogDate,
    IReadOnlyList<string> Lines);

public interface ILogStore
{
    Task<IReadOnlyList<LogRecord>> GetGatewayLogsAsync(
        LogScope scope,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TranscriptDocument>> GetTranscriptsAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDeviceIdsAsync(CancellationToken cancellationToken = default);

    Task<int> WarmAsync(int recentDays, CancellationToken cancellationToken = default);
}
