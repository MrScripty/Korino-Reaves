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
public sealed class OsrRenderHandler : Xilium.CefGlue.CefRenderHandler
{
    private readonly SharedState _shared;
    private readonly IAppLogger _logger;

    public OsrRenderHandler(SharedState shared, IAppLogger logger)
    {
        _shared = shared ?? throw new ArgumentNullException(nameof(shared));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override CefAccessibilityHandler GetAccessibilityHandler()
    {
        return null!;
    }

    /// <summary>
    /// Returns the view rectangle for CEF rendering.
    /// </summary>
    protected override void GetViewRect(CefBrowser browser, out CefRectangle rect)
    {
        var size = _shared.ViewportSize;
        rect = new CefRectangle(0, 0, size.Width, size.Height);
    }

    /// <summary>
    /// Called when CEF has rendered a frame.
    /// Captures the BGRA buffer and stores in SharedState.
    /// </summary>
    protected override void OnPaint(
        CefBrowser browser,
        CefPaintElementType type,
        CefRectangle[] dirtyRects,
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
        screenInfo.Rectangle = new CefRectangle(0, 0, size.Width, size.Height);
        screenInfo.AvailableRectangle = screenInfo.Rectangle;
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

    protected override void OnPopupShow(CefBrowser browser, bool show)
    {
    }

    protected override void OnPopupSize(CefBrowser browser, CefRectangle rect)
    {
    }

    protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y)
    {
    }

    protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds)
    {
    }

    protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr sharedHandle)
    {
    }

    protected override void OnVirtualKeyboardRequested(CefBrowser browser, CefTextInputMode inputMode)
    {
    }
}
