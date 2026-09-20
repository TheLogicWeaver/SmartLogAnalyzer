# SmartLogAnalyzer

A .NET 8 ASP.NET Core Web API for analyzing logs produced by IoT gateways. Logs are uploaded to an
Azure Storage account by a separate diagnostic collection tool; this API reads them from blob
storage, caches the parsed result in memory, and exposes analytics endpoints on top.

## Features

- **Storage backed**: Reads log bundles directly from Azure Blob Storage. No local database.
- **Prefix pruning**: Device and start-time filters are translated into the smallest possible blob listing before anything is downloaded.
- **Parsed-file cache**: Uploaded blobs are immutable, so each file is downloaded and parsed at most once per version and then served from memory.
- **Collection de-duplication**: Repeat diagnostic runs re-upload the same source files; only the freshest copy of each is read, so lines are never counted twice.
- **Background warm-up**: A hosted service pre-loads recent collections so requests rarely wait on storage.
- **Flexible Parsing**: Supports multiple log formats through a parser factory pattern with extension points.
- **Anomaly Detection**: Detects spike-based anomalies and interprets them into readable issue summaries.
- **Insight Generation**: Top messages, log counts, and normalized issue grouping.
- **Connection Timeline Analysis**: Detects disconnect/reconnect windows, marks outages over 12 or 24 hours, preserves preceding error context, and exposes heartbeat activity.

## Storage layout

The collection tool writes one bundle per run, relative to the container
(`LogStorage:Container`, default `gatewaylogs`):

```
{deviceId}/{yyyy}/{MM}/{dd}/{sequence}/{collection}/00_transcript.log
{deviceId}/{yyyy}/{MM}/{dd}/{sequence}/{collection}/artefacts/ApplicationLog/*.txt
{deviceId}/{yyyy}/{MM}/{dd}/{sequence}/{collection}/artefacts/AuditLog/*.txt
```

- `{deviceId}` is the device serial number, e.g. `i393-092r0218n0013`.
- `{sequence}` is the upload sequence number for that day, e.g. `00`.
- `{collection}` is a diagnostic run, e.g. `DiagnosticLogs` or `DiagnosticLogs_102401`. Several runs
  can exist for the same day and they re-upload the same source files.
- Gateway log lines (`== {date} [device] [source] [eventId] [level] message`) live only under the
  artefact folders named in `LogStorage:ArtefactLogFolders`. Everything else under a collection
  (numbered `1.2_*.txt` section files, `EventLogs/`, `.evtx`, `.zip`) is diagnostic output, not a
  log, and is ignored.
- `00_transcript.log` is the diagnostic transcript consumed by `/api/diagnostics/transcript-summary`.
- The date in the path is the collection date. Because a bundle collected on day *D* can contain
  older lines but never newer ones, only the **lower** time bound is used to prune prefixes.

### Device identity

A gateway logs `[DeviceNotProvisioned]` until it has been provisioned, so the line itself often does
not name the device. The upload folder always does, so the folder is authoritative: `LogRecord.EffectiveDeviceId`
falls back to the folder name and every device filter, timeline, and heartbeat grouping uses it.

## Architecture

### Folder Structure
- `Storage/`
  - `LogStorageOptions.cs` — bound to the `LogStorage` configuration section
  - `LogRecord.cs` — a parsed log line
  - `LogFileReference.cs` — a discovered blob plus the coordinates decoded from its name
  - `BlobLogCatalog.cs` — cached blob listings
  - `ILogStore.cs` / `BlobLogStore.cs` — cached read, parse, and de-duplication
  - `LogCacheWarmupService.cs` — background pre-loading
- `Models/`
  - `LogEntry.cs`, `LogAnomaly.cs`, `LogIssueGroup.cs`, `AnomalyDetectionResult.cs`, `ConnectionIncident.cs`
- `Parsers/`
  - `ILogParser.cs`, `IotGatewayLogParser.cs`, `DiagnosticTranscriptParser.cs`, `LogParserFactory.cs`
- `Services/`
  - `LogProcessingService.cs`, `LogInsightsService.cs`, `LogAnomalyAnalysisService.cs`, `ConnectionAnalysisService.cs`, `DiagnosticTranscriptService.cs`
- `Detection/`
  - `IAnomalyDetector.cs`, `SpikeAnomalyDetector.cs`, `AnomalyInterpreter.cs`
- `Utilities/`
  - `MessageNormalizer.cs`

### Core Components
- **Storage**: `BlobLogCatalog` answers "which blobs exist", `BlobLogStore` answers "what is in them", both through caches.
- **Domain Models**: `LogRecord` is the parsed line; `LogEntry`, `LogAnomaly`, `LogIssueGroup`, and `AnomalyDetectionResult` capture parser output and insight results.
- **Services**:
  - `LogProcessingService`: Routes raw log lines to the correct parser.
  - `LogInsightsService`: Generates analytics, smart grouping, and query results.
  - `LogAnomalyAnalysisService`: Runs anomaly detection and interprets the output.
  - `ConnectionAnalysisService`: Rebuilds device timelines and connection incidents.
  - `DiagnosticTranscriptService`: Extracts structured checks from a device transcript.
