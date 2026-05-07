public class LogIssueGroup
{
    public required string Pattern { get; set; }

    public int Count { get; set; }

    public required string Level { get; set; }

    public DateTime FirstOccurrence { get; set; }

    public DateTime LastOccurrence { get; set; }
}