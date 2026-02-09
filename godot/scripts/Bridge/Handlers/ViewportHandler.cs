// Viewport Handler - 3D/2D Preview Controls
//
// Handles viewport-related IPC messages for camera controls
// and preview rendering options.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using UAssetViewer.Rendering;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for viewport-related IPC messages.
/// Delegates camera and rendering controls to PreviewManager.
/// </summary>
public sealed class ViewportHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly PreviewManager _previewManager;

    public string MessageType => MessageTypes.Viewport;

    public ViewportHandler(IAppLogger logger, PreviewManager previewManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _previewManager = previewManager ?? throw new ArgumentNullException(nameof(previewManager));
    }

    public bool CanHandle(string action)
    {
        return action is "orbitCamera" or "zoomCamera" or "resetCamera" or "setDoubleSided";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Debug("ViewportHandler received: action={Action}", message.Action);

        switch (message.Action)
        {
            case "orbitCamera":
                HandleOrbitCamera(message);
                break;
            case "zoomCamera":
                HandleZoomCamera(message);
                break;
            case "resetCamera":
                _previewManager.HandleCameraReset();
                break;
            case "setDoubleSided":
                HandleSetDoubleSided(message);
                break;
        }

        // Camera actions don't return a response — the preview frame
        // is pushed via viewport:preview after rendering.
        return Task.FromResult<IpcMessage?>(null);
    }

    private void HandleOrbitCamera(IpcMessage message)
    {
        if (message.Payload is not JsonElement element) return;

        float dx = 0, dy = 0;
        if (element.TryGetProperty("dx", out var dxProp))
            dx = dxProp.GetSingle();
        if (element.TryGetProperty("dy", out var dyProp))
            dy = dyProp.GetSingle();

        _previewManager.HandleCameraOrbit(dx, dy);
    }

    private void HandleZoomCamera(IpcMessage message)
    {
        if (message.Payload is not JsonElement element) return;

        float delta = 0;
        if (element.TryGetProperty("delta", out var deltaProp))
            delta = deltaProp.GetSingle();

        _previewManager.HandleCameraZoom(delta);
    }

    private void HandleSetDoubleSided(IpcMessage message)
    {
        if (message.Payload is not JsonElement element) return;

        bool enabled = true;
        if (element.TryGetProperty("enabled", out var enabledProp))
            enabled = enabledProp.GetBoolean();

        _previewManager.HandleSetDoubleSided(enabled);
    }
}
