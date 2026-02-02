// CEF Render Handler for offscreen rendering
//
// Captures BGRA framebuffer from CEF paint callbacks and stores
// in SharedState for consumption by Godot's texture update loop.

using System;
using UAssetViewer.Infrastructure;
using Xilium.CefGlue;

namespace UAssetViewer.Cef;

/// <summary>
/// CEF render handler for offscreen rendering.
/// Captures paint events and stores BGRA buffer in SharedState.
/// </summary>
public sealed class CefRenderHandler : CefRenderHandler
{
    private readonly SharedState _shared;
    private readonly IAppLogger _logger;

    public CefRenderHandler(SharedState shared, IAppLogger logger)
    {
        _shared = shared ?? throw new ArgumentNullException(nameof(shared));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the view rectangle for CEF rendering.
    /// </summary>
    protected override CefRect GetViewRect(CefBrowser browser)
    {
        var size = _shared.ViewportSize;
        return new CefRect(0, 0, size.Width, size.Height);
    }

    /// <summary>
    /// Called when CEF has rendered a frame.
    /// Captures the BGRA buffer and stores in SharedState.
    /// </summary>
    protected override void OnPaint(
        CefBrowser browser,
        CefPaintElementType type,
        CefRect[] dirtyRects,
        IntPtr buffer,
        int width,
        int height)
    {
        // Only handle VIEW (main view), not POPUP
        if (type != CefPaintElementType.View)
        {
            return;
        }

        if (buffer == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        try
        {
            var bufferSize = width * height * 4;

            // Copy BGRA buffer from unmanaged memory
            // CEF guarantees the buffer is valid for the duration of OnPaint
            unsafe
            {
                var span = new ReadOnlySpan<byte>((void*)buffer, bufferSize);
                _shared.UpdateFramebuffer(span, width, height);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error capturing CEF framebuffer");
        }
    }

    /// <summary>
    /// Called to get the screen info for the browser.
    /// </summary>
    protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo)
    {
        var size = _shared.ViewportSize;
        screenInfo.DeviceScaleFactor = 1.0f;
        screenInfo.Rect = new CefRect(0, 0, size.Width, size.Height);
        screenInfo.AvailableRect = screenInfo.Rect;
        return true;
    }

    /// <summary>
    /// Called to get screen point from view coordinates.
    /// </summary>
    protected override bool GetScreenPoint(CefBrowser browser, int viewX, int viewY, ref int screenX, ref int screenY)
    {
        screenX = viewX;
        screenY = viewY;
        return true;
    }

    /// <summary>
    /// Called when the popup should be shown/hidden.
    /// </summary>
    protected override void OnPopupShow(CefBrowser browser, bool show)
    {
        // We don't handle popups in this implementation
    }

    /// <summary>
    /// Called when the popup size changes.
    /// </summary>
    protected override void OnPopupSize(CefBrowser browser, CefRect rect)
    {
        // We don't handle popups in this implementation
    }

    /// <summary>
    /// Called when the scroll offset changes.
    /// </summary>
    protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y)
    {
        // No action needed for offscreen rendering
    }

    /// <summary>
    /// Called when the IME composition range changes.
    /// </summary>
    protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRect[] characterBounds)
    {
        // No action needed for offscreen rendering
    }

    /// <summary>
    /// Called when the browser needs accelerated paint handling.
    /// </summary>
    protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRect[] dirtyRects, IntPtr sharedHandle)
    {
        // We use software rendering, not accelerated
    }

    /// <summary>
    /// Called when virtual keyboard is requested.
    /// </summary>
    protected override void OnVirtualKeyboardRequested(CefBrowser browser, CefTextInputMode inputMode)
    {
        // No virtual keyboard in this implementation
    }
}
