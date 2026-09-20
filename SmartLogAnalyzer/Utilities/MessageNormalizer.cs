using System.Text.RegularExpressions;

public class MessageNormalizer
{
    public string Normalize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var rules = new List<(string Pattern, string Replacement)>
        {
            (@"\b\d{4}-\d{2}-\d{2}\b", "{DATE}"),
            (@"\b\d{2}:\d{2}:\d{2}\b", "{TIME}"),
            (@"\b\d+\b", "{NUMBER}"),
            (@"\b(?:\d{1,3}\.){3}\d{1,3}\b", "{IP}"),
            (@"https?:\/\/\S+", "{URL}"),
            ("\".*?\"", "\"{VALUE}\""),
            (@"\b[a-fA-F0-9]{8}\-[a-fA-F0-9]{4}\-[a-fA-F0-9]{4}\-[a-fA-F0-9]{4}\-[a-fA-F0-9]{12}\b", "{GUID}")
        };

        foreach (var rule in rules)
        {
            message = Regex.Replace(
                message,
                rule.Pattern,
                rule.Replacement);
        }

        return message;
    }
}