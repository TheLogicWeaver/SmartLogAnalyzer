using System.Text.RegularExpressions;

public class IotGatewayLogParser : ILogParser
{
    private static readonly Regex LogRegex = new Regex(
        @"== (?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[(?<device>[^\]]+)\] \[(?<source>[^\]]+)\] \[(?<eventId>\d+)\] \[(?<level>[^\]]+)\] (?<message>.*)",
        RegexOptions.Compiled);

    public bool CanParse(string line)
    {
        return line.StartsWith("== ");
    }

    public LogEntry? Parse(string line)
    {
        var match = LogRegex.Match(line);

        if (!match.Success)
            return null;

        return new LogEntry
        {
            Timestamp = DateTime.Parse(match.Groups["timestamp"].Value),
            DeviceId = match.Groups["device"].Value,
            Source = match.Groups["source"].Value,
            EventId = int.Parse(match.Groups["eventId"].Value),
            Level = match.Groups["level"].Value,
            Message = match.Groups["message"].Value
        };
    }
}