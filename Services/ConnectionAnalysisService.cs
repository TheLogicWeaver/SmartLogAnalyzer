using Microsoft.EntityFrameworkCore;

public class ConnectionAnalysisService
{
    private const int ContextLineCount = 40;
    private const string UnprovisionedDeviceId = "DeviceNotProvisioned";
    private readonly AppDbContext _db;

    public ConnectionAnalysisService(AppDbContext db) => _db = db;

    public async Task<List<ConnectionIncident>> GetIncidentsAsync(LogQueryFilter filter)
    {
        var logs = await GetFilteredLogsAsync(filter, includeUnprovisionedContext: true);
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
            .Take(Math.Clamp(filter.Limit, 1, 500))
            .ToList();
    }

    public async Task<List<HeartbeatExecution>> GetHeartbeatsAsync(LogQueryFilter filter)
    {
        var logs = await GetFilteredLogsAsync(filter, includeUnprovisionedContext: false);
        return logs.GroupBy(log => log.DeviceId)
            .SelectMany(deviceLogs => BuildHeartbeatExecutions(
                deviceLogs.OrderBy(log => log.Timestamp).ThenBy(log => log.Id).ToList(),
                deviceLogs.Key))
            .OrderByDescending(heartbeat => heartbeat.StartedAt)
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
}
