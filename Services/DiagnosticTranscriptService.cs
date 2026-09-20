using System.Text.RegularExpressions;
using System.Text.Json;
using SmartLogAnalyzer.Models;
using SmartLogAnalyzer.Storage;

public class DiagnosticTranscriptService
{
    private static readonly Regex SectionRegex = new Regex(@"^\s*(?<sec>(?:\d+\.\d+|[A-Z]\.\d+))\b", RegexOptions.Compiled);

    private readonly ILogStore _store;

    public DiagnosticTranscriptService(ILogStore store)
    {
        _store = store;
    }

    public async Task<object?> AnalyzeAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        var d = deviceId.Trim();

        var transcripts = await _store.GetTranscriptsAsync(d, cancellationToken);

        if (transcripts.Count == 0)
            return null;

        // A transcript file is already the analysis window, so the newest upload is used as-is.
        var latest = transcripts
            .OrderByDescending(transcript => transcript.LogDate)
            .ThenByDescending(transcript => transcript.Path, StringComparer.Ordinal)
            .First();

        if (latest.Lines.Count == 0)
            return null;

        var isExact = transcripts.Any(transcript =>
            transcript.DeviceId.Trim().Equals(d, StringComparison.OrdinalIgnoreCase));

        return AnalyzeLines(latest.Lines.ToList(), d, isExact);
    }

    private object AnalyzeLines(List<string> includedLines, string sourceLabel, bool isExactDeviceQuery)
    {
        // Parse sections into structured diagnostic checks
        var sections = new List<(string Id, string Title, List<string> Lines)>();
        string? curId = null;
        string? curTitle = null;
        List<string>? curLines = null;

        var headerRegex = new Regex(@"\[\s*(?<id>[^\]]+)\]\s*(?<title>.*)", RegexOptions.Compiled);

        foreach (var l in includedLines)
        {
            var m = headerRegex.Match(l);
            if (m.Success)
            {
                if (curId is not null && curLines is not null)
                    sections.Add((curId, curTitle ?? string.Empty, curLines));

                curId = m.Groups["id"].Value.Trim();
                curTitle = m.Groups["title"].Value.Trim();
                curLines = new List<string>();
                continue;
            }

            if (curLines is not null)
                curLines.Add(l);
        }

        if (curId is not null && curLines is not null)
            sections.Add((curId, curTitle ?? string.Empty, curLines));

        // Structured extraction for known sections
        object? deviceConfiguration = null;
        object? dpsTcp = null;
        var gatewayClientCerts = new List<object>();

        object? winHttpProxyService = null;
        object? winHttpAutoProxyService = null;

        // Only include sections in the requested ranges: 1.2..A.3 (inclusive) and A.5
        bool inRange = false;
        foreach (var sec in sections)
        {
            var id = sec.Id;
            var lines = sec.Lines;

            // remove separator lines and blank lines for section-specific parsing
            var cleaned = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !Regex.IsMatch(l.Trim(), "^={3,}$")).ToList();

            var idPrefix = id.Split('_')[0];

            // 6.2 is not reported; skip it so its lines cannot leak into other checks.
            if (idPrefix == "6.2")
                continue;

            if (idPrefix == "1.2")
                inRange = true;

            var includeSection = false;
            if (inRange)
                includeSection = true;
            if (idPrefix == "A.5")
                includeSection = true;
            if (!inRange && idPrefix != "A.5")
                includeSection = false;

            if (!includeSection)
            {
                if (idPrefix == "A.3")
                    inRange = false; // end of initial range
                continue;
            }

            if (idPrefix == "A.3")
                inRange = false; // include A.3 then end range

            if ((id.IndexOf("DeviceConfiguration", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("1.3"))
            {
                var valid = false;
                var begin = lines.FindIndex(l => l.Contains("BEGIN DeviceConfiguration.json"));
                var end = lines.FindIndex(l => l.Contains("END DeviceConfiguration.json"));
                if (begin >= 0 && end > begin)
                {
                    var jsonText = string.Join('\n', lines.Skip(begin + 1).Take(end - begin - 1));
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);

                        valid = parsed.ValueKind == JsonValueKind.Object
                            && HasText(parsed, "ClientSerialNumber")
                            && HasText(parsed, "ClientType")
                            && HasText(parsed, "BusinessUnitName")
                            && parsed.TryGetProperty("BuAcknowledged", out var acknowledged)
                            && acknowledged.ValueKind == JsonValueKind.True;
                    }
                    catch (JsonException)
                    {
                        valid = false;
                    }
                }

                deviceConfiguration = new { Valid = valid };
            }

            if ((id.IndexOf("TcpDps443", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("2.1"))
            {
                var resolved = cleaned.FirstOrDefault(l => l.IndexOf("Resolved", StringComparison.OrdinalIgnoreCase) >= 0);
                var tcpTest = cleaned.FirstOrDefault(x => x.IndexOf("TcpTestSucceeded", StringComparison.OrdinalIgnoreCase) >= 0);
                var pingTest = cleaned.FirstOrDefault(x => x.IndexOf("PingSucceeded", StringComparison.OrdinalIgnoreCase) >= 0);

                int? latencyMs = null;
                if (tcpTest != null)
                {
                    var m = Regex.Match(tcpTest, @"(\d+) ms");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var ms))
                        latencyMs = ms;
                }

                dpsTcp = new
                {
                    Resolved = resolved,
                    TcpTestSucceeded = tcpTest != null && tcpTest.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0,
                    PingSucceeded = pingTest != null && pingTest.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0,
                    LatencyMs = latencyMs
                };
            }

            // WinHTTP proxy blocks: parse key: value pairs inside section (A.3 / A.5)
            if (idPrefix == "A.3" || idPrefix == "A.5" || cleaned.Any(l => l.IndexOf("WinHttp", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var kv = new Dictionary<string, string>();
                foreach (var ln in cleaned)
                {
                    var m = Regex.Match(ln, "^(?<k>[^:]+):\\s*(?<v>.*)$");
                    if (m.Success)
                        kv[m.Groups["k"].Value.Trim()] = m.Groups["v"].Value.Trim();
                }

                if (kv.Count > 0)
                {
                    if (kv.TryGetValue("Name", out var name) && name.IndexOf("WinHttpAutoProxySvc", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var svc = new Dictionary<string, string>();
                        foreach (var key in new[] { "Name", "DisplayName", "Status", "StartType" })
                        {
                            if (kv.TryGetValue(key, out var value))
                                svc[key] = value;
                        }

                        winHttpAutoProxyService = svc;
                    }

                    if (kv.TryGetValue("Current WinHTTP proxy settings", out var win) || kv.TryGetValue("Direct access (no proxy server).", out win))
                    {
                        winHttpProxyService = new { Raw = string.Join("; ", cleaned), Proxy = win };
                    }
                }
            }

            // Gateway client certificate entry (4.1)
            if ((id.IndexOf("GatewayClientCert", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("4.1"))
            {
                foreach (var ln in cleaned)
                {
                    var t = ln.Trim();
                    if (string.IsNullOrEmpty(t))
                        continue;
                    // skip separator lines
                    if (Regex.IsMatch(t, "^={3,}$"))
                        continue;

                    var cnM = Regex.Match(t, "CN=(?<cn>[^\\s,]+)", RegexOptions.IgnoreCase);

                    // Section 4.1 also carries status text; only real certificate lines are reported.
                    if (!cnM.Success)
                        continue;

                    var hpM = Regex.Match(t, "HasPrivateKey=(?<hp>\\S+)", RegexOptions.IgnoreCase);
                    var naM = Regex.Match(t, "NotAfter=(?<na>[^\n]+?)\\s", RegexOptions.IgnoreCase);
                    var thumbM = Regex.Match(t, "([A-Fa-f0-9]{16,})$");

                    var certObj = new
                    {
                        Line = t,
                        CN = cnM.Groups["cn"].Value,
                        HasPrivateKey = hpM.Success ? hpM.Groups["hp"].Value : null,
                        NotAfter = naM.Success ? naM.Groups["na"].Value.Trim() : null,
                        Thumbprint = thumbM.Success ? thumbM.Groups[1].Value : null
                    };

                    if (isExactDeviceQuery)
                    {
                        var cn = cnM.Groups["cn"].Value;
                        if (string.Equals(cn, sourceLabel, StringComparison.OrdinalIgnoreCase) || t.IndexOf(sourceLabel, StringComparison.OrdinalIgnoreCase) >= 0)
                            gatewayClientCerts.Add(certObj);
                    }
                    else
                    {
                        gatewayClientCerts.Add(certObj);
                    }
                }
                continue;
            }
        }

        return new
        {
            DeviceConfiguration = deviceConfiguration,
            DpsTcp = dpsTcp,
            GatewayClientCertificates = gatewayClientCerts,
            WinHttpProxyService = winHttpProxyService,
            WinHttpAutoProxyService = winHttpAutoProxyService
        };
    }

    private static bool HasText(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());

    private string NormalizeMessage(string line)
    {
        var t = Regex.Replace(line, @"\d{1,2}:\d{2}:\d{2}", "");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }
}