- **Detection**: `SpikeAnomalyDetector` identifies spikes over a historical baseline, `AnomalyInterpreter` converts detections into user-facing anomalies.
- **Utilities**: `MessageNormalizer` normalizes log messages to reduce noise in grouping and anomaly detection.

### Caching model

| Layer | What it holds | Lifetime |
| --- | --- | --- |
| Catalog cache | Device folders and the blob list per device | `LogStorage:CatalogTtlSeconds` (default 5 min) |
| File cache | Parsed `LogRecord`s per blob version | Sliding `LogStorage:FileCacheSlidingMinutes`, bounded by `LogStorage:MaxCachedLogLines` |

Both caches are in-process, which keeps the deployment free of an extra cache service. Concurrent
requests for the same blob share a single download, and `LogStorage:MaxParallelDownloads` bounds how
many are fetched at once.

## Prerequisites

- .NET 8 SDK
- An Azure Storage account with a SAS token granting **read and list** (`sp=rl`) on the container

## Configuration

`appsettings.json`:

```json
"LogStorage": {
  "ServiceUri": "https://myaccount.blob.core.windows.net",
  "Container": "gatewaylogs",
  "SasToken": "",
  "RootPath": "",
  "ArtefactLogFolders": [ "ApplicationLog", "AuditLog" ],
  "CatalogTtlSeconds": 300,
  "FileCacheSlidingMinutes": 240,
  "MaxCachedLogLines": 2000000,
  "MaxParallelDownloads": 8,
  "MaxFilesPerQuery": 500,
  "WarmupEnabled": true,
  "WarmupIntervalSeconds": 600,
  "WarmupRecentDays": 3
}
```

The SAS token is a secret and must not be committed. Supply it out of band:

```bash
dotnet user-secrets set "LogStorage:SasToken" "sp=rl&st=..."
```

or via the environment variable `LogStorage__SasToken` (App Service or Container Apps application
setting, ideally sourced from Key Vault).

Startup fails fast if `ServiceUri` or `Container` is missing.

## Installation and Setup

1. **Clone the repository**:
   ```bash
   git clone <repository-url>
   cd SmartLogAnalyzer
   ```

2. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

3. **Configure the storage connection** as described above.

4. **Run the application**:
   ```bash
   dotnet run
   ```

The API will be available at `https://localhost:5001` (or as configured in `launchSettings.json`).

## Usage

### API Endpoints

The application exposes RESTful endpoints for log analysis. Refer to `SmartLogAnalyzer.http` for sample requests.

- GET `/api/devices` - List the device folders present in the container.
- GET `/api/insights/summary` - Summary counts. Accepts the standard filters below.
- GET `/api/insights/top-messages` - Get the most frequent messages, optionally filtered by level.
- GET `/api/insights/logs` - Retrieve parsed log entries.
- GET `/api/insights/smart-groups` - Receive normalized issue grouping.
- GET `/insights/anomalies?level=Error` - View detected anomaly patterns.
- GET `/api/insights/connection-incidents` - Return concise, AI-friendly connection-incident summaries. Optional query parameters: `deviceId` and `limit`.
- GET `/api/insights/connection-incidents/{incidentKey}/evidence` - Return the detailed event context, preceding 40 log lines, errors, and heartbeat executions for one incident.
- GET `/api/insights/heartbeats` - Retrieve heartbeat executions. Optional: `deviceId`, `hasCloudConnectionFailure`, `limit`.
- GET `/api/diagnostics/transcript-summary?deviceId=...` - Structured diagnostic checks from the newest transcript for a device.

Standard filters on the insights endpoints: `level`, `deviceId`, `startTime`, `endTime`, `top`, `limit`.
Supplying `deviceId` and `startTime` is strongly recommended - they are what allow the API to read a
single device prefix instead of scanning the whole container.

Connection incidents are detected from IoT Hub cloud-connectivity signals, including `not connected to cloud`, connection status changes, and failed control-message delivery. Recovery is confirmed by a connected status or successful IoT Hub delivery. When the gateway temporarily labels its log lines as `DeviceNotProvisioned`, incident analysis associates those lines with the real serial number referenced in the same log timeline; it never returns `DeviceNotProvisioned` as a device incident. The heartbeat endpoint groups each `901` start through `903` completion and returns the events and errors produced in that execution. An open incident is measured through the last available log timestamp, so an uploaded historical file is never incorrectly treated as disconnected until the present time.

### Configuration

- **appsettings.json**: Main configuration file.
- **appsettings.Development.json**: Development-specific settings.

## Development

### Building
```bash
dotnet build
```

### Testing
```bash
dotnet test
```

## Contributing

1. Fork the repository.
2. Create a feature branch.
3. Make your changes.
4. Submit a pull request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
