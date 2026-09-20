namespace SmartLogAnalyzer.Functions.Models;

/// <summary>
/// The subset of the connection-incident summary this function needs to reason about. The full
/// payload is passed through to the summary file untouched.
/// </summary>
public sealed class ConnectionIncidentSummaryDto
{
    public string IncidentKey { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public DateTime DisconnectedAt { get; set; }

    public DateTime? ReconnectedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public double DurationMinutes { get; set; }

    public bool IsOngoing => Status.Equals("Ongoing", StringComparison.OrdinalIgnoreCase);
}
