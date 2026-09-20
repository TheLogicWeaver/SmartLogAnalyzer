using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using SmartLogAnalyzer.Functions.Configuration;

namespace SmartLogAnalyzer.Functions.Services;

/// <summary>
/// Writes the consolidated analysis document next to the logs it was derived from.
/// </summary>
public sealed class SummaryBlobWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly DiagnosticExtractionOptions _options;

    public SummaryBlobWriter(IOptions<DiagnosticExtractionOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> WriteAsync(
        string storagePath,
        JsonObject document,
        CancellationToken cancellationToken)
    {
        var (containerName, prefix) = SplitStoragePath(storagePath);
        var blobPath = $"{prefix}/{_options.SummaryFolderName.Trim('/')}/{_options.SummaryFileName}";

        var container = new BlobContainerClient(BuildContainerUri(containerName));
        var blob = container.GetBlobClient(blobPath);

        var payload = Encoding.UTF8.GetBytes(document.ToJsonString(WriteOptions));
        using var stream = new MemoryStream(payload);

        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            },
            cancellationToken);

        return $"{containerName}/{blobPath}";
    }

    private Uri BuildContainerUri(string containerName)
    {
        var sasToken = _options.LogsSasToken.TrimStart('?');
        var baseUri = $"{_options.LogsBlobServiceUri.TrimEnd('/')}/{containerName}";

        return string.IsNullOrWhiteSpace(sasToken)
            ? new Uri(baseUri)
            : new Uri($"{baseUri}?{sasToken}");
    }

    /// <summary>StoragePath starts with the container name, e.g. "gatewaylogs/device/2026/...".</summary>
    private static (string ContainerName, string Prefix) SplitStoragePath(string storagePath)
    {
        var normalized = storagePath.Replace('\\', '/').Trim('/');
        var separator = normalized.IndexOf('/');

        if (separator <= 0 || separator == normalized.Length - 1)
            throw new InvalidOperationException($"StoragePath '{storagePath}' does not contain a container and a path.");

        return (normalized[..separator], normalized[(separator + 1)..]);
    }
}
