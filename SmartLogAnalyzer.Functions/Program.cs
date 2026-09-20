using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartLogAnalyzer.Functions.Configuration;
using SmartLogAnalyzer.Functions.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddOptions<DiagnosticExtractionOptions>()
    .Bind(builder.Configuration.GetSection(DiagnosticExtractionOptions.SectionName))
    .Validate(
        options => options.IsConfigured,
        "DiagnosticExtraction requires AnalyzerApiBaseUrl, TableConnectionString and LogsBlobServiceUri.")
    .ValidateOnStart();

builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<DiagnosticExtractionOptions>>().Value;
    return new TableClient(options.TableConnectionString, options.TableName);
});

builder.Services.AddHttpClient<AnalyzerApiClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<DiagnosticExtractionOptions>>().Value;
    httpClient.BaseAddress = new Uri($"{options.AnalyzerApiBaseUrl.TrimEnd('/')}/");
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(30, options.ApiTimeoutSeconds));
});

builder.Services.AddSingleton<SummaryBlobWriter>();

// Scoped, not singleton: it depends on a typed HttpClient whose handler is rotated by the factory.
builder.Services.AddScoped<DiagnosticExtractionProcessor>();

builder.Build().Run();
