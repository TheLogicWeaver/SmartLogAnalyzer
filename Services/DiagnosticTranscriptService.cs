using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SmartLogAnalyzer.Models;

public class DiagnosticTranscriptService
{
    private static readonly Regex SectionRegex = new Regex(@"^\s*(?<sec>(?:\d+\.\d+|[A-Z]\.\d+))\b", RegexOptions.Compiled);

    // "<subject>   NotAfter=<yyyy-MM-dd HH:mm:ssZ> <thumbprint>" as emitted by sections 3.1 and 3.2.
    private static readonly Regex CertificateLineRegex = new Regex(
        @"^(?<subject>.+?)\s+NotAfter=(?<notAfter>.+?)\s+(?<thumbprint>[A-Fa-f0-9]{20,})\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ServerCertFieldRegex = new Regex(
        @"^(?<k>Subject|Issuer|NotBefore|NotAfter|Thumbprint)\s*:\s*(?<v>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        var trustedRootCAs = new List<TranscriptCertificate>();
        var seenTrustedRootThumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var intermediateCAs = new List<TranscriptCertificate>();
        var seenIntermediateThumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object? serverCertificate = null;
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
                var begin = lines.FindIndex(l => l.Contains("BEGIN DeviceConfiguration.json"));
                var end = lines.FindIndex(l => l.Contains("END DeviceConfiguration.json"));
                if (begin >= 0 && end > begin)
                {
                    var jsonLines = lines.Skip(begin + 1).Take(end - begin - 1).ToArray();
                    var jsonText = string.Join('\n', jsonLines);
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<JsonElement>(jsonText);

                        // Dictionary keeps the original property casing in the response.
                        var config = new Dictionary<string, string?>();
                        if (parsed.ValueKind == JsonValueKind.Object)
                        {
                            if (parsed.TryGetProperty("ClientType", out var clientType))
                                config["ClientType"] = clientType.GetString();
                            if (parsed.TryGetProperty("BusinessUnitName", out var businessUnit))
                                config["BusinessUnitName"] = businessUnit.GetString();
                        }

                        if (config.Count > 0)
                            deviceConfiguration = config;
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

            // TCP connections owned by gateway process: collect lines under GatewayTcpConnections section (6.2)
            // Trusted root CAs (3.1)
            if ((id.IndexOf("TrustedRootCAs", StringComparison.OrdinalIgnoreCase) >= 0) || idPrefix == "3.1")
            {
                foreach (var ln in cleaned)
                {
                    var t = ln.Trim();
                    if (t.Length == 0 || Regex.IsMatch(t, "^={3,}$"))
                        continue;

                    var cert = ParseCertificateLine(t);
                    if (cert is null)
                        continue;

                    if (cert.Thumbprint is null || seenTrustedRootThumbprints.Add(cert.Thumbprint))
                        trustedRootCAs.Add(cert);
                }
                continue;
            }

            // Intermediate CAs in the local machine store (3.2)
            if ((id.IndexOf("IntermediateCACheck", StringComparison.OrdinalIgnoreCase) >= 0) || idPrefix == "3.2")
            {
                foreach (var ln in cleaned)
                {
                    var t = ln.Trim();
                    if (t.Length == 0 || Regex.IsMatch(t, "^={3,}$"))
                        continue;

                    var cert = ParseCertificateLine(t);
                    if (cert is null)
                        continue;

                    if (cert.Thumbprint is null || seenIntermediateThumbprints.Add(cert.Thumbprint))
                        intermediateCAs.Add(cert);
                }
                continue;
            }

            // Server certificate presented by DPS (3.3)
            if ((id.IndexOf("ServerCertInspect", StringComparison.OrdinalIgnoreCase) >= 0) || idPrefix == "3.3")
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var notes = new List<string>();

                foreach (var ln in cleaned)
                {
                    var t = ln.Trim();
                    if (t.Length == 0 || Regex.IsMatch(t, "^={3,}$"))
                        continue;

                    var fm = ServerCertFieldRegex.Match(t);
                    if (fm.Success)
                        fields[fm.Groups["k"].Value] = fm.Groups["v"].Value.Trim();
                    else
                        notes.Add(t);
                }

                if (fields.Count > 0 || notes.Count > 0)
                {
                    fields.TryGetValue("Subject", out var subject);
                    fields.TryGetValue("Issuer", out var issuer);
                    fields.TryGetValue("NotBefore", out var notBefore);
                    fields.TryGetValue("NotAfter", out var notAfter);
                    fields.TryGetValue("Thumbprint", out var thumbprint);

                    serverCertificate = new
                    {
                        Subject = subject,
                        CommonName = ExtractCommonName(subject),
                        Issuer = issuer,
                        IssuerCommonName = ExtractCommonName(issuer),
                        NotBefore = notBefore,
                        NotAfter = notAfter,
                        Thumbprint = thumbprint,
                        Notes = notes
                    };
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
        }

        return new
        {
            Source = sourceLabel,
            DeviceConfiguration = deviceConfiguration,
            DpsTcp = dpsTcp,
            TrustedRootCAs = trustedRootCAs,
            IntermediateCAs = intermediateCAs,
            ServerCertificate = serverCertificate,
            GatewayClientCertificates = gatewayClientCerts,
            WinHttpProxyService = winHttpProxyService,
            WinHttpAutoProxyService = winHttpAutoProxyService
        };
    }

    private string NormalizeMessage(string line)
    {
        var t = Regex.Replace(line, @"\d{1,2}:\d{2}:\d{2}", "");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }

    private static TranscriptCertificate? ParseCertificateLine(string line)
    {
        var m = CertificateLineRegex.Match(line);
        if (!m.Success)
            return null;

        var subject = m.Groups["subject"].Value.Trim();

        return new TranscriptCertificate
        {
            Line = line,
            Subject = subject,
            CommonName = ExtractCommonName(subject),
            NotAfter = m.Groups["notAfter"].Value.Trim(),
            Thumbprint = m.Groups["thumbprint"].Value.Trim()
        };
    }

    private static string? ExtractCommonName(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
            return null;

        var m = Regex.Match(distinguishedName, "CN=(?<cn>[^,]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["cn"].Value.Trim() : null;
    }
}

public record TranscriptCertificate
{
    public string Line { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public string? CommonName { get; init; }
    public string? NotAfter { get; init; }
    public string? Thumbprint { get; init; }
}
