namespace SmartLogAnalyzer.Functions.Configuration;

public sealed class DiagnosticExtractionOptions
{
    public const string SectionName = "DiagnosticExtraction";

    /// <summary>Base address of the SmartLogAnalyzer Web API.</summary>
    public string AnalyzerApiBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>Connection string for the storage account holding the DiagnosticRecord table.</summary>
    public string TableConnectionString { get; set; } = string.Empty;

    public string TableName { get; set; } = "DiagnosticRecord";

    /// <summary>Blob endpoint of the account the gateway logs are uploaded to.</summary>
    public string LogsBlobServiceUri { get; set; } = string.Empty;

    /// <summary>SAS token for that account. Needs create + write in addition to read + list.</summary>
    public string LogsSasToken { get; set; } = string.Empty;

    /// <summary>Folder created beneath the row's StoragePath to hold the summary.</summary>
    public string SummaryFolderName { get; set; } = "analysis";

    public string SummaryFileName { get; set; } = "summary.json";

    public int MaxRowsPerRun { get; set; } = 50;

    public int ApiTimeoutSeconds { get; set; } = 120;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AnalyzerApiBaseUrl) &&
        !string.IsNullOrWhiteSpace(TableConnectionString) &&
        !string.IsNullOrWhiteSpace(LogsBlobServiceUri);
}
