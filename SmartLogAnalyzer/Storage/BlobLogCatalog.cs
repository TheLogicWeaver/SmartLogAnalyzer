using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SmartLogAnalyzer.Storage;

/// <summary>
/// Keeps a cached view of which log blobs exist so queries can prune by device and date before
/// anything is downloaded.
/// </summary>
public sealed class BlobLogCatalog
{
    private const string DeviceListCacheKey = "logstorage:devices";

    private readonly BlobContainerClient _container;
    private readonly LogStorageOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BlobLogCatalog> _logger;
    private readonly string[] _artefactLogFolders;

    public BlobLogCatalog(
        BlobContainerClient container,
        IOptions<LogStorageOptions> options,
        IMemoryCache cache,
        ILogger<BlobLogCatalog> logger)
    {
        _container = container;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _artefactLogFolders = _options.ArtefactLogFolders.ToArray();
    }

    public async Task<IReadOnlyList<string>> GetDeviceIdsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(DeviceListCacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var prefix = NormalizePrefix(_options.RootPath);
        var devices = new List<string>();

        try
        {
            // A delimited listing returns only the top-level "folders", not every blob.
            await foreach (var item in _container.GetBlobsByHierarchyAsync(
                BlobTraits.None,
                BlobStates.None,
                delimiter: "/",
                prefix: prefix,
                cancellationToken: cancellationToken))
            {
                if (!item.IsPrefix)
                    continue;

                var name = item.Prefix.TrimEnd('/');
                var leaf = name[(name.LastIndexOf('/') + 1)..];

                if (leaf.Length > 0)
                    devices.Add(leaf);
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to list device folders in container {Container}.", _container.Name);
            return [];
        }

        devices.Sort(StringComparer.OrdinalIgnoreCase);
        _cache.Set(DeviceListCacheKey, (IReadOnlyList<string>)devices, CatalogEntryOptions());

        return devices;
    }

    /// <summary>
    /// Returns every log blob matching the supplied coordinates. A device filter restricts the
    /// listing to that device's prefix, which is the cheapest possible call against storage.
    /// </summary>
    public async Task<IReadOnlyList<LogFileReference>> GetFilesAsync(
        string? deviceId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        var deviceFolders = await ResolveDeviceFoldersAsync(deviceId, cancellationToken);
        var files = new List<LogFileReference>();

        foreach (var folder in deviceFolders)
        {
            var folderFiles = await GetFilesForDeviceAsync(folder, cancellationToken);

            files.AddRange(folderFiles.Where(file =>
                (!fromDate.HasValue || file.LogDate >= fromDate.Value) &&
                (!toDate.HasValue || file.LogDate <= toDate.Value)));
        }

        return files;
    }

    public async Task<IReadOnlyList<LogFileReference>> GetFilesForDeviceAsync(
        string deviceFolder,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"logstorage:files:{deviceFolder.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<LogFileReference>? cached) && cached is not null)
            return cached;

        var prefix = $"{NormalizePrefix(_options.RootPath)}{deviceFolder}/";
        var files = new List<LogFileReference>();

        try
        {
            await foreach (var item in _container.GetBlobsAsync(
                BlobTraits.None,
                BlobStates.None,
                prefix: prefix,
                cancellationToken: cancellationToken))
            {
                if (LogPathParser.TryParse(
                        item.Name,
                        item.Properties.ContentLength ?? 0,
                        item.Properties.LastModified,
                        _options.RootPath,
                        _artefactLogFolders,
                        out var reference))
                {
                    files.Add(reference);
                }
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to list blobs under prefix {Prefix}.", prefix);
            return [];
        }

        files.Sort(static (left, right) =>
        {
            var byDate = left.LogDate.CompareTo(right.LogDate);
            if (byDate != 0)
                return byDate;

            var bySequence = string.CompareOrdinal(left.Sequence, right.Sequence);
            return bySequence != 0 ? bySequence : string.CompareOrdinal(left.Path, right.Path);
        });

        _cache.Set(cacheKey, (IReadOnlyList<LogFileReference>)files, CatalogEntryOptions());

        return files;
    }

    private async Task<IReadOnlyList<string>> ResolveDeviceFoldersAsync(
        string? deviceId,
        CancellationToken cancellationToken)
    {
        var allDevices = await GetDeviceIdsAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(deviceId))
            return allDevices;

        var exact = allDevices.FirstOrDefault(device =>
            device.Equals(deviceId, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
            return [exact];

        return allDevices
            .Where(device => device.Contains(deviceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private MemoryCacheEntryOptions CatalogEntryOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(30, _options.CatalogTtlSeconds))
    };

    private static string NormalizePrefix(string rootPath)
    {
        var trimmed = rootPath.Replace('\\', '/').Trim('/');
        return trimmed.Length == 0 ? string.Empty : $"{trimmed}/";
    }
}
