// CEF Browser Wrapper
//
// High-level wrapper around CEF browser instance.
// Provides navigation, JavaScript execution, and input handling.

using System;
using System.Web;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using Xilium.CefGlue;

namespace UAssetViewer.Cef;

/// <summary>
/// High-level wrapper for a CEF browser instance.
/// Manages browser lifecycle, navigation, and communication.
/// </summary>
public sealed class CefBrowserWrapper : IDisposable
{
    private readonly SharedState _shared;
    private readonly CefClientImpl _client;
    private readonly IAppLogger _logger;
    private CefBrowser? _browser;
    private bool _disposed;

    /// <summary>
    /// Event raised when an IPC message is received from the frontend.
    /// </summary>
    public event IpcMessageHandler? MessageReceived
    {
        add => _client.DisplayHandler.MessageReceived += value;
        remove => _client.DisplayHandler.MessageReceived -= value;
    }

    /// <summary>
    /// Gets the shared state for framebuffer access.
    /// </summary>
    public SharedState SharedState => _shared;

    /// <summary>
    /// Gets whether the browser has been created.
    /// </summary>
    public bool IsCreated => _browser != null;

    /// <summary>
    /// Gets whether the framebuffer is dirty (has been updated).
    /// </summary>
    public bool IsDirty => _shared.IsDirty;

    public CefBrowserWrapper(IAppLogger? logger = null)
    {
        _logger = logger ?? AppLogger.Instance;
        _shared = new SharedState();
        _client = new CefClientImpl(_shared, _logger);
    }

    /// <summary>
    /// Creates the browser and navigates to the specified URL.
    /// </summary>
    /// <param name="url">URL to navigate to (can be file://, http://, or data:)</param>
    /// <param name="width">Initial viewport width</param>
    /// <param name="height">Initial viewport height</param>
    public void Create(string url, int width = 1920, int height = 1080)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_browser != null)
        {
            throw new InvalidOperationException("Browser already created");
        }

        if (!CefManager.Instance.IsInitialized)
        {
            throw new InvalidOperationException("CEF must be initialized before creating browsers");
        }

        using var scope = _logger.BeginScope("CefBrowserWrapper.Create");
        _logger.Info("Creating browser with size {Width}x{Height}, URL: {Url}", width, height, url);

        _shared.ViewportSize = (width, height);

        // Configure window info for offscreen rendering
        var windowInfo = CefWindowInfo.Create();
        windowInfo.SetAsWindowless(IntPtr.Zero, false);

        // Browser settings
        var browserSettings = new CefBrowserSettings
        {
            WindowlessFrameRate = 60,
        };

        // Create browser synchronously
        _browser = CefBrowserHost.CreateBrowserSync(
            windowInfo,
            _client,
            browserSettings,
            url
        );

        if (_browser == null)
        {
            throw new InvalidOperationException("Failed to create CEF browser");
        }

        _logger.Info("Browser created successfully");
    }

    /// <summary>
    /// Creates a browser with HTML content using a data URL.
    /// </summary>
    /// <param name="htmlContent">HTML content to display</param>
    /// <param name="width">Initial viewport width</param>
    /// <param name="height">Initial viewport height</param>
    public void CreateWithHtml(string htmlContent, int width = 1920, int height = 1080)
    {
        var encodedHtml = HttpUtility.UrlEncode(htmlContent);
        var dataUrl = $"data:text/html,{encodedHtml}";
        Create(dataUrl, width, height);
    }

    /// <summary>
    /// Navigates to a new URL.
    /// </summary>
    public void Navigate(string url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_browser == null)
        {
            throw new InvalidOperationException("Browser not created");
        }

        _logger.Debug("Navigating to: {Url}", url);
        _browser.GetMainFrame()?.LoadUrl(url);
    }

    /// <summary>
    /// Executes JavaScript in the browser.
    /// </summary>
    /// <param name="code">JavaScript code to execute</param>
    public void ExecuteJavaScript(string code)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_browser == null)
        {
            throw new InvalidOperationException("Browser not created");
        }

        _browser.GetMainFrame()?.ExecuteJavaScript(code, string.Empty, 0);
    }

    /// <summary>
    /// Sends an IPC message to the frontend.
    /// </summary>
    /// <param name="message">Message to send</param>
    public void SendMessage(IpcMessage message)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(message);
        var escapedJson = json.Replace("'", "\\'");
        var code = $"window.{IpcConstants.IpcReceiver}('{escapedJson}')";
        ExecuteJavaScript(code);
    }

    /// <summary>
    /// Sends an IPC message to the frontend.
    /// </summary>
    /// <param name="type">Message type</param>
    /// <param name="action">Action name</param>
    /// <param name="payload">Payload data</param>
    /// <param name="id">Optional correlation ID</param>
    public void SendMessage(string type, string action, object? payload, string? id = null)
    {
        var message = new IpcMessage(type, action, payload, id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        SendMessage(message);
    }

    /// <summary>
    /// Resizes the browser viewport.
    /// </summary>
    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_browser == null)
        {
            return;
        }

        _shared.ViewportSize = (width, height);
        _browser.GetHost()?.WasResized();
        _logger.Debug("Browser resized to {Width}x{Height}", width, height);
    }

    /// <summary>
    /// Sends a mouse move event to the browser.
    /// </summary>
    public void SendMouseMove(int x, int y, CefEventFlags modifiers = CefEventFlags.None)
    {
        if (_disposed || _browser == null) return;

        var evt = new CefMouseEvent
        {
            X = x,
            Y = y,
            Modifiers = modifiers,
        };
        _browser.GetHost()?.SendMouseMoveEvent(evt, false);
    }

    /// <summary>
    /// Sends a mouse button event to the browser.
    /// </summary>
    public void SendMouseButton(int x, int y, CefMouseButtonType button, bool isDown, int clickCount = 1, CefEventFlags modifiers = CefEventFlags.None)
    {
        if (_disposed || _browser == null) return;

        var evt = new CefMouseEvent
        {
            X = x,
            Y = y,
            Modifiers = modifiers,
        };
        _browser.GetHost()?.SendMouseClickEvent(evt, button, !isDown, clickCount);
    }

    /// <summary>
    /// Sends a mouse wheel event to the browser.
    /// </summary>
    public void SendMouseWheel(int x, int y, int deltaX, int deltaY, CefEventFlags modifiers = CefEventFlags.None)
    {
        if (_disposed || _browser == null) return;

        var evt = new CefMouseEvent
        {
            X = x,
            Y = y,
            Modifiers = modifiers,
        };
        _browser.GetHost()?.SendMouseWheelEvent(evt, deltaX, deltaY);
    }

    /// <summary>
    /// Sends a key event to the browser.
    /// </summary>
    public void SendKeyEvent(CefKeyEvent evt)
    {
        if (_disposed || _browser == null) return;
        _browser.GetHost()?.SendKeyEvent(evt);
    }

    /// <summary>
    /// Sets focus on the browser.
    /// </summary>
    public void SetFocus(bool focus)
    {
        if (_disposed || _browser == null) return;
        _browser.GetHost()?.SetFocus(focus);
    }

    /// <summary>
    /// Captures the current framebuffer if it has been updated.
    /// </summary>
    /// <returns>Tuple of (BGRA data, width, height) or null if not dirty</returns>
    public (byte[] Data, int Width, int Height)? CaptureIfDirty()
    {
        return _shared.CaptureIfDirty();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.Info("Disposing browser wrapper");

        if (_browser != null)
        {
            var host = _browser.GetHost();
            host?.CloseBrowser(true);
            _browser.Dispose();
            _browser = null;
        }

        _shared.Dispose();
    }
}
