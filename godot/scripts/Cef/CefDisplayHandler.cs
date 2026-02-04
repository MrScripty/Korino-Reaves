// CEF Display Handler for IPC interception
//
// Intercepts console.log messages that contain IPC payloads,
// parses them, and forwards to the IPC dispatcher.

using System;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using Xilium.CefGlue;

namespace UAssetViewer.Cef;

/// <summary>
/// Delegate for handling IPC messages from the frontend.
/// </summary>
public delegate void IpcMessageHandler(IpcMessage message);

/// <summary>
/// CEF display handler that intercepts console.log messages for IPC.
/// Messages prefixed with IpcConstants.IpcPrefix are parsed and dispatched.
/// </summary>
public sealed class IpcDisplayHandler : Xilium.CefGlue.CefDisplayHandler
{
    private readonly SharedState _shared;
    private readonly IAppLogger _logger;

    /// <summary>
    /// Event raised when an IPC message is received from the frontend.
    /// </summary>
    public event IpcMessageHandler? MessageReceived;

    public IpcDisplayHandler(SharedState shared, IAppLogger logger)
    {
        _shared = shared ?? throw new ArgumentNullException(nameof(shared));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override bool OnConsoleMessage(
        CefBrowser browser,
        CefLogSeverity level,
        string message,
        string source,
        int line)
    {
        if (message.StartsWith(IpcConstants.IpcPrefix, StringComparison.Ordinal))
        {
            var jsonStr = message.Substring(IpcConstants.IpcPrefix.Length);
            HandleIpcMessage(jsonStr);
            return true;
        }

        return false;
    }

    private void HandleIpcMessage(string jsonStr)
    {
        try
        {
            var message = System.Text.Json.JsonSerializer.Deserialize<IpcMessage>(jsonStr);

            if (message == null)
            {
                _logger.Warning("Received null IPC message: {Json}", jsonStr);
                return;
            }

            _logger.Debug("IPC received: type={Type}, action={Action}", message.Type, message.Action);

            if (message.Type == "ui" && message.Action == "dirty")
            {
                _shared.MarkDirty();
            }

            MessageReceived?.Invoke(message);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.Warning("Failed to parse IPC message: {Json} - {Error}", jsonStr, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling IPC message: {Json}", jsonStr);
        }
    }

    protected override void OnTitleChange(CefBrowser browser, string title)
    {
        _logger.Debug("Browser title changed: {Title}", title);
    }

    protected override void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
    {
        if (frame.IsMain)
        {
            _logger.Debug("Browser URL changed: {Url}", url);
        }
    }

    protected override bool OnTooltip(CefBrowser browser, string text)
    {
        return false;
    }

    protected override void OnStatusMessage(CefBrowser browser, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _logger.Debug("Browser status: {Status}", value);
        }
    }

    protected override bool OnCursorChange(CefBrowser browser, IntPtr cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo)
    {
        return false;
    }

    protected override void OnFullscreenModeChange(CefBrowser browser, bool fullscreen)
    {
        _logger.Debug("Fullscreen mode changed: {Fullscreen}", fullscreen);
    }

    protected override bool OnAutoResize(CefBrowser browser, ref CefSize newSize)
    {
        return false;
    }

    protected override void OnLoadingProgressChange(CefBrowser browser, double progress)
    {
    }
}
