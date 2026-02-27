// Scene Handler - Level Scene IPC Commands
//
// Handles scene-related IPC messages for actor selection and scene mode control.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using UAssetViewer.Rendering;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for scene-related IPC messages.
/// Delegates actor selection and scene control to SceneManager.
/// </summary>
public sealed class SceneHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly SceneManager _sceneManager;

    public string MessageType => MessageTypes.Scene;

    public SceneHandler(IAppLogger logger, SceneManager sceneManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
    }

    public bool CanHandle(string action)
    {
        return action is "selectActor" or "focusActor" or "pickActor" or "deselectActor" or "exitScene";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Debug("SceneHandler received: action={Action}", message.Action);

        switch (message.Action)
        {
            case "selectActor":
                HandleSelectActor(message);
                break;
            case "focusActor":
                HandleFocusActor(message);
                break;
            case "pickActor":
                HandlePickActor(message);
                break;
            case "deselectActor":
                _sceneManager.SetActorSelected(null);
                break;
            case "exitScene":
                _sceneManager.ClearScene();
                break;
        }

        return Task.FromResult<IpcMessage?>(null);
    }

    private void HandleSelectActor(IpcMessage message)
    {
        if (message.Payload is not JsonElement element) return;

        string? actorId = null;
        if (element.TryGetProperty("actorId", out var idProp))
            actorId = idProp.GetString();

        if (string.IsNullOrEmpty(actorId)) return;

        _sceneManager.SetActorSelected(actorId);
    }

    private void HandleFocusActor(IpcMessage message)
    {
        if (message.Payload is not JsonElement element) return;

        string? actorId = null;
        if (element.TryGetProperty("actorId", out var idProp))
            actorId = idProp.GetString();

        if (string.IsNullOrEmpty(actorId)) return;

        _sceneManager.SelectActor(actorId);
    }

    private void HandlePickActor(IpcMessage message)
    {
        if (message.Payload is not JsonElement element) return;

        float normalizedX = 0, normalizedY = 0;
        if (element.TryGetProperty("normalizedX", out var xProp))
            normalizedX = xProp.GetSingle();
        if (element.TryGetProperty("normalizedY", out var yProp))
            normalizedY = yProp.GetSingle();

        var hitActorId = _sceneManager.PickActorAtScreenPosition(normalizedX, normalizedY);
        _sceneManager.SetActorSelected(hitActorId);
    }
}
