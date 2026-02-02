// Selection Handler - Handles selection state changes
//
// Manages the current selection state and notifies when it changes.

using System;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for selection-related IPC messages.
/// Manages selection state for tree nodes.
/// </summary>
public sealed class SelectionHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private SelectionState _state = new(null, Array.Empty<string>());

    public string MessageType => MessageTypes.Selection;

    /// <summary>
    /// Gets the current selection state.
    /// </summary>
    public SelectionState CurrentState => _state;

    /// <summary>
    /// Event raised when selection changes.
    /// </summary>
    public event Action<SelectionState>? SelectionChanged;

    public SelectionHandler(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(string action)
    {
        return action is "select" or "getState" or "setExpanded";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("SelectionHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "select" => HandleSelect(message),
            "getState" => HandleGetState(message),
            "setExpanded" => HandleSetExpanded(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private Task<IpcMessage?> HandleSelect(IpcMessage message)
    {
        string? selectedId = null;

        if (message.Payload is System.Text.Json.JsonElement element &&
            element.TryGetProperty("id", out var idProp))
        {
            selectedId = idProp.GetString();
        }

        _logger.Info("Selection changed to: {Id}", selectedId ?? "(none)");

        _state = _state with { SelectedId = selectedId };
        SelectionChanged?.Invoke(_state);

        var response = new IpcMessage(
            MessageTypes.Selection,
            "changed",
            _state,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleGetState(IpcMessage message)
    {
        var response = new IpcMessage(
            MessageTypes.Selection,
            "state",
            _state,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleSetExpanded(IpcMessage message)
    {
        // TODO: Update expanded IDs from payload
        _logger.Info("Expanded state update requested (stub)");

        var response = new IpcMessage(
            MessageTypes.Selection,
            "expandedUpdated",
            _state,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }
}
