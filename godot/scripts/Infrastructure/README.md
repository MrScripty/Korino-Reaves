# Infrastructure

## Purpose

Cross-cutting infrastructure services that can be used throughout the application.
Contains logging, telemetry, and other foundational components.

## Contents

- `IAppLogger.cs` - Application logging interface
- `AppLogger.cs` - Serilog implementation with console and file sinks

## Design Decisions

- **Interface Abstraction**: `IAppLogger` allows swapping logging implementations
  without changing application code. Currently uses Serilog, but could use any backend.

- **Structured Logging**: All log methods support structured parameters (`{Name}` placeholders)
  for better searchability and filtering in log aggregators.

- **Scoped Operations**: `BeginScope` creates both an Activity (for OpenTelemetry tracing)
  and a Serilog LogContext for correlating related log entries.

- **Singleton Pattern**: `AppLogger.Instance` provides global access. This is intentional
  for logging - it's a cross-cutting concern that shouldn't require dependency injection.

## Dependencies

- Internal: None
- External: Serilog, Serilog.Sinks.Console, Serilog.Sinks.File, OpenTelemetry

## Usage Examples

```csharp
// Get logger instance
var logger = AppLogger.Instance;

// Log at different levels
logger.Debug("Processing item {Id}", itemId);
logger.Info("Asset loaded: {Path}", assetPath);
logger.Warning("Fallback used for {Feature}", featureName);
logger.Error(ex, "Failed to save asset: {Path}", path);

// Scoped operations
using (logger.BeginScope("LoadAsset"))
{
    logger.Info("Loading {Path}", path);
    // All logs in this block include the scope
}
```

## Log Output

Console format:
```
[14:32:15 INF] [LoadAsset] Loading /Game/Characters/Hero.uasset
```

File format (in `~/.local/share/UAssetViewer/logs/`):
```
2024-01-15 14:32:15.123 +00:00 [INF] [LoadAsset] Loading /Game/Characters/Hero.uasset
```
