using Microsoft.EntityFrameworkCore;
using SmartLogAnalyzer.Models;

public class ConnectionAnalysisService
{
    private const int ContextLineCount = 40;
    private const int MaxIncidentCount = 500;
    private const string UnprovisionedDeviceId = "DeviceNotProvisioned";
    private readonly AppDbContext _db;
    private readonly MessageNormalizer _normalizer;

    public ConnectionAnalysisService(AppDbContext db, MessageNormalizer normalizer)
    {
        _db = db;
        _normalizer = normalizer;
    }

    public async Task<List<ConnectionIncident>> GetIncidentsAsync(ConnectionIncidentFilter filter)
    {
        var logs = await GetIncidentLogsAsync(filter);
        var incidents = new List<ConnectionIncident>();

        foreach (var (deviceId, deviceLogs) in BuildDeviceTimelines(logs, filter.DeviceId))
        {
            var orderedLogs = deviceLogs.OrderBy(log => log.Timestamp).ThenBy(log => log.Id).ToList();
            var heartbeatExecutions = BuildHeartbeatExecutions(orderedLogs, deviceId);
            ConnectionIncident? activeIncident = null;

            for (var index = 0; index < orderedLogs.Count; index++)
            {
                var log = orderedLogs[index];

                if (IsCloudDisconnection(log) && activeIncident is null)
                {
                    activeIncident = new ConnectionIncident
                    {
                        DeviceId = deviceId,
                        DisconnectedAt = log.Timestamp,
                        ObservedUntil = log.Timestamp,
                        IsOngoing = true,
                        DisconnectEvent = ToContext(log),
                        LogsBeforeDisconnection = orderedLogs
                            .Take(index)
                            .TakeLast(ContextLineCount)
                            .Select(ToContext)
                            .ToList(),
                        ErrorsBeforeDisconnection = orderedLogs
                            .Take(index)
                            .TakeLast(ContextLineCount)
                            .Where(IsError)
                            .Select(ToContext)
                            .ToList()
                    };
                    continue;
                }

                if (activeIncident is null)
                    continue;

                activeIncident.ObservedUntil = log.Timestamp;

                if (IsCloudReconnection(log))
                {
                    activeIncident.ReconnectedAt = log.Timestamp;
                    activeIncident.RecoveryEvent = ToContext(log);
                    activeIncident.IsOngoing = false;
                    Complete(activeIncident);
                    incidents.Add(activeIncident);
                    activeIncident = null;
                }
            }

            if (activeIncident is not null)
            {
                // For an open incident, duration is measured only through the latest
                // observed log, never against the current clock for historical uploads.
                Complete(activeIncident);
                incidents.Add(activeIncident);
            }

            foreach (var incident in incidents.Where(incident => incident.DeviceId == deviceId))
            {
                incident.HeartbeatsDuringDisconnection.AddRange(heartbeatExecutions.Where(heartbeat =>
                    heartbeat.StartedAt >= incident.DisconnectedAt &&
                    heartbeat.StartedAt <= incident.ObservedUntil));
            }
        }

        return incidents
            .OrderByDescending(incident => incident.DisconnectedAt)
            .Take(Math.Clamp(filter.Limit, 1, MaxIncidentCount))
            .ToList();
    }

    public async Task<List<ConnectionIncidentSummary>> GetIncidentSummariesAsync(ConnectionIncidentFilter filter)
    {
        var incidents = await GetIncidentsAsync(filter);
        return incidents.Select(ToSummary).ToList();
    }

