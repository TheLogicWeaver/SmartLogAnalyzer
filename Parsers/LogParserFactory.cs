public class LogParserFactory
{
    private readonly List<ILogParser> _parsers;

    public LogParserFactory()
    {
        _parsers = new List<ILogParser>
        {
            new IotGatewayLogParser()
        };
    }

    public ILogParser? GetParser(string line)
    {
        return _parsers.FirstOrDefault(p => p.CanParse(line));
    }
}