using System.Text.Json.Nodes;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLogAnalyzer.Functions.Configuration;
using SmartLogAnalyzer.Functions.Models;

namespace SmartLogAnalyzer.Functions.Services;

public sealed class DiagnosticExtractionProcessor
{
    private const string SummarySchemaVersion = "1.0";

    // The summary feeds an LLM, so evidence is capped to the lines closest to the disconnect.
    private const int MaxLogsBeforeDisconnection = 10;

    private readonly TableClient _table;
    private readonly AnalyzerApiClient _api;
    private readonly SummaryBlobWriter _summaryWriter;
    private readonly DiagnosticExtractionOptions _options;
    private readonly ILogger<DiagnosticExtractionProcessor> _logger;

    public DiagnosticExtractionProcessor(
        TableClient table,
        AnalyzerApiClient api,
        SummaryBlobWriter summaryWriter,
        IOptions<DiagnosticExtractionOptions> options,
        ILogger<DiagnosticExtractionProcessor> logger)
    {
        _table = table;
        _api = api;
        _summaryWriter = summaryWriter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractionRunResult> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var result = new ExtractionRunResult();
        var pending = new List<DiagnosticRecordEntity>();

        // ExtractionCompleted does not exist until this function writes it, and OData comparisons
        // against a missing property are unreliable, so that half of the filter is applied locally.
        await foreach (var entity in _table.QueryAsync<DiagnosticRecordEntity>(
            filter: "CopyCompleted eq true",
            cancellationToken: cancellationToken))
        {
            result.RowsExamined++;

            if (!IsPendingExtraction(entity))
                continue;

            pending.Add(entity);

            if (pending.Count >= _options.MaxRowsPerRun)
                break;
        }

        _logger.LogInformation(
            "Found {PendingCount} row(s) pending extraction out of {ExaminedCount} examined.",
            pending.Count,
            result.RowsExamined);

        foreach (var entity in pending)
        {
            var rowResult = await ProcessRowAsync(entity, cancellationToken);
            result.Rows.Add(rowResult);

            if (rowResult.Succeeded)
                result.RowsProcessed++;
            else
                result.RowsFailed++;
        }

        return result;
    }

    private async Task<ExtractionRowResult> ProcessRowAsync(
        DiagnosticRecordEntity entity,
        CancellationToken cancellationToken)
    {
        var rowResult = new ExtractionRowResult
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            DeviceSerialNumber = entity.DeviceSerialNumber
        };

        try
        {
            if (string.IsNullOrWhiteSpace(entity.StoragePath))
                throw new InvalidOperationException("Row has no StoragePath.");

            var deviceId = ResolveDeviceSerialNumber(entity);

            var (rawIncidents, incidents) = await _api.GetConnectionIncidentsAsync(deviceId, cancellationToken);
            rowResult.IncidentCount = incidents.Count;

            var selected = SelectIncident(incidents);
            rowResult.DisconnectedAtTimestamp = selected is null
                ? null
                : DateTime.SpecifyKind(selected.DisconnectedAt, DateTimeKind.Utc);

            if (incidents.Count > 1)
            {
                rowResult.Warnings.Add(
                    $"Device has {incidents.Count} connection incidents; only the selected one is included.");
            }

            var connectionIncident = selected is null
                ? null
                : FindRawIncident(rawIncidents, selected.IncidentKey);

            var evidence = await CollectEvidenceAsync(selected, rowResult, cancellationToken);

            JsonNode? transcript = null;
            try
            {
                transcript = await _api.GetTranscriptSummaryAsync(deviceId, cancellationToken);
                if (transcript is null)
                    rowResult.Warnings.Add("No diagnostic transcript was available for this device.");
            }
            catch (Exception ex)
            {
                rowResult.Warnings.Add($"Transcript summary call failed: {ex.Message}");
                _logger.LogWarning(ex, "Transcript summary call failed for {DeviceId}.", deviceId);
            }

            var document = BuildSummaryDocument(entity, deviceId, connectionIncident, evidence, transcript, rowResult);

            // The blob is written first so a storage failure never leaves the row marked complete.
            rowResult.SummaryBlobPath = await _summaryWriter.WriteAsync(
                entity.StoragePath!,
                document,
                cancellationToken);

            await MarkExtractedAsync(entity, rowResult.DisconnectedAtTimestamp, cancellationToken);

            rowResult.Succeeded = true;

            _logger.LogInformation(
                "Extraction complete for {DeviceId}; summary written to {BlobPath}.",
                deviceId,
                rowResult.SummaryBlobPath);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            rowResult.Error = "Row was modified concurrently; it will be retried on the next run.";
            _logger.LogWarning(ex, "Concurrency conflict updating {PartitionKey}/{RowKey}.", entity.PartitionKey, entity.RowKey);
        }
        catch (Exception ex)
        {
            rowResult.Error = ex.Message;
            _logger.LogError(ex, "Extraction failed for {PartitionKey}/{RowKey}.", entity.PartitionKey, entity.RowKey);
        }

