# SmartLogAnalyzer

A .NET 8 ASP.NET Core Web API application for analyzing and processing logs from IoT gateways. This project provides a scalable solution for ingesting, parsing, storing, and gaining insights from log data using Entity Framework Core and SQLite.

## Features

- **Log Ingestion**: Handles log data from uploaded files and parses them into structured records.
- **Flexible Parsing**: Supports multiple log formats through a parser factory pattern with extension points.
- **Anomaly Detection**: Detects spike-based anomalies and interprets them into readable issue summaries.
- **Insight Generation**: Provides analytics such as top messages, log counts, and normalized issue grouping.
- **Connection Timeline Analysis**: Detects disconnect/reconnect windows, marks outages over 12 or 24 hours, preserves preceding error context, and exposes heartbeat activity.
- **Database Storage**: Uses Entity Framework Core with SQLite for efficient data persistence.
- **RESTful API**: Exposes endpoints for log ingestion, analytics, and anomaly insights.
- **Migration Support**: Includes database migrations for schema management.

## Architecture

### Folder Structure
- `Data/`
  - `AppDbContext.cs`
  - `LogEntryEntity.cs`
  - `LogMetadataEntity.cs`
  - `Migrations/`
- `Models/`
  - `LogEntry.cs`
  - `LogAnomaly.cs`
  - `LogIssueGroup.cs`
  - `AnomalyDetectionResult.cs`
  - `ConnectionIncident.cs`
- `Parsers/`
  - `ILogParser.cs`
  - `IotGatewayLogParser.cs`
  - `LogParserFactory.cs`
- `Services/`
  - `LogIngestionService.cs`
  - `LogProcessingService.cs`
  - `LogInsightsService.cs`
  - `LogAnomalyAnalysisService.cs`
  - `ConnectionAnalysisService.cs`
- `Detection/`
  - `IAnomalyDetector.cs`
  - `SpikeAnomalyDetector.cs`
  - `AnomalyInterpreter.cs`
- `Utilities/`
  - `MessageNormalizer.cs`

### Core Components
- **Entities**: `LogEntryEntity` and `LogMetadataEntity` for EF Core persistence.
- **Domain Models**: `LogEntry`, `LogAnomaly`, `LogIssueGroup`, and `AnomalyDetectionResult` capture parsed data and insight results.
- **Services**:
  - `LogIngestionService`: Reads uploaded log files, parses lines, and stores entries in the database.
  - `LogProcessingService`: Routes raw log lines to the correct parser.
  - `LogInsightsService`: Generates analytics, smart grouping, and query results.
  - `LogAnomalyAnalysisService`: Runs anomaly detection and interprets the output.
- **Parsers**: `ILogParser` and `IotGatewayLogParser` support log format detection and parsing.
- **Detection**: `SpikeAnomalyDetector` implements `IAnomalyDetector` and identifies spikes over a historical baseline, while `AnomalyInterpreter` converts detection results into user-facing anomalies.
- **Utilities**: `MessageNormalizer` normalizes log messages to reduce noise in grouping and anomaly detection.

### Database Schema
- **LogEntryEntity**: Stores main log entries with timestamp, device, source, event ID, level, message, raw line, and metadata.
- **LogMetadataEntity**: Stores additional metadata key/value pairs for each log entry.

## Prerequisites

- .NET 8 SDK
- SQLite (included with EF Core)

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

3. **Apply database migrations**:
   ```bash
   dotnet ef database update
   ```

4. **Run the application**:
   ```bash
   dotnet run
   ```

The API will be available at `https://localhost:5001` (or as configured in `launchSettings.json`).

## Usage

### API Endpoints

The application exposes RESTful endpoints for log management. Refer to `SmartLogAnalyzer.http` for sample requests.

Example endpoints:
- POST `/upload-log` - Ingest new log entries from an uploaded file.
- GET `/api/insights/summary` - Retrieve summary counts.
- GET `/api/insights/top-messages` - Get the most frequent messages, optionally filtered by level.
- GET `/api/insights/logs` - Retrieve parsed log entries.
- GET `/api/insights/smart-groups` - Receive normalized issue grouping.
- GET `/insights/anomalies` - View detected anomaly patterns.
- GET `/api/insights/connection-incidents` - Inspect connection outages. Optional query parameters: `deviceId`, `startTime`, `endTime`, and `limit`. Each incident includes the 40 preceding parsed log lines.
- GET `/api/insights/heartbeats` - Retrieve parsed heartbeat log entries with the same filters.

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

### Database Migrations
To create a new migration:
```bash
dotnet ef migrations add <MigrationName>
```

## Contributing

1. Fork the repository.
2. Create a feature branch.
3. Make your changes.
4. Submit a pull request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
