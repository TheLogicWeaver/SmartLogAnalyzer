public class ConnectionIncidentFilter
{
    public string? DeviceId { get; set; }

    // Nullable so [AsParameters] treats it as optional rather than a required query string value.
    public int? Limit { get; set; }

    public int ResolvedLimit => Limit is > 0 ? Limit.Value : 100;
}
