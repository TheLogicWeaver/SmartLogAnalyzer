using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SmartLogAnalyzer.Models;

public class DiagnosticTranscriptService
{
    private static readonly Regex SectionRegex = new Regex(@"^\s*(?<sec>(?:\d+\.\d+|[A-Z]\.\d+))\b", RegexOptions.Compiled);
    private readonly AppDbContext _db;

    public DiagnosticTranscriptService(AppDbContext db)
    {
        _db = db;
    }

    public Task<object?> AnalyzeAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult<object?>(null);

        var d = deviceId.Trim();

        // Find rows that reference the device either in DeviceId or RawLine
        var matches = _db.Logs
            .Where(x => EF.Functions.Like(x.DeviceId, $"%{d}%") || EF.Functions.Like(x.RawLine, $"%{d}%"))
            .OrderBy(x => x.Id)
            .ToList();

        if (!matches.Any())
            return Task.FromResult<object?>(null);

        var minId = matches.First().Id;
        var maxId = matches.Last().Id;

        // Expand window to capture surrounding section headers and blocks
        var windowBefore = 500;
        var windowAfter = 500;

        var startId = Math.Max(0, minId - windowBefore);
        var endId = maxId + windowAfter;

        var block = _db.Logs
            .Where(x => x.Id >= startId && x.Id <= endId)
            .OrderBy(x => x.Id)
            .Select(x => x.RawLine)
            .ToList();

        if (!block.Any())
            return Task.FromResult<object?>(null);

