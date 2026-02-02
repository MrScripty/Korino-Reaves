// Serilog implementation of IAppLogger
//
// Provides structured logging with console and file sinks.
// Supports scoped operations for request correlation.

using System;
using System.Diagnostics;
using System.IO;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace UAssetViewer.Infrastructure;

/// <summary>
/// Serilog-based application logger with structured logging support.
/// </summary>
public sealed class AppLogger : IAppLogger, IDisposable
{
    private static AppLogger? _instance;
    private static readonly object InstanceLock = new();

    private readonly ILogger _logger;
    private readonly ActivitySource _activitySource;
    private bool _disposed;

    /// <summary>
    /// Gets the singleton logger instance.
    /// </summary>
    public static AppLogger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (InstanceLock)
                {
                    _instance ??= new AppLogger();
                }
            }
            return _instance;
        }
    }

    private AppLogger()
    {
        // Determine log path
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UAssetViewer",
            "logs",
            "uassetviewer-.log"
        );

        // Ensure directory exists
        var logDir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        // Configure Serilog
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "UAssetViewer")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Scope}{Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Scope}{Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        // Create activity source for OpenTelemetry tracing
        _activitySource = new ActivitySource("UAssetViewer", "1.0.0");

        Info("Logger initialized. Log path: {LogPath}", logPath);
    }

    /// <summary>
    /// Configures the minimum log level at runtime.
    /// </summary>
    public static void SetMinimumLevel(LogLevel level)
    {
        // Note: Serilog doesn't support runtime level changes easily
        // For now, we filter manually or recreate the logger
        Instance.Info("Log level change requested to {Level} (not implemented)", level);
    }

    public void Debug(string message, params object[] args)
    {
        if (_disposed) return;
        _logger.Debug(message, args);
    }

    public void Info(string message, params object[] args)
    {
        if (_disposed) return;
        _logger.Information(message, args);
    }

    public void Warning(string message, params object[] args)
    {
        if (_disposed) return;
        _logger.Warning(message, args);
    }

    public void Error(Exception ex, string message, params object[] args)
    {
        if (_disposed) return;
        _logger.Error(ex, message, args);
    }

    public void Error(string message, params object[] args)
    {
        if (_disposed) return;
        _logger.Error(message, args);
    }

    public IDisposable BeginScope(string operationName)
    {
        if (_disposed)
        {
            return new NoOpDisposable();
        }

        // Start an activity for tracing
        var activity = _activitySource.StartActivity(operationName);

        // Push operation name to log context
        var logScope = LogContext.PushProperty("Scope", $"[{operationName}] ");

        return new CompositeScope(activity, logScope);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _activitySource.Dispose();

        if (_logger is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _instance = null;
    }

    /// <summary>
    /// No-op disposable for when logger is disposed.
    /// </summary>
    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>
    /// Combines activity and log context scopes.
    /// </summary>
    private sealed class CompositeScope : IDisposable
    {
        private readonly Activity? _activity;
        private readonly IDisposable? _logScope;

        public CompositeScope(Activity? activity, IDisposable? logScope)
        {
            _activity = activity;
            _logScope = logScope;
        }

        public void Dispose()
        {
            _activity?.Dispose();
            _logScope?.Dispose();
        }
    }
}
