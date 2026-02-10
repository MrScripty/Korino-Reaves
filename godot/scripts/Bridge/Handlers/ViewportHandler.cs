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
    private readonly SceneManager? _sceneManager;

    public string MessageType => MessageTypes.Viewport;

    public ViewportHandler(IAppLogger logger, PreviewManager previewManager, SceneManager? sceneManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _previewManager = previewManager ?? throw new ArgumentNullException(nameof(previewManager));
        _sceneManager = sceneManager;
    }

    public bool CanHandle(string action)
    {
        return action is "orbitCamera" or "panCamera" or "zoomCamera" or "resetCamera"
            or "setDoubleSided" or "setCameraView" or "setRenderMode" or "setTimeOfDay";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Debug("ViewportHandler received: action={Action}", message.Action);

        var inSceneMode = _sceneManager?.IsActive == true;

        switch (message.Action)
        {
            case "orbitCamera":
                HandleOrbitCamera(message, inSceneMode);
                break;
            case "panCamera":
                HandlePanCamera(message, inSceneMode);
                break;
            case "zoomCamera":
                HandleZoomCamera(message, inSceneMode);
                break;
            case "resetCamera":
                if (inSceneMode) _sceneManager!.HandleCameraReset();
                else _previewManager.HandleCameraReset();
                break;
            case "setDoubleSided":
                HandleSetDoubleSided(message, inSceneMode);
                break;
            case "setCameraView":
                HandleSetCameraView(message, inSceneMode);
                break;
            case "setRenderMode":
                HandleSetRenderMode(message, inSceneMode);
                break;
            case "setTimeOfDay":
                HandleSetTimeOfDay(message, inSceneMode);
                break;
        }

        // Camera actions don't return a response — the preview frame
        // is pushed via viewport:preview after rendering.
        return Task.FromResult<IpcMessage?>(null);
    }

    private void HandleOrbitCamera(IpcMessage message, bool inSceneMode)
    {
        if (message.Payload is not JsonElement element) return;

        float dx = 0, dy = 0;
        if (element.TryGetProperty("dx", out var dxProp))
            dx = dxProp.GetSingle();
        if (element.TryGetProperty("dy", out var dyProp))
            dy = dyProp.GetSingle();

        if (inSceneMode) _sceneManager!.HandleCameraOrbit(dx, dy);
        else _previewManager.HandleCameraOrbit(dx, dy);
    }

    private void HandlePanCamera(IpcMessage message, bool inSceneMode)
    {
        if (message.Payload is not JsonElement element) return;

        float dx = 0, dy = 0;
        if (element.TryGetProperty("dx", out var dxProp))
            dx = dxProp.GetSingle();
        if (element.TryGetProperty("dy", out var dyProp))
            dy = dyProp.GetSingle();

        if (inSceneMode) _sceneManager!.HandleCameraPan(dx, dy);
        else _previewManager.HandleCameraPan(dx, dy);
    }

    private void HandleZoomCamera(IpcMessage message, bool inSceneMode)
    {
        if (message.Payload is not JsonElement element) return;

        float delta = 0;
        if (element.TryGetProperty("delta", out var deltaProp))
            delta = deltaProp.GetSingle();

        if (inSceneMode) _sceneManager!.HandleCameraZoom(delta);
        else _previewManager.HandleCameraZoom(delta);
    }

    private void HandleSetDoubleSided(IpcMessage message, bool inSceneMode)
    {
        if (message.Payload is not JsonElement element) return;

        bool enabled = true;
        if (element.TryGetProperty("enabled", out var enabledProp))
            enabled = enabledProp.GetBoolean();

        if (inSceneMode) _sceneManager!.HandleSetDoubleSided(enabled);
        else _previewManager.HandleSetDoubleSided(enabled);
    }

    private void HandleSetCameraView(IpcMessage message, bool inSceneMode)
    {
        if (message.Payload is not JsonElement element) return;

        float yaw = 0, pitch = 0;
        if (element.TryGetProperty("yaw", out var yawProp))
            yaw = yawProp.GetSingle();
        if (element.TryGetProperty("pitch", out var pitchProp))
            pitch = pitchProp.GetSingle();

        if (inSceneMode) _sceneManager!.HandleSetCameraView(yaw, pitch);
        else _previewManager.HandleSetCameraView(yaw, pitch);
    }

    private void HandleSetRenderMode(IpcMessage message, bool inSceneMode)
    {
        if (message.Payload is not JsonElement element) return;

        string mode = "shaded";
        if (element.TryGetProperty("mode", out var modeProp))
            mode = modeProp.GetString() ?? "shaded";

        if (inSceneMode) _sceneManager!.HandleSetRenderMode(mode);
        else _previewManager.HandleSetRenderMode(mode);
    }

    private void HandleSetTimeOfDay(IpcMessage message, bool inSceneMode)
    {
        if (message.Payload is not JsonElement element) return;

        float hours = 10f;
        if (element.TryGetProperty("hours", out var hoursProp))
            hours = hoursProp.GetSingle();

        if (inSceneMode) _sceneManager!.HandleSetTimeOfDay(hours);
        else _previewManager.HandleSetTimeOfDay(hours);
    }
}
