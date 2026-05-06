public interface ILogParser
{
    bool CanParse(string line);
    LogEntry? Parse(string line);
}