public class ConnectionIncident
{
    public required string DeviceId { get; init; }
    public DateTime DisconnectedAt { get; init; }
    public DateTime? ReconnectedAt { get; set; }
    public DateTime ObservedUntil { get; set; }
    public double DurationHours { get; set; }
    public bool IsOngoing { get; set; }
    public bool IsProlongedOver12Hours { get; set; }
    public bool IsProlongedOver24Hours { get; set; }
    public ConnectionLogContext? DisconnectEvent { get; init; }
    public ConnectionLogContext? RecoveryEvent { get; set; }
    public List<ConnectionLogContext> LogsBeforeDisconnection { get; init; } = [];
    public List<ConnectionLogContext> ErrorsBeforeDisconnection { get; init; } = [];
    public List<HeartbeatExecution> HeartbeatsDuringDisconnection { get; } = [];
}

public class ConnectionLogContext
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public required string Source { get; init; }
    public int? EventId { get; init; }
}

public class HeartbeatExecution
{
    public required string DeviceId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public bool IsCompleted { get; set; }
    public bool HasCloudConnectionFailure { get; set; }
    public List<ConnectionLogContext> Events { get; } = [];
    public List<ConnectionLogContext> Errors { get; } = [];
}
