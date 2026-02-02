// Application logging interface
//
// Provides a Godot-agnostic logging abstraction that can be used
// throughout the application for structured logging.

using System;

namespace UAssetViewer.Infrastructure;

/// <summary>
/// Application logging interface for structured logging.
/// Implementations can use Serilog, Console, or any other backend.
/// </summary>
public interface IAppLogger
{
    /// <summary>
    /// Logs a debug message. Used for detailed diagnostic info during development.
    /// </summary>
    void Debug(string message, params object[] args);

    /// <summary>
    /// Logs an informational message. Used for normal operations.
    /// </summary>
    void Info(string message, params object[] args);

    /// <summary>
    /// Logs a warning message. Used for recoverable issues.
    /// </summary>
    void Warning(string message, params object[] args);

    /// <summary>
    /// Logs an error message with exception details.
    /// </summary>
    void Error(Exception ex, string message, params object[] args);

    /// <summary>
    /// Logs an error message without exception.
    /// </summary>
    void Error(string message, params object[] args);

    /// <summary>
    /// Begins a named scope for operation correlation.
    /// Returns a disposable that ends the scope when disposed.
    /// </summary>
    IDisposable BeginScope(string operationName);
}

/// <summary>
/// Log levels for filtering.
/// </summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}
