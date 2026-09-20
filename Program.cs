using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using SmartLogAnalyzer.Models;
using SmartLogAnalyzer.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

builder.Services.AddOptions<LogStorageOptions>()
    .Bind(builder.Configuration.GetSection(LogStorageOptions.SectionName))
    .Validate(options => options.IsConfigured, "LogStorage:ServiceUri and LogStorage:Container must be configured.")
    .ValidateOnStart();

builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<LogStorageOptions>>().Value;
    var sasToken = options.SasToken.TrimStart('?');

    var uri = string.IsNullOrWhiteSpace(sasToken)
        ? new Uri($"{options.ServiceUri.TrimEnd('/')}/{options.Container}")
        : new Uri($"{options.ServiceUri.TrimEnd('/')}/{options.Container}?{sasToken}");

    return new BlobContainerClient(uri);
});

builder.Services.AddSingleton<BlobLogCatalog>();
builder.Services.AddSingleton<ILogStore, BlobLogStore>();
builder.Services.AddHostedService<LogCacheWarmupService>();

builder.Services.AddSingleton<MessageNormalizer>();
builder.Services.AddScoped<LogInsightsService>();
builder.Services.AddScoped<DiagnosticTranscriptService>();
builder.Services.AddScoped<IAnomalyDetector, SpikeAnomalyDetector>();
builder.Services.AddScoped<AnomalyInterpreter>();
builder.Services.AddScoped<LogAnomalyAnalysisService>();
builder.Services.AddScoped<ConnectionAnalysisService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/api/devices", async (ILogStore store, CancellationToken cancellationToken) =>
{
    return await store.GetDeviceIdsAsync(cancellationToken);
});

app.MapGet("/api/insights/summary",
    async (
        [AsParameters] LogQueryFilter filter,
        LogInsightsService service,
        CancellationToken cancellationToken) =>
{
    return await service.GetSummaryAsync(filter, cancellationToken);
});

app.MapGet("/api/insights/top-messages",
    async (
        [AsParameters] LogQueryFilter filter,
        LogInsightsService service,
        CancellationToken cancellationToken) =>
{
    return await service.GetTopMessagesAsync(filter, cancellationToken);
});

app.MapGet("/api/insights/logs",
    async (
        [AsParameters] LogQueryFilter filter,
        LogInsightsService service,
        CancellationToken cancellationToken) =>
{
    return await service.GetLogsAsync(filter, cancellationToken);
});

app.MapGet("/api/insights/smart-groups",
    async (
        [AsParameters] LogQueryFilter filter,
        LogInsightsService service,
        CancellationToken cancellationToken) =>
{
    return await service.GetSmartGroupsAsync(filter, cancellationToken);
});

app.MapGet("/insights/anomalies", async (
    string level,
    LogAnomalyAnalysisService service) =>
{
    return await service.AnalyzeAsync(level);
});

app.MapGet("/api/insights/connection-incidents", async (
    [AsParameters] ConnectionIncidentFilter filter,
    ConnectionAnalysisService service) =>
{
    return await service.GetIncidentSummariesAsync(filter);
});

app.MapGet("/api/insights/connection-incidents/{incidentKey}/evidence", async (
    string incidentKey,
    ConnectionAnalysisService service) =>
{
    var incident = await service.GetIncidentEvidenceAsync(incidentKey);
    return incident is null ? Results.NotFound() : Results.Ok(incident);
});

app.MapGet("/api/insights/heartbeats", async (
    [AsParameters] HeartbeatFilter filter,
    ConnectionAnalysisService service) =>
{
    return await service.GetHeartbeatsAsync(filter);
});

app.MapGet("/api/diagnostics/transcript-summary", async (
    string? deviceId,
    DiagnosticTranscriptService service,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(deviceId))
        return Results.BadRequest("Missing required 'deviceId' query parameter.");

    var summary = await service.AnalyzeAsync(deviceId, cancellationToken);

    return summary == null ? Results.NotFound() : Results.Ok(summary);
});

app.Run();