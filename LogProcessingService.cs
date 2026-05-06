public class LogProcessingService
{
    private readonly LogParserFactory _factory;

    public LogProcessingService(LogParserFactory factory)
    {
        _factory = factory;
    }

    public LogEntry? ProcessLine(string line)
    {
        var parser = _factory.GetParser(line);

        if (parser == null)
            return null;

        return parser.Parse(line);
    }
}