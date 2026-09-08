public class ConnectionIncidentSummary
{
    public required string IncidentKey { get; init; }
    public required string DeviceId { get; init; }
    public DateTime DisconnectedAt { get; init; }
    public DateTime? ReconnectedAt { get; init; }
    public double DurationMinutes { get; init; }
    public required string Status { get; init; }
    public required string Severity { get; init; }
    public bool IsProlongedOver12Hours { get; init; }
    public bool IsProlongedOver24Hours { get; init; }
    public required string DisconnectReason { get; init; }
    public string? RecoverySignal { get; init; }
    public required HeartbeatSummary HeartbeatSummary { get; init; }
    public List<ConnectionErrorSummary> PrecedingErrors { get; init; } = [];
    public required ConnectionIncidentEvidenceReference Evidence { get; init; }
}

public class HeartbeatSummary
{
    public int TotalRuns { get; init; }
    public int FailedRuns { get; init; }
    public DateTime? LastFailureAt { get; init; }
}

public class ConnectionErrorSummary
{
    public required string Pattern { get; init; }
    public int Count { get; init; }
    public DateTime LastSeenAt { get; init; }
}

public class ConnectionIncidentEvidenceReference
{
    public int? DisconnectEventId { get; init; }
    public int? RecoveryEventId { get; init; }
}
