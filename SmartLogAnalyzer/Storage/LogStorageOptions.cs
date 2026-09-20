namespace SmartLogAnalyzer.Storage;

public class LogStorageOptions
{
    public const string SectionName = "LogStorage";

    /// <summary>Blob endpoint, e.g. https://myaccount.blob.core.windows.net</summary>
    public string ServiceUri { get; set; } = string.Empty;

    /// <summary>Container the uploader tool writes to.</summary>
    public string Container { get; set; } = "gatewaylogs";

    /// <summary>SAS token granting at least read + list on the container. Keep out of source control.</summary>
    public string SasToken { get; set; } = string.Empty;

    /// <summary>Optional prefix if device folders are not at the container root.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Subfolders of <c>artefacts/</c> that contain gateway log files. Anything else under a
    /// collection folder is diagnostic output, not a log, and is ignored.
    /// </summary>
    public IList<string> ArtefactLogFolders { get; set; } = ["ApplicationLog", "AuditLog"];

    /// <summary>How long a blob listing stays valid before it is re-listed.</summary>
    public int CatalogTtlSeconds { get; set; } = 300;

    /// <summary>Uploaded blobs never change, so parsed content is kept as long as it keeps being used.</summary>
    public int FileCacheSlidingMinutes { get; set; } = 240;

    /// <summary>Upper bound on cached log lines across all files. Oldest entries are evicted first.</summary>
    public long MaxCachedLogLines { get; set; } = 2_000_000;

    /// <summary>Maximum number of blobs downloaded concurrently for a single query.</summary>
    public int MaxParallelDownloads { get; set; } = 8;

    /// <summary>Safety valve so an unscoped query cannot pull the entire container into memory.</summary>
    public int MaxFilesPerQuery { get; set; } = 500;

    public bool WarmupEnabled { get; set; } = true;

    public int WarmupIntervalSeconds { get; set; } = 600;

    /// <summary>Only collections newer than this are pre-loaded by the warm-up service.</summary>
    public int WarmupRecentDays { get; set; } = 3;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUri) && !string.IsNullOrWhiteSpace(Container);
}
