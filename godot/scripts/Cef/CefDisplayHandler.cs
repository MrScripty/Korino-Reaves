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
public sealed class CefDisplayHandler : CefDisplayHandler
{
    private readonly SharedState _shared;
    private readonly IAppLogger _logger;

    /// <summary>
    /// Event raised when an IPC message is received from the frontend.
    /// </summary>
    public event IpcMessageHandler? MessageReceived;

    public CefDisplayHandler(SharedState shared, IAppLogger logger)
    {
        _shared = shared ?? throw new ArgumentNullException(nameof(shared));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called when a console message is logged.
    /// Intercepts IPC messages and dispatches them.
    /// </summary>
    protected override bool OnConsoleMessage(
        CefBrowser browser,
        CefLogSeverity level,
        string message,
        string source,
        int line)
    {
        // Check if this is an IPC message
        if (message.StartsWith(IpcConstants.IpcPrefix, StringComparison.Ordinal))
        {
            var jsonStr = message.Substring(IpcConstants.IpcPrefix.Length);
            HandleIpcMessage(jsonStr);

            // Return true to suppress the console message (we handled it)
            return true;
        }

        // Return false to allow normal console message handling
        return false;
    }

    /// <summary>
    /// Parses and dispatches an IPC message.
    /// </summary>
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

            // Handle special UI dirty message
            if (message.Type == "ui" && message.Action == "dirty")
            {
                _shared.MarkDirty();
            }

            // Raise event for dispatcher
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

    /// <summary>
    /// Called when the browser title changes.
    /// </summary>
    protected override void OnTitleChange(CefBrowser browser, string title)
    {
        _logger.Debug("Browser title changed: {Title}", title);
    }

    /// <summary>
    /// Called when the page URL changes.
    /// </summary>
    protected override void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
    {
        if (frame.IsMain)
        {
            _logger.Debug("Browser URL changed: {Url}", url);
        }
    }

    /// <summary>
    /// Called to display a tooltip.
    /// </summary>
    protected override bool OnTooltip(CefBrowser browser, ref string text)
    {
        // Allow default tooltip handling
        return false;
    }

    /// <summary>
    /// Called when the browser receives a status message.
    /// </summary>
    protected override void OnStatusMessage(CefBrowser browser, string value)
    {
        // Log status messages at debug level
        if (!string.IsNullOrEmpty(value))
        {
            _logger.Debug("Browser status: {Status}", value);
        }
    }

    /// <summary>
    /// Called when the cursor changes.
    /// </summary>
    protected override bool OnCursorChange(CefBrowser browser, IntPtr cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo)
    {
        // Allow default cursor handling
        return false;
    }

    /// <summary>
    /// Called when fullscreen mode changes.
    /// </summary>
    protected override void OnFullscreenModeChange(CefBrowser browser, bool fullscreen)
    {
        _logger.Debug("Fullscreen mode changed: {Fullscreen}", fullscreen);
    }

    /// <summary>
    /// Called when auto-resize is enabled and the contents have auto-resized.
    /// </summary>
    protected override bool OnAutoResize(CefBrowser browser, ref CefSize newSize)
    {
        // We manage size ourselves
        return false;
    }

    /// <summary>
    /// Called when the loading progress changes.
    /// </summary>
    protected override void OnLoadingProgressChange(CefBrowser browser, double progress)
    {
        // Could be used to show loading indicator
    }

    /// <summary>
    /// Called when media access changes.
    /// </summary>
    protected override void OnMediaAccessChange(CefBrowser browser, bool hasVideoAccess, bool hasAudioAccess)
    {
        // No media access needed
    }
}
