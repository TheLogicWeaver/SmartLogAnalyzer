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

app.MapGet("/insights/summary", async (LogInsightsService service) =>
{
    return await service.GetSummaryAsync();
})
.DisableAntiforgery();

app.MapGet("/insights/top-messages-by-level", async (LogInsightsService service, string level, int top) =>
{
    return await service.GetTopMessagesByLevelAsync(level, top);
})
.DisableAntiforgery();

app.MapGet("/insights/top-messages-by-level-deviceId", 
async (LogInsightsService service, string level, string deviceId, int top) =>
{
    return await service.GetTopMessagesByLevelAndDeviceIdAsync(level, deviceId, top);
})
.DisableAntiforgery();

app.MapGet("/insights/logs-by-level", async (LogInsightsService service, string level) =>
{
    return await service.GetLogsByLevelAsync(level);
})
.DisableAntiforgery();

app.MapGet("/insights/logs-by-level-deviceId", async (LogInsightsService service, string level, string deviceId) =>
{
    return await service.GetLogsByLevelDeviceIdAsync(level, deviceId);
})
.DisableAntiforgery();

app.MapGet("/insights/smart-groups", async (
    string level,
    int top,
    LogInsightsService service) =>
{
    return await service.GetSmartGroupsAsync(level, top);
})
.DisableAntiforgery();

app.MapGet("/insights/smart-groups-by-deviceId", 
async (LogInsightsService service, string level, int top, string deviceId) =>
{
    return await service.GetSmartGroupsByDeviceId(level, top, deviceId);
})
.DisableAntiforgery();

app.MapGet("/insights/anomalies", async (
    string level,
    LogAnomalyAnalysisService service) =>
{
    return await service.AnalyzeAsync(level);
})
.DisableAntiforgery();

app.Run();