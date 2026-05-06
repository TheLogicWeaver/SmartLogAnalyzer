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

app.MapGet("/insights/logs-by-level", async (LogInsightsService service, string level) =>
{
    return await service.GetLogsByLevelAsync(level);
})
.DisableAntiforgery();

app.Run();