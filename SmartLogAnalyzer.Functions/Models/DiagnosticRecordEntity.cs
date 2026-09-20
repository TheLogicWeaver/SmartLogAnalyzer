using Azure;
using Azure.Data.Tables;

namespace SmartLogAnalyzer.Functions.Models;

/// <summary>
/// A row written by the log upload tool. The table is schemaless, so the extraction columns are
/// nullable and only materialise once this function has run.
/// </summary>
public sealed class DiagnosticRecordEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public bool? CopyCompleted { get; set; }

    public string? DeviceSerialNumber { get; set; }

    public string? Guid { get; set; }

    public string? StoragePath { get; set; }

    public DateTime? DisconnectedAtTimestamp { get; set; }

    public bool? ExtractedIotLogs { get; set; }

    public bool? ExtractionCompleted { get; set; }
}
