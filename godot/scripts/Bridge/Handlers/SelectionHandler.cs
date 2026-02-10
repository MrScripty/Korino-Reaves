// Selection Handler - Handles selection state changes
//
// Manages the current selection state and notifies when it changes.

using System;
using System.Linq;
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
            "update",
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
        string[]? ids = null;

        if (message.Payload is System.Text.Json.JsonElement element &&
            element.TryGetProperty("expandedIds", out var idsProp) &&
            idsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var item in idsProp.EnumerateArray())
            {
                var s = item.GetString();
                if (s != null) list.Add(s);
            }
            ids = list.ToArray();
        }

        if (ids != null)
        {
            _state = _state with { ExpandedIds = ids };
            SelectionChanged?.Invoke(_state);
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection,
            "update",
            _state,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    // -----------------------------------------------------------------
    // Public API for other handlers (e.g. TreeHandler) to mutate state
    // -----------------------------------------------------------------

    /// <summary>
    /// Toggles expansion of a single node.
    /// </summary>
    public SelectionState ToggleExpanded(string id)
    {
        var list = new System.Collections.Generic.List<string>(_state.ExpandedIds);
        if (!list.Remove(id))
            list.Add(id);

        _state = _state with { ExpandedIds = list.ToArray() };
        SelectionChanged?.Invoke(_state);
        return _state;
    }

    /// <summary>
    /// Sets a single node as expanded or collapsed.
    /// </summary>
    public SelectionState SetNodeExpanded(string id, bool expanded)
    {
        var list = new System.Collections.Generic.List<string>(_state.ExpandedIds);
        if (expanded && !list.Contains(id))
            list.Add(id);
        else if (!expanded)
            list.Remove(id);

        _state = _state with { ExpandedIds = list.ToArray() };
        SelectionChanged?.Invoke(_state);
        return _state;
    }

    /// <summary>
    /// Collapses all nodes.
    /// </summary>
    public SelectionState CollapseAll()
    {
        _state = _state with { ExpandedIds = Array.Empty<string>() };
        SelectionChanged?.Invoke(_state);
        return _state;
    }

    /// <summary>
    /// Expands additional node IDs (union with existing).
    /// </summary>
    public SelectionState ExpandIds(string[] ids)
    {
        var set = new System.Collections.Generic.HashSet<string>(_state.ExpandedIds);
        foreach (var id in ids) set.Add(id);

        _state = _state with { ExpandedIds = set.ToArray() };
        SelectionChanged?.Invoke(_state);
        return _state;
    }

    /// <summary>
    /// Collapses specific node IDs (removes them from expanded set).
    /// </summary>
    public SelectionState CollapseIds(string[] ids)
    {
        var set = new System.Collections.Generic.HashSet<string>(_state.ExpandedIds);
        foreach (var id in ids) set.Remove(id);

        _state = _state with { ExpandedIds = set.ToArray() };
        SelectionChanged?.Invoke(_state);
        return _state;
    }
}