        var isExact = matches.Any(x => !string.IsNullOrWhiteSpace(x.DeviceId) && string.Equals(x.DeviceId.Trim(), d, StringComparison.OrdinalIgnoreCase));
        var result = AnalyzeLines(block, d, isExact);
        return Task.FromResult<object?>(result);
    }

    private object AnalyzeLines(List<string> includedLines, string sourceLabel, bool isExactDeviceQuery)
    {
        var total = includedLines.Count;
        var errors = includedLines.Count(l => l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0);
        var warnings = includedLines.Count(l => l.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0);

        // Global scan for certificate reads anywhere in the included window
        var globalCertReads = includedLines
            .Select(l => Regex.Match(l, "Read the certificate \"(?<cert>[^\"]+)\"", RegexOptions.IgnoreCase))
            .Where(m => m.Success)
            .Select(m => m.Groups["cert"].Value)
            .ToList();

        // topMessages removed: this API focuses on transcript diagnostic sections only

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
        object? gatewayService = null;
        object? deviceConfiguration = null;
        object? dpsTcp = null;
        object? tlsDps = null;
        var certReads = new List<string>();
        var trustedRootCAs = new List<string>();
        var gatewayClientCerts = new List<object>();

        object? winHttpProxyService = null;
        object? winHttpAutoProxyService = null;
        var gatewayTcpConnections = new List<string>();
        object? tls12Client = null;
        object? windowsTimeService = null;
        object? webSocketUpgrade = null;

        // Only include sections in the requested ranges: 1.2..A.3 (inclusive) and A.5
        bool inRange = false;
        foreach (var sec in sections)
        {
            var id = sec.Id;
            var lines = sec.Lines;

            // remove separator lines and blank lines for section-specific parsing
            var cleaned = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !Regex.IsMatch(l.Trim(), "^={3,}$")).ToList();

            var idPrefix = id.Split('_')[0];

            // Always capture Gateway TCP connections (6.2) regardless of the 1.2..A.3 range
            if (idPrefix == "6.2")
            {
                foreach (var ln in cleaned)
                {
                    var t = ln.Trim();
                    if (t.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) || t.IndexOf("PID", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Radiometer.Lc.IotGateway.Service", StringComparison.OrdinalIgnoreCase) >= 0)
                        gatewayTcpConnections.Add(t);
                }
                // continue to next section (we handled 6.2)
                continue;
            }

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

            if ((id.IndexOf("GatewayServiceStatus", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("1.2"))
            {
                var svcLine = cleaned.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (svcLine != null)
                {
                    var svcMatch = Regex.Match(svcLine.Trim(), @"^(?<svc>\S+)\s+(?<status>\S+)");
                    if (svcMatch.Success)
                        gatewayService = new { Service = svcMatch.Groups["svc"].Value, ServiceStatus = svcMatch.Groups["status"].Value };
                }
            }

            if ((id.IndexOf("DeviceConfiguration", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("1.3"))
            {
                var begin = lines.FindIndex(l => l.Contains("BEGIN DeviceConfiguration.json"));
                var end = lines.FindIndex(l => l.Contains("END DeviceConfiguration.json"));
                if (begin >= 0 && end > begin)
                {
                    var jsonLines = lines.Skip(begin + 1).Take(end - begin - 1).ToArray();
                    var jsonText = string.Join('\n', jsonLines);
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);
                        string? serial = null;
                        if (parsed.ValueKind == JsonValueKind.Object && parsed.TryGetProperty("ClientSerialNumber", out var el))
                            serial = el.GetString();

                        deviceConfiguration = new { Parsed = parsed, ClientSerialNumber = serial };
                    }
                    catch
                    {
                        deviceConfiguration = new { Raw = string.Join('\n', jsonLines) };
                    }
                }
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

            if ((id.IndexOf("TlsDpsDirect", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("2.2"))
            {
                var established = cleaned.Any(x => x.IndexOf("Established connection", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("SSL/TLS connection renegotiated", StringComparison.OrdinalIgnoreCase) >= 0);
                var httpResp = cleaned.FirstOrDefault(x => x.TrimStart().StartsWith("< HTTP/", StringComparison.OrdinalIgnoreCase));
                var xms = cleaned.FirstOrDefault(x => x.IndexOf("x-ms-request-id", StringComparison.OrdinalIgnoreCase) >= 0);
                tlsDps = new { TlsEstablished = established, HttpResponse = httpResp, XMsRequestId = xms };
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
                        winHttpAutoProxyService = kv;
                    }

                    if (kv.TryGetValue("Current WinHTTP proxy settings", out var win) || kv.TryGetValue("Direct access (no proxy server).", out win))
                    {
                        winHttpProxyService = new { Raw = string.Join("; ", cleaned), Proxy = win };
                    }
                }
            }

            // TCP connections owned by gateway process: collect lines under GatewayTcpConnections section (6.2)
            if ((id.IndexOf("GatewayTcpConnections", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("6.2"))
            {
                foreach (var ln in cleaned)
                {
                    if (Regex.IsMatch(ln, "^TCP\\s+", RegexOptions.IgnoreCase) || ln.Contains("PID") || ln.Contains("Radiometer.Lc.IotGateway.Service"))
                        gatewayTcpConnections.Add(ln.Trim());
                }
                continue;
            }

            // TLS 1.2 client settings
            if (lines.Any(l => l.IndexOf("TLS 1.2", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Tls12", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("SecurityProtocol", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var ln = lines.FirstOrDefault(l => l.IndexOf("TLS 1.2", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Tls12", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("SecurityProtocol", StringComparison.OrdinalIgnoreCase) >= 0);
                if (ln != null)
                {
                    var enabled = ln.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0 || ln.IndexOf("Enabled", StringComparison.OrdinalIgnoreCase) >= 0 || ln.IndexOf("Tls12", StringComparison.OrdinalIgnoreCase) >= 0 && ln.IndexOf("Enabled", StringComparison.OrdinalIgnoreCase) >= 0;
                    tls12Client = new { Raw = ln.Trim(), Enabled = enabled };
                }
            }

            // Windows Time service / time sync
            if (lines.Any(l => l.IndexOf("Windows Time", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("w32time", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var ln = lines.FirstOrDefault(l => l.IndexOf("Windows Time", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("w32time", StringComparison.OrdinalIgnoreCase) >= 0);
                if (ln != null)
                {
                    var m = Regex.Match(ln, "(Running|Stopped|Synchronized|Not synchronized|Sync|Succeeded)", RegexOptions.IgnoreCase);
                    var status = m.Success ? m.Groups[1].Value : null;
                    windowsTimeService = new { Raw = ln.Trim(), Status = status };
                }
            }

            // WebSocket upgrade probe
            if (lines.Any(l => l.IndexOf("WebSocket", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var wsLine = lines.FirstOrDefault(l => l.IndexOf("WebSocket", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0);
                if (wsLine != null)
                {
                    var ok = wsLine.IndexOf("101", StringComparison.OrdinalIgnoreCase) >= 0 || wsLine.IndexOf("Upgrade: websocket", StringComparison.OrdinalIgnoreCase) >= 0 || wsLine.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0 && wsLine.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) >= 0;
                    webSocketUpgrade = new { Raw = wsLine.Trim(), Success = ok };
                }
            }

            // Trusted root CAs (3.1)
            if ((id.IndexOf("TrustedRootCAs", StringComparison.OrdinalIgnoreCase) >= 0) || id.StartsWith("3.1"))
            {
                foreach (var ln in cleaned)
                {
                    // skip separators
                    if (Regex.IsMatch(ln.Trim(), "^={3,}$"))
                        continue;
                    trustedRootCAs.Add(ln.Trim());
                }
                continue;
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
                    var hpM = Regex.Match(t, "HasPrivateKey=(?<hp>\\S+)", RegexOptions.IgnoreCase);
                    var naM = Regex.Match(t, "NotAfter=(?<na>[^\n]+?)\\s", RegexOptions.IgnoreCase);
                    var thumbM = Regex.Match(t, "([A-Fa-f0-9]{16,})$");

                    var certObj = new
                    {
                        Line = t,
                        CN = cnM.Success ? cnM.Groups["cn"].Value : null,
                        HasPrivateKey = hpM.Success ? hpM.Groups["hp"].Value : null,
                        NotAfter = naM.Success ? naM.Groups["na"].Value.Trim() : null,
                        Thumbprint = thumbM.Success ? thumbM.Groups[1].Value : null
                    };

                    if (isExactDeviceQuery)
                    {
                        var cn = cnM.Success ? cnM.Groups["cn"].Value : null;
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

            // Collect certificate read lines (from app logs within included sections)
            foreach (var ln in lines)
            {
                var c = Regex.Match(ln, "Read the certificate \"(?<cert>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (c.Success)
                    certReads.Add(c.Groups["cert"].Value);
            }
        }

        // Merge global cert reads + section-local reads, and apply exact-device filtering if requested
        var allCerts = new List<string>();
        allCerts.AddRange(globalCertReads);
        allCerts.AddRange(certReads);
        var finalCerts = allCerts.Distinct().ToList();
        if (isExactDeviceQuery)
            finalCerts = finalCerts.Where(c => c.IndexOf(sourceLabel, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        return new
        {
            Source = sourceLabel,
            TotalIncludedLines = total,
            Errors = errors,
            Warnings = warnings,
            GatewayService = gatewayService,
            DeviceConfiguration = deviceConfiguration,
            DpsTcp = dpsTcp,
            TlsDps = tlsDps,
            CertificatesRead = finalCerts,
            TrustedRootCAs = trustedRootCAs.Distinct().ToList(),
            GatewayClientCertificates = gatewayClientCerts,
            WinHttpProxyService = winHttpProxyService,
            WinHttpAutoProxyService = winHttpAutoProxyService,
            GatewayTcpConnections = gatewayTcpConnections.Distinct().ToList(),
            Tls12Client = tls12Client,
            WindowsTimeService = windowsTimeService,
            WebSocketUpgrade = webSocketUpgrade
        };
    }

    private string NormalizeMessage(string line)
    {
        var t = Regex.Replace(line, @"\d{1,2}:\d{2}:\d{2}", "");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }
}
