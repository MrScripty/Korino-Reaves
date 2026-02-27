// IPC Log Sink - Forwards Serilog events to the frontend via IPC
//
// Buffers events until the IPC dispatcher is connected (since the logger
// singleton is created before the dispatcher). Filters at Information level
// to avoid flooding the UI with Debug-level noise.

using System;
using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using UAssetViewer.Bridge;
using UAssetViewer.Models;

namespace UAssetViewer.Infrastructure;

/// <summary>
/// Serilog sink that forwards log events to the UI via IPC.
/// </summary>
public sealed class IpcLogSink : ILogEventSink
{
    private IpcDispatcher? _dispatcher;
    private readonly ConcurrentQueue<LogEvent> _buffer = new();
    private const int MaxBufferSize = 200;
    private readonly LogEventLevel _minimumLevel;
    [ThreadStatic] private static bool _sending;

    public IpcLogSink(LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        _minimumLevel = minimumLevel;
    }

    /// <summary>
    /// Sets the dispatcher and flushes any buffered log events.
    /// Called from MainController after the dispatcher is created.
    /// </summary>
    public void SetDispatcher(IpcDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        FlushBuffer();
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < _minimumLevel) return;

        if (_dispatcher == null)
        {
            _buffer.Enqueue(logEvent);
            while (_buffer.Count > MaxBufferSize)
                _buffer.TryDequeue(out _);
            return;
        }

        SendLogEvent(logEvent);
    }

    private void FlushBuffer()
    {
        while (_buffer.TryDequeue(out var logEvent))
        {
            SendLogEvent(logEvent);
        }
    }

    private void SendLogEvent(LogEvent logEvent)
    {
        if (_sending) return; // Prevent infinite recursion (Send → log warning → Send)
        _sending = true;
        try
        {
            var payload = new
            {
                level = logEvent.Level.ToString().ToLowerInvariant(),
                message = logEvent.RenderMessage(),
                timestamp = logEvent.Timestamp.ToUnixTimeMilliseconds(),
                exception = logEvent.Exception?.Message,
            };

            _dispatcher?.Send(MessageTypes.Log, "entry", payload);
        }
        catch
        {
            // Never let logging errors crash the application
        }
        finally
        {
            _sending = false;
        }
    }
}
