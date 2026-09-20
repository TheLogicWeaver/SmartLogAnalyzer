namespace SmartLogAnalyzer.Storage;

/// <summary>
/// A log blob discovered in the container, together with the coordinates decoded from its name.
/// </summary>
public sealed record LogFileReference(
    string Path,
    string DeviceId,
    DateOnly LogDate,
    string Sequence,
    string Collection,
    string ContentKey,
    LogFileKind Kind,
    long Length,
    DateTimeOffset? LastModified)
{
    /// <summary>Cache key that changes whenever the blob is replaced.</summary>
    public string VersionKey => $"{Path}|{LastModified?.UtcTicks ?? 0}|{Length}";

    /// <summary>
    /// Identifies the same source file across repeated collections, so only the freshest copy is read.
    /// </summary>
    public string DeduplicationKey => $"{DeviceId}|{ContentKey}".ToLowerInvariant();
}

/// <summary>
/// Decodes the uploader's hierarchy:
/// {deviceId}/{yyyy}/{MM}/{dd}/{sequence}/{collection}/00_transcript.log
/// {deviceId}/{yyyy}/{MM}/{dd}/{sequence}/{collection}/artefacts/{area}/*.txt
/// </summary>
public static class LogPathParser
{
    private const string ArtefactsFolder = "artefacts";
    private static readonly string[] LogExtensions = [".log", ".txt"];

    public static bool TryParse(
        string path,
        long length,
        DateTimeOffset? lastModified,
        string rootPath,
        IReadOnlyCollection<string> artefactLogFolders,
        out LogFileReference reference)
    {
        reference = null!;

        var relative = StripRoot(path, rootPath);
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // deviceId / yyyy / MM / dd / sequence / collection / ...file
        if (segments.Length < 7)
            return false;

        var fileName = segments[^1];
        if (!LogExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!int.TryParse(segments[1], out var year) ||
            !int.TryParse(segments[2], out var month) ||
            !int.TryParse(segments[3], out var day))
            return false;

        DateOnly logDate;
        try
        {
            logDate = new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var tail = segments[6..];
        var contentKey = string.Join('/', tail);

        LogFileKind kind;

        if (string.Equals(tail[0], ArtefactsFolder, StringComparison.OrdinalIgnoreCase))
        {
            // Only the configured artefact areas hold gateway logs; EventLogs and manifests do not.
            if (tail.Length < 3 || !artefactLogFolders.Contains(tail[1], StringComparer.OrdinalIgnoreCase))
                return false;

            kind = LogFileKind.GatewayLog;
        }
        else if (tail.Length == 1 && fileName.Contains("transcript", StringComparison.OrdinalIgnoreCase))
        {
            kind = LogFileKind.Transcript;
        }
        else
        {
            // Numbered diagnostic section files duplicate transcript content; skip them.
            return false;
        }

        reference = new LogFileReference(
            path,
            segments[0],
            logDate,
            segments[4],
            segments[5],
            contentKey,
            kind,
            length,
            lastModified);

        return true;
    }

    private static string StripRoot(string path, string rootPath)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        var root = rootPath.Replace('\\', '/').Trim('/');

        if (root.Length == 0 || !normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            return normalized;

        return normalized[(root.Length + 1)..];
    }
}
