using System.Text.RegularExpressions;

public class DiagnosticTranscriptParser : ILogParser
{
    private static readonly Regex TimeRegex = new Regex(@"^(?<time>\d{1,2}:\d{2}:\d{2})", RegexOptions.Compiled);
    private static readonly Regex SerialRegex = new Regex("ClientSerialNumber\"\\s*:\\s*\"(?<serial>[^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private string? _currentDeviceId;

    public bool CanParse(string line)
    {
        // This parser is a fallback for lines not handled by other parsers.
        // It will be consulted after other parsers; return true for non-empty lines.
        return !string.IsNullOrWhiteSpace(line);
    }

    public LogEntry? Parse(string line)
    {
        var timestamp = DateTime.UtcNow;

        var m = TimeRegex.Match(line);
        if (m.Success && TimeSpan.TryParse(m.Groups["time"].Value, out var _))
        {
            timestamp = DateTime.UtcNow; // transcript timestamps are not absolute; keep UtcNow
        }

        var sm = SerialRegex.Match(line);
        if (sm.Success)
        {
            _currentDeviceId = sm.Groups["serial"].Value;
        }

        return new LogEntry
        {
            Timestamp = timestamp,
            DeviceId = _currentDeviceId ?? string.Empty,
            Source = "DiagnosticTranscript",
            EventId = 0,
            Level = "Info",
            Message = line
        };
    }
}
