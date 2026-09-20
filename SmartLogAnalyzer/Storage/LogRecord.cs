namespace SmartLogAnalyzer.Storage;

public enum LogFileKind
{
    GatewayLog,
    Transcript
}

/// <summary>
/// A single parsed log line. Replaces the former EF entity; instances only ever live in the cache.
/// </summary>
public sealed class LogRecord
{
    /// <summary>Label the gateway writes before it knows its own identity.</summary>
    public const string UnprovisionedDeviceId = "DeviceNotProvisioned";

    /// <summary>Line ordinal inside <see cref="FilePath"/>, used to keep original file order.</summary>
    public long Id { get; init; }

    public DateTime Timestamp { get; init; }

    public required string Level { get; init; }

    public required string Message { get; init; }

    /// <summary>Device reported by the log line itself, which may be "DeviceNotProvisioned".</summary>
    public required string DeviceId { get; init; }

    /// <summary>Device folder the file was uploaded under, always the real serial number.</summary>
    public required string PartitionDeviceId { get; init; }

    public required string Source { get; init; }

    public int? EventId { get; init; }

    public required string RawLine { get; init; }

    public required string FilePath { get; init; }

    public LogFileKind Kind { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// The device this line really belongs to. Gateways log <c>DeviceNotProvisioned</c> until they
    /// are provisioned, but the upload folder always names the real serial.
    /// </summary>
    public string EffectiveDeviceId =>
        string.IsNullOrWhiteSpace(DeviceId) ||
        DeviceId.Equals(UnprovisionedDeviceId, StringComparison.OrdinalIgnoreCase)
            ? PartitionDeviceId
            : DeviceId;
}
