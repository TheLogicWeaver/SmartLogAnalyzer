# SmartLogAnalyzer

A .NET 8 ASP.NET Core Web API application for analyzing and processing logs from IoT gateways. This project provides a scalable solution for ingesting, parsing, storing, and gaining insights from log data using Entity Framework Core and SQLite.

## Features

- **Log Ingestion**: Handles log data from various sources.
- **Flexible Parsing**: Supports multiple log formats through a factory pattern (e.g., IoT Gateway logs).
- **Database Storage**: Uses Entity Framework Core with SQLite for efficient data persistence.
- **Insights Generation**: Provides analytics and insights on log data.
- **RESTful API**: Exposes endpoints for log management and querying.
- **Migration Support**: Includes database migrations for schema management.

## Architecture

### Core Components
- **Entities**: `LogEntryEntity` and `LogMetadataEntity` for data modeling.
- **Services**:
  - `LogIngestionService`: Handles incoming log data.
  - `LogProcessingService`: Processes and parses logs.
  - `LogInsightsService`: Generates insights and analytics.
- **Parsers**: `ILogParser` interface with implementations like `IotGatewayLogParser`.
- **Factory**: `LogParserFactory` for selecting appropriate parsers.
- **Database Context**: `AppDbContext` with EF Core configuration.

### Database Schema
- **LogEntryEntity**: Stores main log entries (ID, Timestamp, Level, Message, DeviceId, Source, EventId, RawLine).
- **LogMetadataEntity**: Stores additional metadata for each log entry (Key-Value pairs).

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
- POST `/api/logs` - Ingest new log entries
- GET `/api/logs` - Retrieve log entries with filtering
- GET `/api/insights` - Get log analytics and insights

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