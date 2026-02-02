// Test logger implementation for unit tests

using System;
using System.Collections.Generic;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Tests;

/// <summary>
/// Test logger that captures log messages for assertions.
/// </summary>
public sealed class TestLogger : IAppLogger
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;

    public void Debug(string message, params object[] args)
    {
        _entries.Add(new LogEntry(LogLevel.Debug, FormatMessage(message, args), null));
    }

    public void Info(string message, params object[] args)
    {
        _entries.Add(new LogEntry(LogLevel.Info, FormatMessage(message, args), null));
    }

    public void Warning(string message, params object[] args)
    {
        _entries.Add(new LogEntry(LogLevel.Warning, FormatMessage(message, args), null));
    }

    public void Error(Exception ex, string message, params object[] args)
    {
        _entries.Add(new LogEntry(LogLevel.Error, FormatMessage(message, args), ex));
    }

    public void Error(string message, params object[] args)
    {
        _entries.Add(new LogEntry(LogLevel.Error, FormatMessage(message, args), null));
    }

    public IDisposable BeginScope(string operationName)
    {
        return new NoOpDisposable();
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private static string FormatMessage(string message, object[] args)
    {
        if (args == null || args.Length == 0)
        {
            return message;
        }

        // Simple placeholder replacement for testing
        var result = message;
        for (int i = 0; i < args.Length; i++)
        {
            var placeholder = $"{{{i}}}";
            if (result.Contains(placeholder))
            {
                result = result.Replace(placeholder, args[i]?.ToString() ?? "null");
            }
        }
        return result;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

public record LogEntry(LogLevel Level, string Message, Exception? Exception);
