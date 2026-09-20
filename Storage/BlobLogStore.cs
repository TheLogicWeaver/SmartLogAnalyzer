using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SmartLogAnalyzer.Storage;

/// <summary>
/// Reads logs from blob storage and keeps the parsed result in memory. Uploaded blobs are
/// immutable, so a file is downloaded and parsed at most once per version, which keeps both the
/// response time and the storage transaction cost down.
/// </summary>
public sealed class BlobLogStore : ILogStore, IDisposable
{
    private static readonly Regex TranscriptTimeRegex =
        new(@"^(?<time>\d{1,2}:\d{2}:\d{2})", RegexOptions.Compiled);

    private const string TranscriptSource = "DiagnosticTranscript";

    private readonly BlobContainerClient _container;
    private readonly BlobLogCatalog _catalog;
    private readonly LogStorageOptions _options;
    private readonly ILogger<BlobLogStore> _logger;
    private readonly MemoryCache _fileCache;
    private readonly SemaphoreSlim _downloadThrottle;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<LogRecord>>> _inFlight = new();

    public BlobLogStore(
        BlobContainerClient container,
        BlobLogCatalog catalog,
        IOptions<LogStorageOptions> options,
        ILogger<BlobLogStore> logger)
    {
        _container = container;
        _catalog = catalog;
        _options = options.Value;
        _logger = logger;

        _fileCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = Math.Max(10_000, _options.MaxCachedLogLines)
        });

        _downloadThrottle = new SemaphoreSlim(Math.Max(1, _options.MaxParallelDownloads));
    }

    public Task<IReadOnlyList<string>> GetDeviceIdsAsync(CancellationToken cancellationToken = default) =>
        _catalog.GetDeviceIdsAsync(cancellationToken);

    public async Task<IReadOnlyList<LogRecord>> GetGatewayLogsAsync(
        LogScope scope,
        CancellationToken cancellationToken = default)
    {
        var files = await SelectFilesAsync(scope, LogFileKind.GatewayLog, cancellationToken);
        var loaded = await LoadAsync(files, cancellationToken);

        var records = loaded.SelectMany(static records => records);

        if (scope.StartTime.HasValue)
            records = records.Where(record => record.Timestamp >= scope.StartTime.Value);

        if (scope.EndTime.HasValue)
            records = records.Where(record => record.Timestamp <= scope.EndTime.Value);

        return records
            .OrderBy(record => record.Timestamp)
            .ThenBy(record => record.FilePath, StringComparer.Ordinal)
            .ThenBy(record => record.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<TranscriptDocument>> GetTranscriptsAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return [];

        var files = await SelectFilesAsync(new LogScope(deviceId), LogFileKind.Transcript, cancellationToken);
        var documents = new List<TranscriptDocument>(files.Count);

        foreach (var file in files)
        {
            var records = await LoadFileAsync(file);

            if (records.Count == 0)
                continue;

            documents.Add(new TranscriptDocument(
                file.Path,
                file.DeviceId,
                file.LogDate,
                records.Select(record => record.RawLine).ToList()));
        }

        return documents;
    }

    public async Task<int> WarmAsync(int recentDays, CancellationToken cancellationToken = default)
    {
        var fromDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-Math.Max(0, recentDays)));
        var files = await _catalog.GetFilesAsync(null, fromDate, null, cancellationToken);
        var deduplicated = Deduplicate(files);

        await LoadAsync(deduplicated, cancellationToken);

        return deduplicated.Count;
    }

    private async Task<IReadOnlyList<LogFileReference>> SelectFilesAsync(
        LogScope scope,
        LogFileKind kind,
        CancellationToken cancellationToken)
    {
        // Only the lower bound can prune collections: a bundle uploaded on day D can contain logs
        // from well before D, but never after it.
        var fromDate = scope.StartTime.HasValue
            ? DateOnly.FromDateTime(scope.StartTime.Value.Date)
            : (DateOnly?)null;

        var files = await _catalog.GetFilesAsync(scope.DeviceId, fromDate, null, cancellationToken);

        var selected = Deduplicate(files.Where(file => file.Kind == kind).ToList());

        if (selected.Count <= _options.MaxFilesPerQuery)
            return selected;

        _logger.LogWarning(
            "Query matched {MatchedCount} files which exceeds MaxFilesPerQuery ({Limit}); using the most recent files only.",
            selected.Count,
            _options.MaxFilesPerQuery);

        return selected
            .OrderByDescending(file => file.LogDate)
            .ThenByDescending(file => file.LastModified)
            .Take(_options.MaxFilesPerQuery)
            .ToList();
    }

    /// <summary>
    /// Repeated diagnostic collections re-upload the same source files, so the freshest copy of
    /// each one wins. Without this every line would be counted once per collection run.
    /// </summary>
    private static List<LogFileReference> Deduplicate(IReadOnlyList<LogFileReference> files) =>
        files
            .GroupBy(file => file.DeduplicationKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(file => file.LogDate)
                .ThenByDescending(file => file.LastModified)
                .ThenByDescending(file => file.Length)
                .First())
            .ToList();

    private async Task<IReadOnlyList<IReadOnlyList<LogRecord>>> LoadAsync(
        IReadOnlyList<LogFileReference> files,
        CancellationToken cancellationToken)
    {
        var results = new IReadOnlyList<LogRecord>[files.Count];

        var tasks = files.Select(async (file, index) =>
        {
            await _downloadThrottle.WaitAsync(cancellationToken);
            try
            {
                results[index] = await LoadFileAsync(file);
            }
            finally
            {
                _downloadThrottle.Release();
            }
        });

        await Task.WhenAll(tasks);

        return results;
    }

    private Task<IReadOnlyList<LogRecord>> LoadFileAsync(LogFileReference file)
    {
        var key = file.VersionKey;

        if (_fileCache.TryGetValue(key, out IReadOnlyList<LogRecord>? cached) && cached is not null)
            return Task.FromResult(cached);

        // De-duplicate concurrent requests for the same blob so it is downloaded only once.
        return _inFlight.GetOrAdd(key, _ => LoadAndCacheAsync(file, key));
    }

    private async Task<IReadOnlyList<LogRecord>> LoadAndCacheAsync(LogFileReference file, string key)
    {
        try
        {
            var records = await ReadFileAsync(file);

            _fileCache.Set(key, records, new MemoryCacheEntryOptions
            {
                Size = Math.Max(1, records.Count),
                SlidingExpiration = TimeSpan.FromMinutes(Math.Max(1, _options.FileCacheSlidingMinutes))
            });

            return records;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<IReadOnlyList<LogRecord>> ReadFileAsync(LogFileReference file)
    {
        var records = new List<LogRecord>();

        try
        {
            var client = _container.GetBlobClient(file.Path);

            await using var stream = await client.OpenReadAsync();
            using var reader = new StreamReader(stream);

            // The transcript parser carries device context across lines, so each file needs its own.
            var processor = new LogProcessingService(new LogParserFactory());

            long ordinal = 0;
            string? line;

            while ((line = await reader.ReadLineAsync()) is not null)
            {
                ordinal++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parsed = processor.ProcessLine(line);

                if (parsed is null)
                    continue;

                records.Add(new LogRecord
                {
                    Id = ordinal,
                    Timestamp = ResolveTimestamp(parsed, line, file),
                    Level = parsed.Level,
                    Message = parsed.Message,
                    DeviceId = string.IsNullOrWhiteSpace(parsed.DeviceId) ? file.DeviceId : parsed.DeviceId,
                    PartitionDeviceId = file.DeviceId,
                    Source = parsed.Source ?? string.Empty,
                    EventId = parsed.EventId,
                    RawLine = line,
                    FilePath = file.Path,
                    Kind = file.Kind,
                    Metadata = parsed.Metadata
                });
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to read blob {Path}.", file.Path);
            return [];
        }

        return records;
    }

    private static DateTime ResolveTimestamp(LogEntry parsed, string rawLine, LogFileReference file)
    {
        if (!string.Equals(parsed.Source, TranscriptSource, StringComparison.Ordinal))
            return parsed.Timestamp;

        // Transcript lines have no date of their own, so the collection date from the path wins.
        var match = TranscriptTimeRegex.Match(rawLine);

        var timeOfDay = match.Success && TimeSpan.TryParse(match.Groups["time"].Value, out var parsedTime)
            ? parsedTime
            : TimeSpan.Zero;

        return file.LogDate.ToDateTime(TimeOnly.MinValue).Add(timeOfDay);
    }

    public void Dispose()
    {
        _fileCache.Dispose();
        _downloadThrottle.Dispose();
    }
}
