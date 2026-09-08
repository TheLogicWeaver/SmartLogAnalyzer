using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=logs.db"));
builder.Services.AddScoped<LogParserFactory>();
builder.Services.AddScoped<LogProcessingService>();
builder.Services.AddScoped<LogIngestionService>();
builder.Services.AddScoped<LogInsightsService>();
builder.Services.AddSingleton<MessageNormalizer>();
builder.Services.AddScoped<IAnomalyDetector, SpikeAnomalyDetector>();
builder.Services.AddScoped<AnomalyInterpreter>();
builder.Services.AddScoped<LogAnomalyAnalysisService>();
builder.Services.AddScoped<ConnectionAnalysisService>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapPost("/upload-log", async (IFormFile file, LogIngestionService ingestionService) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded");

    using var stream = file.OpenReadStream();

    var count = await ingestionService.ProcessFileAsync(stream);

    return Results.Ok(new { Inserted = count });
})
.DisableAntiforgery();

app.MapGet("/api/insights/summary",
    async (LogInsightsService service) =>
{
    return await service.GetSummaryAsync();
})
.DisableAntiforgery();

app.MapGet("/api/insights/top-messages",
    async (
        [AsParameters] LogQueryFilter filter,
        LogInsightsService service) =>
{
    return await service.GetTopMessagesAsync(filter);
})
.DisableAntiforgery();

app.MapGet("/api/insights/logs",
    async (
        [AsParameters] LogQueryFilter filter,
        LogInsightsService service) =>
{
    return await service.GetLogsAsync(filter);
})
.DisableAntiforgery();

app.MapGet("/api/insights/smart-groups",
    async (
        [AsParameters] LogQueryFilter filter,
        LogInsightsService service) =>
{
    return await service.GetSmartGroupsAsync(filter);
})
.DisableAntiforgery();

app.MapGet("/insights/anomalies", async (
    string level,
    LogAnomalyAnalysisService service) =>
{
    return await service.AnalyzeAsync(level);
})
.DisableAntiforgery();

app.MapGet("/api/insights/connection-incidents", async (
    [AsParameters] LogQueryFilter filter,
    ConnectionAnalysisService service) =>
{
    return await service.GetIncidentsAsync(filter);
})
.DisableAntiforgery();

app.MapGet("/api/insights/heartbeats", async (
    [AsParameters] LogQueryFilter filter,
    ConnectionAnalysisService service) =>
{
    return await service.GetHeartbeatsAsync(filter);
})
.DisableAntiforgery();

app.Run();
