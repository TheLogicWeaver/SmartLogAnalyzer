using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SmartLogAnalyzer.Functions.Services;

namespace SmartLogAnalyzer.Functions.Functions;

/// <summary>
/// Azure Table Storage has no change feed, so pending rows are polled rather than pushed.
/// </summary>
public sealed class DiagnosticExtractionTimerFunction
{
    private readonly DiagnosticExtractionProcessor _processor;
    private readonly ILogger<DiagnosticExtractionTimerFunction> _logger;

    public DiagnosticExtractionTimerFunction(
        DiagnosticExtractionProcessor processor,
        ILogger<DiagnosticExtractionTimerFunction> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    [Function(nameof(DiagnosticExtractionTimerFunction))]
    public async Task RunAsync(
        [TimerTrigger("%ExtractionSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var result = await _processor.ProcessPendingAsync(cancellationToken);

        _logger.LogInformation(
            "Extraction run finished. Examined {Examined}, processed {Processed}, failed {Failed}.",
            result.RowsExamined,
            result.RowsProcessed,
            result.RowsFailed);
    }
}