    public async Task<ConnectionIncident?> GetIncidentEvidenceAsync(string incidentKey)
    {
        if (!TryParseIncidentKey(incidentKey, out var deviceId, out var disconnectedAt))
            return null;

        var incidents = await GetIncidentsAsync(new ConnectionIncidentFilter
        {
            DeviceId = deviceId,
            Limit = MaxIncidentCount
        });

        return incidents.SingleOrDefault(incident =>
            incident.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) &&
            incident.DisconnectedAt.Ticks == disconnectedAt.Ticks);
    }

    public async Task<List<HeartbeatExecution>> GetHeartbeatsAsync(HeartbeatFilter filter)
    {
        var logs = await GetHeartbeatLogsAsync(filter);

        var heartbeats = logs.GroupBy(log => log.DeviceId)
            .SelectMany(deviceLogs => BuildHeartbeatExecutions(
                deviceLogs.OrderBy(log => log.Timestamp).ThenBy(log => log.Id).ToList(),
                deviceLogs.Key))
            .OrderByDescending(heartbeat => heartbeat.StartedAt);

        var filteredHeartbeats = filter.HasCloudConnectionFailure.HasValue
        ? heartbeats.Where(heartbeat =>
        heartbeat.HasCloudConnectionFailure == filter.HasCloudConnectionFailure.Value)
        : heartbeats;

        return filteredHeartbeats
            .Take(Math.Clamp(filter.Limit, 1, 500))
            .ToList();
    }

    private async Task<List<LogEntryEntity>> GetFilteredLogsAsync(
        LogQueryFilter filter,
        bool includeUnprovisionedContext)
    {
        var query = filter.ApplyTo(_db.Logs.AsNoTracking());

        if (includeUnprovisionedContext && !string.IsNullOrWhiteSpace(filter.DeviceId))
        {
            query = filter.ApplyNonDeviceFilters(_db.Logs.AsNoTracking())
                .Where(log =>
                    log.DeviceId.Contains(filter.DeviceId) ||
                    log.DeviceId == UnprovisionedDeviceId);
        }

        return await query.ToListAsync();
    }

    private async Task<List<LogEntryEntity>> GetIncidentLogsAsync(
        ConnectionIncidentFilter filter)
    {
        var query = _db.Logs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.DeviceId))
        {
            query = query.Where(log =>
                log.DeviceId.Contains(filter.DeviceId) ||
                log.DeviceId == UnprovisionedDeviceId);
        }

        return await query.ToListAsync();
    }

    private async Task<List<LogEntryEntity>> GetHeartbeatLogsAsync(
        HeartbeatFilter filter)
    {
        var query = _db.Logs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.DeviceId))
        {
            query = query.Where(log =>
                log.DeviceId.Contains(filter.DeviceId));
        }

        return await query.ToListAsync();
    }    

    private static List<(string DeviceId, List<LogEntryEntity> Logs)> BuildDeviceTimelines(
        List<LogEntryEntity> logs,
        string? requestedDeviceId)
    {
        if (string.IsNullOrWhiteSpace(requestedDeviceId))
        {
            return logs
                .Where(log => !IsUnprovisioned(log))
                .GroupBy(log => log.DeviceId)
                .Select(group => (group.Key, group.ToList()))
                .ToList();
        }

        var deviceIds = logs
            .Where(log => !IsUnprovisioned(log))
            .Select(log => log.DeviceId)
            .Where(deviceId => deviceId.Contains(requestedDeviceId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var timelines = deviceIds.ToDictionary(
            deviceId => deviceId,
            _ => new List<LogEntryEntity>(),
            StringComparer.OrdinalIgnoreCase);

        string? activeDeviceId = null;

        foreach (var log in logs.OrderBy(log => log.Timestamp).ThenBy(log => log.Id))
        {
            var referencedDeviceId = deviceIds.FirstOrDefault(deviceId =>
                ReferencesDevice(log, deviceId));

            if (referencedDeviceId is not null)
            {
                activeDeviceId = referencedDeviceId;
            }

            if (IsUnprovisioned(log))
            {
                if (activeDeviceId is not null)
                    timelines[activeDeviceId].Add(log);

                continue;
            }

            if (referencedDeviceId is not null)
                timelines[referencedDeviceId].Add(log);
        }

        return timelines
            .Where(timeline => timeline.Value.Count > 0)
            .Select(timeline => (timeline.Key, timeline.Value))
            .ToList();
    }

    private static bool ReferencesDevice(LogEntryEntity log, string deviceId) =>
        log.DeviceId.Contains(deviceId, StringComparison.OrdinalIgnoreCase) ||
        log.Message.Contains(deviceId, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnprovisioned(LogEntryEntity log) =>
        string.Equals(log.DeviceId, UnprovisionedDeviceId, StringComparison.OrdinalIgnoreCase);

    private static void Complete(ConnectionIncident incident)
    {
        incident.DurationHours = Math.Round(
            Math.Max(0, (incident.ObservedUntil - incident.DisconnectedAt).TotalHours), 2);
        incident.IsProlongedOver12Hours = incident.DurationHours >= 12;
        incident.IsProlongedOver24Hours = incident.DurationHours >= 24;
    }

    private static bool IsError(LogEntryEntity log) =>
        string.Equals(log.Level, "Error", StringComparison.OrdinalIgnoreCase);

    private static List<HeartbeatExecution> BuildHeartbeatExecutions(
        List<LogEntryEntity> logs,
        string deviceId)
    {
        var heartbeats = new List<HeartbeatExecution>();
        HeartbeatExecution? activeHeartbeat = null;

        foreach (var log in logs)
        {
            if (IsHeartbeatStarted(log))
            {
                if (activeHeartbeat is not null)
                    heartbeats.Add(activeHeartbeat);

                activeHeartbeat = new HeartbeatExecution
                {
                    DeviceId = deviceId,
                    StartedAt = log.Timestamp
                };
            }

            if (activeHeartbeat is null)
                continue;

            var context = ToContext(log);
            activeHeartbeat.Events.Add(context);

            if (IsError(log))
                activeHeartbeat.Errors.Add(context);

            if (IsCloudDisconnection(log))
                activeHeartbeat.HasCloudConnectionFailure = true;

            if (IsHeartbeatCompleted(log))
            {
                activeHeartbeat.CompletedAt = log.Timestamp;
                activeHeartbeat.IsCompleted = true;
                heartbeats.Add(activeHeartbeat);
                activeHeartbeat = null;
            }
        }

        if (activeHeartbeat is not null)
            heartbeats.Add(activeHeartbeat);

        return heartbeats;
    }

    private static bool IsHeartbeatStarted(LogEntryEntity log) =>
        log.EventId == 901 ||
        log.Message.Contains("heartbeat job started", StringComparison.OrdinalIgnoreCase);

    private static bool IsHeartbeatCompleted(LogEntryEntity log) =>
        log.EventId == 903 ||
        log.Message.Contains("heartbeat job completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsCloudDisconnection(LogEntryEntity log) =>
        log.Message.Contains("not connected to cloud", StringComparison.OrdinalIgnoreCase) ||
        log.Message.Contains("current status: disconnected", StringComparison.OrdinalIgnoreCase) ||
        log.Message.Contains("device went into disconnected state", StringComparison.OrdinalIgnoreCase) ||
        log.Message.Contains("connection lost", StringComparison.OrdinalIgnoreCase);

    private static bool IsCloudReconnection(LogEntryEntity log) =>
        log.Message.Contains("current status: connected", StringComparison.OrdinalIgnoreCase) ||
        log.Message.Contains("is sent to iot hub successfully", StringComparison.OrdinalIgnoreCase) ||
        log.Message.Contains("data message send to iot hub successfully", StringComparison.OrdinalIgnoreCase);

    private static ConnectionLogContext ToContext(LogEntryEntity log) => new()
    {
        Id = log.Id,
        Timestamp = log.Timestamp,
        Level = log.Level,
        Message = log.Message,
        Source = log.Source,
        EventId = log.EventId
    };

    private ConnectionIncidentSummary ToSummary(ConnectionIncident incident)
    {
        var failedHeartbeats = incident.HeartbeatsDuringDisconnection
            .Where(heartbeat => heartbeat.HasCloudConnectionFailure)
            .ToList();

        return new ConnectionIncidentSummary
        {
            IncidentKey = CreateIncidentKey(incident),
            DeviceId = incident.DeviceId,
            DisconnectedAt = incident.DisconnectedAt,
            ReconnectedAt = incident.ReconnectedAt,
            DurationMinutes = Math.Round(incident.DurationHours * 60, 1),
            Status = incident.IsOngoing ? "Ongoing" : "Resolved",
            Severity = GetSeverity(incident),
            IsProlongedOver12Hours = incident.IsProlongedOver12Hours,
            IsProlongedOver24Hours = incident.IsProlongedOver24Hours,
            DisconnectReason = incident.DisconnectEvent?.Message ?? "Cloud connection unavailable.",
            RecoverySignal = incident.RecoveryEvent?.Message,
            HeartbeatSummary = new HeartbeatSummary
            {
                TotalRuns = incident.HeartbeatsDuringDisconnection.Count,
                FailedRuns = failedHeartbeats.Count,
                LastFailureAt = failedHeartbeats.LastOrDefault()?.StartedAt
            },
            PrecedingErrors = incident.ErrorsBeforeDisconnection
                .GroupBy(error => _normalizer.Normalize(error.Message))
                .Select(group => new ConnectionErrorSummary
                {
                    Pattern = group.Key,
                    Count = group.Count(),
                    LastSeenAt = group.Max(error => error.Timestamp)
                })
                .OrderByDescending(error => error.Count)
                .ThenByDescending(error => error.LastSeenAt)
                .Take(3)
                .ToList(),
            Evidence = new ConnectionIncidentEvidenceReference
            {
                DisconnectEventId = incident.DisconnectEvent?.EventId,
                RecoveryEventId = incident.RecoveryEvent?.EventId
            }
        };
    }

    private static string GetSeverity(ConnectionIncident incident)
    {
        if (incident.IsProlongedOver24Hours)
            return "Critical";
        if (incident.IsProlongedOver12Hours)
            return "High";
        if (incident.IsOngoing)
            return "Medium";

        return "Low";
    }

    private static string CreateIncidentKey(ConnectionIncident incident) =>
        $"{Uri.EscapeDataString(incident.DeviceId)}~{incident.DisconnectedAt.Ticks}";

    private static bool TryParseIncidentKey(
        string incidentKey,
        out string deviceId,
        out DateTime disconnectedAt)
    {
        deviceId = string.Empty;
        disconnectedAt = default;

        var separator = incidentKey.LastIndexOf('~');
        if (separator <= 0 || !long.TryParse(incidentKey[(separator + 1)..], out var ticks))
            return false;

        try
        {
            deviceId = Uri.UnescapeDataString(incidentKey[..separator]);
            disconnectedAt = new DateTime(ticks);
            return !string.IsNullOrWhiteSpace(deviceId);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
