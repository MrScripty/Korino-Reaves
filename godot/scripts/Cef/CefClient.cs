// CEF Client - Combines handlers for browser instance
//
// Provides the render handler and display handler to CEF.

using System;
using UAssetViewer.Infrastructure;
using Xilium.CefGlue;

namespace UAssetViewer.Cef;

/// <summary>
/// CEF client that provides render and display handlers.
/// </summary>
public sealed class CefClientImpl : CefClient
{
    private readonly CefRenderHandler _renderHandler;
    private readonly CefDisplayHandler _displayHandler;

    public CefClientImpl(SharedState shared, IAppLogger logger)
    {
        _renderHandler = new CefRenderHandler(shared, logger);
        _displayHandler = new CefDisplayHandler(shared, logger);
    }

    /// <summary>
    /// Gets the display handler for IPC interception.
    /// </summary>
    public CefDisplayHandler DisplayHandler => _displayHandler;

    protected override CefRenderHandler? GetRenderHandler()
    {
        return _renderHandler;
    }

    protected override CefDisplayHandler? GetDisplayHandler()
    {
        return _displayHandler;
    }

    protected override CefLifeSpanHandler? GetLifeSpanHandler()
    {
        return null;
    }

    protected override CefLoadHandler? GetLoadHandler()
    {
        return null;
    }

    protected override CefRequestHandler? GetRequestHandler()
    {
        return null;
    }

    protected override CefContextMenuHandler? GetContextMenuHandler()
    {
        return null;
    }

    protected override CefDialogHandler? GetDialogHandler()
    {
        return null;
    }

    protected override CefDownloadHandler? GetDownloadHandler()
    {
        return null;
    }

    protected override CefDragHandler? GetDragHandler()
    {
        return null;
    }

    protected override CefFindHandler? GetFindHandler()
    {
        return null;
    }

    protected override CefFocusHandler? GetFocusHandler()
    {
        return null;
    }

    protected override CefJSDialogHandler? GetJSDialogHandler()
    {
        return null;
    }

    protected override CefKeyboardHandler? GetKeyboardHandler()
    {
        return null;
    }

    protected override CefPrintHandler? GetPrintHandler()
    {
        return null;
    }

    protected override CefPermissionHandler? GetPermissionHandler()
    {
        return null;
    }

    protected override CefAudioHandler? GetAudioHandler()
    {
        return null;
    }

    protected override CefCommandHandler? GetCommandHandler()
    {
        return null;
    }

    protected override CefFrameHandler? GetFrameHandler()
    {
        return null;
    }

    protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message)
    {
        // No custom process messages
        return false;
    }
}