        return rowResult;
    }

    private async Task<(string IncidentKey, JsonNode? Evidence)?> CollectEvidenceAsync(
        ConnectionIncidentSummaryDto? selected,
        ExtractionRowResult rowResult,
        CancellationToken cancellationToken)
    {
        if (selected is null)
            return null;

        try
        {
            var node = await _api.GetIncidentEvidenceAsync(selected.IncidentKey, cancellationToken);
            return (selected.IncidentKey, node);
        }
        catch (Exception ex)
        {
            rowResult.Warnings.Add($"Evidence call failed for incident {selected.IncidentKey}: {ex.Message}");
            _logger.LogWarning(ex, "Evidence call failed for incident {IncidentKey}.", selected.IncidentKey);
            return null;
        }
    }

    /// <summary>A node cannot be re-parented, so the matching incident is cloned out of the API response.</summary>
    private static JsonNode? FindRawIncident(JsonArray rawIncidents, string incidentKey)
    {
        foreach (var node in rawIncidents)
        {
            if (node?["incidentKey"]?.GetValue<string>() == incidentKey)
                return node.DeepClone();
        }

        return null;
    }

    /// <summary>
    /// Kept off the entity itself: Azure.Data.Tables persists every public property, so a computed
    /// one would be written back as a real column.
    /// </summary>
    private static bool IsPendingExtraction(DiagnosticRecordEntity entity) =>
        entity.CopyCompleted == true && entity.ExtractionCompleted != true;

    /// <summary>
    /// An unresolved outage is reported from the moment connectivity was first lost, so the
    /// earliest ongoing incident wins. Otherwise the most recent incident is used.
    /// </summary>
    private static ConnectionIncidentSummaryDto? SelectIncident(IReadOnlyList<ConnectionIncidentSummaryDto> incidents)
    {
        if (incidents.Count == 0)
            return null;

        var ongoing = incidents.Where(incident => incident.IsOngoing).ToList();

        return ongoing.Count > 0
            ? ongoing.OrderBy(incident => incident.DisconnectedAt).First()
            : incidents.OrderByDescending(incident => incident.DisconnectedAt).First();
    }

    private static string ResolveDeviceSerialNumber(DiagnosticRecordEntity entity)
    {
        // StoragePath is "{container}/{deviceSerial}/{yyyy}/{MM}/{dd}/...".
        var segments = entity.StoragePath!
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 2 && !string.IsNullOrWhiteSpace(segments[1]))
            return segments[1];

        if (!string.IsNullOrWhiteSpace(entity.DeviceSerialNumber))
            return entity.DeviceSerialNumber!;

        throw new InvalidOperationException("Unable to resolve a device serial number for the row.");
    }

    private JsonObject BuildSummaryDocument(
        DiagnosticRecordEntity entity,
        string deviceId,
        JsonNode? connectionIncident,
        (string IncidentKey, JsonNode? Evidence)? evidence,
        JsonNode? transcript,
        ExtractionRowResult rowResult)
    {
        var warnings = new JsonArray();
        foreach (var warning in rowResult.Warnings)
            warnings.Add(warning);

        return new JsonObject
        {
            ["schemaVersion"] = SummarySchemaVersion,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["device"] = new JsonObject
            {
                ["serialNumber"] = deviceId,
                ["guid"] = entity.Guid,
                ["partitionKey"] = entity.PartitionKey,
                ["rowKey"] = entity.RowKey,
                ["storagePath"] = entity.StoragePath
            },
            ["connectionIncident"] = connectionIncident,
            ["incidentEvidence"] = evidence is null
                ? null
                : new JsonObject
                {
                    ["incidentKey"] = evidence.Value.IncidentKey,
                    ["evidence"] = TrimEvidence(evidence.Value.Evidence)
                },
            ["transcriptSummary"] = transcript,
            ["warnings"] = warnings
        };
    }

    /// <summary>Heartbeats dominate the payload and add no diagnostic value beyond the summary counts.</summary>
    private static JsonNode? TrimEvidence(JsonNode? evidence)
    {
        if (evidence is not JsonObject node)
            return evidence;

        node.Remove("heartbeatsDuringDisconnection");

        if (node["logsBeforeDisconnection"] is JsonArray logs)
        {
            while (logs.Count > MaxLogsBeforeDisconnection)
                logs.RemoveAt(logs.Count - 1);
        }

        return node;
    }

    private async Task MarkExtractedAsync(
        DiagnosticRecordEntity entity,
        DateTime? disconnectedAt,
        CancellationToken cancellationToken)
    {
        entity.DisconnectedAtTimestamp = disconnectedAt;
        entity.ExtractedIotLogs = true;
        entity.ExtractionCompleted = true;

        await _table.UpdateEntityAsync(
            entity,
            entity.ETag,
            TableUpdateMode.Merge,
            cancellationToken);
    }
}
