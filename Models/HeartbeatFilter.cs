public class HeartbeatFilter
{
    public string? DeviceId { get; set; }

    public bool? HasCloudConnectionFailure { get; set; }

    public int Limit { get; set; } = 100;
}