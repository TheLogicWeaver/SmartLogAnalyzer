using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SmartLogAnalyzer.Functions.Models;

namespace SmartLogAnalyzer.Functions.Services;

public sealed class AnalyzerApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<AnalyzerApiClient> _logger;

    public AnalyzerApiClient(HttpClient http, ILogger<AnalyzerApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<(JsonArray Raw, List<ConnectionIncidentSummaryDto> Parsed)> GetConnectionIncidentsAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        var node = await GetJsonAsync(
            $"api/insights/connection-incidents?deviceId={Uri.EscapeDataString(deviceId)}",
            cancellationToken);

        if (node is not JsonArray array)
            return (new JsonArray(), []);

        var parsed = array.Deserialize<List<ConnectionIncidentSummaryDto>>(SerializerOptions) ?? [];
        return (array, parsed);
    }

    public Task<JsonNode?> GetIncidentEvidenceAsync(string incidentKey, CancellationToken cancellationToken)
    {
        // The key is already percent-encoded by the API, so it is used verbatim as a path segment.
        return GetJsonAsync(
            $"api/insights/connection-incidents/{incidentKey}/evidence",
            cancellationToken);
    }

    public Task<JsonNode?> GetTranscriptSummaryAsync(string deviceId, CancellationToken cancellationToken)
    {
        return GetJsonAsync(
            $"api/diagnostics/transcript-summary?deviceId={Uri.EscapeDataString(deviceId)}",
            cancellationToken);
    }

    private async Task<JsonNode?> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(relativeUrl, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Analyzer API returned 404 for {RelativeUrl}.", relativeUrl);
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
