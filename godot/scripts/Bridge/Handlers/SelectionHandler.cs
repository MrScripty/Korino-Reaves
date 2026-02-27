// Selection Handler - Handles selection state changes
//
// Manages the current selection state and notifies when it changes.
// Handles all selection and expand/collapse IPC actions directly.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for selection-related IPC messages.
/// Manages selection state for tree nodes, including expand/collapse operations.
/// </summary>
public sealed class SelectionHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private string? _selectedId;
    private readonly HashSet<string> _expandedIds = new();

    public string MessageType => MessageTypes.Selection;

    /// <summary>
    /// Gets the current selection state.
    /// </summary>
    public SelectionState CurrentState => BuildState();

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
        return action is "select" or "getState" or "setExpanded"
            or "toggle" or "expand" or "collapse"
            or "expandAll" or "collapseAll" or "collapseBranch";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("SelectionHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "select" => HandleSelect(message),
            "getState" => HandleGetState(message),
            "setExpanded" => HandleSetExpanded(message),
            "toggle" => HandleToggle(message),
            "expand" => HandleExpand(message),
            "collapse" => HandleCollapse(message),
            "expandAll" => HandleExpandAll(message),
            "collapseAll" => HandleCollapseAll(message),
            "collapseBranch" => HandleCollapseBranch(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    // -----------------------------------------------------------------
    // IPC action handlers
    // -----------------------------------------------------------------

    private Task<IpcMessage?> HandleSelect(IpcMessage message)
    {
        string? selectedId = null;

        if (message.Payload is JsonElement element &&
            element.TryGetProperty("id", out var idProp))
        {
            selectedId = idProp.GetString();
        }

        var state = SelectNode(selectedId);

        var response = new IpcMessage(
            MessageTypes.Selection,
            "update",
            state,
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
            BuildState(),
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleSetExpanded(IpcMessage message)
    {
        if (message.Payload is JsonElement element &&
            element.TryGetProperty("expandedIds", out var idsProp) &&
            idsProp.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in idsProp.EnumerateArray())
            {
                var s = item.GetString();
                if (s != null) list.Add(s);
            }

            _expandedIds.Clear();
            _expandedIds.UnionWith(list);
            SelectionChanged?.Invoke(BuildState());
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection,
            "update",
            BuildState(),
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleToggle(IpcMessage message)
    {
        var id = ParseId(message.Payload);
        if (string.IsNullOrEmpty(id))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in toggle request"));

        ToggleExpanded(id);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", BuildState(),
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleExpand(IpcMessage message)
    {
        var id = ParseId(message.Payload);
        if (string.IsNullOrEmpty(id))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in expand request"));

        SetNodeExpanded(id, true);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", BuildState(),
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleCollapse(IpcMessage message)
    {
        var id = ParseId(message.Payload);
        if (string.IsNullOrEmpty(id))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in collapse request"));

        SetNodeExpanded(id, false);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", BuildState(),
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleExpandAll(IpcMessage message)
    {
        var ids = ParseIds(message.Payload);

        if (ids != null && ids.Length > 0)
        {
            ExpandIds(ids);
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", BuildState(),
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleCollapseAll(IpcMessage message)
    {
        CollapseAll();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", BuildState(),
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleCollapseBranch(IpcMessage message)
    {
        var ids = ParseIds(message.Payload);

        if (ids != null && ids.Length > 0)
        {
            CollapseIds(ids);
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", BuildState(),
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    // -----------------------------------------------------------------
    // Public API for other handlers (e.g. TreeHandler) to mutate state
    // -----------------------------------------------------------------

    /// <summary>
    /// Selects a node ID (or null to clear selection).
    /// </summary>
    public SelectionState SelectNode(string? id)
    {
        _logger.Info("Selection changed to: {Id}", id ?? "(none)");
        _selectedId = id;
        var state = BuildState();
        SelectionChanged?.Invoke(state);
        return state;
    }

    /// <summary>
    /// Toggles expansion of a single node.
    /// </summary>
    public SelectionState ToggleExpanded(string id)
    {
        if (!_expandedIds.Remove(id))
            _expandedIds.Add(id);

        var state = BuildState();
        SelectionChanged?.Invoke(state);
        return state;
    }

    /// <summary>
    /// Sets a single node as expanded or collapsed.
    /// </summary>
    public SelectionState SetNodeExpanded(string id, bool expanded)
    {
        if (expanded)
            _expandedIds.Add(id);
        else
            _expandedIds.Remove(id);

        var state = BuildState();
        SelectionChanged?.Invoke(state);
        return state;
    }

    /// <summary>
    /// Collapses all nodes.
    /// </summary>
    public SelectionState CollapseAll()
    {
        _expandedIds.Clear();

        var state = BuildState();
        SelectionChanged?.Invoke(state);
        return state;
    }

    /// <summary>
    /// Expands additional node IDs (union with existing).
    /// </summary>
    public SelectionState ExpandIds(string[] ids)
    {
        _expandedIds.UnionWith(ids);

        var state = BuildState();
        SelectionChanged?.Invoke(state);
        return state;
    }

    /// <summary>
    /// Collapses specific node IDs (removes them from expanded set).
    /// </summary>
    public SelectionState CollapseIds(string[] ids)
    {
        _expandedIds.ExceptWith(ids);

        var state = BuildState();
        SelectionChanged?.Invoke(state);
        return state;
    }

    // -----------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------

    private SelectionState BuildState() => new(_selectedId, _expandedIds.ToArray());

    private static string? ParseId(object? payload)
    {
        if (payload is JsonElement element)
        {
            if (element.TryGetProperty("nodeId", out var prop)) return prop.GetString();
            if (element.TryGetProperty("id", out var idProp)) return idProp.GetString();
        }
        return null;
    }

    private static string[]? ParseIds(object? payload)
    {
        if (payload is JsonElement element &&
            element.TryGetProperty("ids", out var idsProp) &&
            idsProp.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in idsProp.EnumerateArray())
            {
                var s = item.GetString();
                if (s != null) list.Add(s);
            }
            return list.ToArray();
        }
        return null;
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, string errorMessage)
    {
        return new IpcMessage(
            MessageTypes.Error, "error",
            new ErrorResponse(ErrorCodes.InvalidRequest, errorMessage, request.Id),
            request.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
