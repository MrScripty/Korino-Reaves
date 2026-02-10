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
        return action is "selectActor" or "focusActor" or "exitScene";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Debug("SceneHandler received: action={Action}", message.Action);

        switch (message.Action)
        {
            case "selectActor":
            case "focusActor":
                HandleActorAction(message);
                break;
            case "exitScene":
                _sceneManager.ClearScene();
                break;
        }

        return Task.FromResult<IpcMessage?>(null);
    }

    private void HandleActorAction(IpcMessage message)
    {
        if (message.Payload is not JsonElement element) return;

        string? actorId = null;
        if (element.TryGetProperty("actorId", out var idProp))
            actorId = idProp.GetString();

        if (string.IsNullOrEmpty(actorId)) return;

        _sceneManager.SelectActor(actorId);
    }
}
