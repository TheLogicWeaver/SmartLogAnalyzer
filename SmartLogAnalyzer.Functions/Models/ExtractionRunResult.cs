namespace SmartLogAnalyzer.Functions.Models;

public sealed class ExtractionRunResult
{
    public int RowsExamined { get; set; }

    public int RowsProcessed { get; set; }

    public int RowsFailed { get; set; }

    public List<ExtractionRowResult> Rows { get; } = [];
}

public sealed class ExtractionRowResult
{
    public required string PartitionKey { get; init; }

    public required string RowKey { get; init; }

    public string? DeviceSerialNumber { get; init; }

    public bool Succeeded { get; set; }

    public string? SummaryBlobPath { get; set; }

    public DateTime? DisconnectedAtTimestamp { get; set; }

    public int IncidentCount { get; set; }

    public string? Error { get; set; }

    public List<string> Warnings { get; } = [];
}
