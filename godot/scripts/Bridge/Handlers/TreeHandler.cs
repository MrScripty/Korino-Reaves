// Tree Handler - Tree Navigation via AssetManager
//
// Handles tree-related IPC messages using AssetManager's tree building.
// Provides tree structure for the asset explorer UI.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for tree navigation IPC messages.
/// Uses AssetManager for real tree building.
/// </summary>
public sealed class TreeHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly AssetManager _assetManager;
    private readonly IpcDispatcher _dispatcher;

    public string MessageType => MessageTypes.Tree;

    public TreeHandler(IAppLogger logger, AssetManager assetManager, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CanHandle(string action)
    {
        return action is "getRoot" or "getChildren" or "search" or "getPath"
            or "toggle" or "expand" or "collapse" or "expandAll" or "collapseAll"
            or "expandBranch" or "collapseBranch" or "openInFileBrowser";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("TreeHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "getRoot" => HandleGetRoot(message),
            "getChildren" => HandleGetChildren(message),
            "search" => HandleSearch(message),
            "getPath" => HandleGetPath(message),
            "toggle" => HandleToggle(message),
            "expand" => HandleExpand(message),
            "collapse" => HandleCollapse(message),
            "expandAll" => HandleExpandAll(message),
            "collapseAll" => HandleCollapseAll(message),
            "expandBranch" => HandleExpandBranch(message),
            "collapseBranch" => HandleCollapseBranch(message),
            "openInFileBrowser" => HandleOpenInFileBrowser(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private Task<IpcMessage?> HandleGetRoot(IpcMessage message)
    {
        _logger.Info("Getting tree root nodes");

        if (!_assetManager.IsLoaded)
        {
            return Task.FromResult<IpcMessage?>(CreateEmptyResponse(message, "root"));
        }

        var rootNodes = _assetManager.GetRootNodes();

        var response = new IpcMessage(
            MessageTypes.Tree,
            "root",
            rootNodes,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleGetChildren(IpcMessage message)
    {
        _logger.Info("Getting tree children");

        if (!_assetManager.IsLoaded)
        {
            return Task.FromResult<IpcMessage?>(CreateEmptyResponse(message, "children"));
        }

        // Parse the nodeId from payload
        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing nodeId in request"));
        }

        var children = _assetManager.GetChildren(nodeId);

        var response = new IpcMessage(
            MessageTypes.Tree,
            "children",
            new { nodeId, children },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleSearch(IpcMessage message)
    {
        _logger.Info("Searching tree");

        if (!_assetManager.IsLoaded)
        {
            return Task.FromResult<IpcMessage?>(CreateEmptyResponse(message, "searchResults"));
        }

        // Parse the query from payload
        var query = ParseQuery(message.Payload);
        if (string.IsNullOrEmpty(query))
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing query in request"));
        }

        var results = _assetManager.Search(query);

        var response = new IpcMessage(
            MessageTypes.Tree,
            "searchResults",
            new { query, results },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleGetPath(IpcMessage message)
    {
        _logger.Info("Getting path to node");

        if (!_assetManager.IsLoaded)
        {
            return Task.FromResult<IpcMessage?>(CreateEmptyResponse(message, "path"));
        }

        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing nodeId in request"));
        }

        var path = _assetManager.GetPathToNode(nodeId);

        var response = new IpcMessage(
            MessageTypes.Tree,
            "path",
            new { nodeId, path },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    // -----------------------------------------------------------------
    // Expand / Collapse handlers
    // -----------------------------------------------------------------

    private SelectionHandler? GetSelectionHandler()
    {
        return _dispatcher.GetHandler<SelectionHandler>();
    }

    private Task<IpcMessage?> HandleToggle(IpcMessage message)
    {
        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in toggle request"));

        var sel = GetSelectionHandler();
        if (sel == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "SelectionHandler not available"));

        var newState = sel.ToggleExpanded(nodeId);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", newState,
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleExpand(IpcMessage message)
    {
        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in expand request"));

        var sel = GetSelectionHandler();
        if (sel == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "SelectionHandler not available"));

        var newState = sel.SetNodeExpanded(nodeId, true);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", newState,
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleCollapse(IpcMessage message)
    {
        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in collapse request"));

        var sel = GetSelectionHandler();
        if (sel == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "SelectionHandler not available"));

        var newState = sel.SetNodeExpanded(nodeId, false);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", newState,
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleExpandAll(IpcMessage message)
    {
        var sel = GetSelectionHandler();
        if (sel == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "SelectionHandler not available"));

        // Collect all expandable node IDs from the payload if provided,
        // otherwise this is a no-op (frontend should send IDs).
        string[]? ids = null;
        if (message.Payload is JsonElement element &&
            element.TryGetProperty("ids", out var idsProp) &&
            idsProp.ValueKind == JsonValueKind.Array)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var item in idsProp.EnumerateArray())
            {
                var s = item.GetString();
                if (s != null) list.Add(s);
            }
            ids = list.ToArray();
        }

        Models.SelectionState newState;
        if (ids != null && ids.Length > 0)
        {
            newState = sel.ExpandIds(ids);
        }
        else
        {
            // No IDs provided - nothing to expand
            newState = sel.CurrentState;
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", newState,
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleCollapseAll(IpcMessage message)
    {
        var sel = GetSelectionHandler();
        if (sel == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "SelectionHandler not available"));

        var newState = sel.CollapseAll();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", newState,
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleExpandBranch(IpcMessage message)
    {
        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in expandBranch request"));

        var sel = GetSelectionHandler();
        if (sel == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "SelectionHandler not available"));

        // Start with IDs the frontend already collected (pre-loaded children, e.g. file tree)
        var allIds = new System.Collections.Generic.HashSet<string>();
        if (message.Payload is JsonElement el &&
            el.TryGetProperty("ids", out var idsProp) &&
            idsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in idsProp.EnumerateArray())
            {
                var s = item.GetString();
                if (s != null) allIds.Add(s);
            }
        }

        // Recursively load lazy children from asset manager and send to frontend
        if (_assetManager.IsLoaded)
        {
            LoadBranchChildren(nodeId, allIds);
        }

        Models.SelectionState newState;
        if (allIds.Count > 0)
        {
            newState = sel.ExpandIds(allIds.ToArray());
        }
        else
        {
            newState = sel.CurrentState;
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", newState,
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    /// <summary>
    /// Recursively loads children from the asset manager and pushes them to the frontend.
    /// Collects all expandable node IDs along the way.
    /// </summary>
    private void LoadBranchChildren(string nodeId, System.Collections.Generic.HashSet<string> expandIds)
    {
        expandIds.Add(nodeId);

        var children = _assetManager.GetChildren(nodeId);
        if (children == null || children.Length == 0) return;

        // Push children data to frontend
        _dispatcher.Send(MessageTypes.Tree, "children",
            new { parentId = nodeId, children });

        // Recurse into children that have their own children
        foreach (var child in children)
        {
            if (child.HasChildren)
            {
                LoadBranchChildren(child.Id, expandIds);
            }
        }
    }

    private Task<IpcMessage?> HandleCollapseBranch(IpcMessage message)
    {
        var sel = GetSelectionHandler();
        if (sel == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "SelectionHandler not available"));

        string[]? ids = null;
        if (message.Payload is JsonElement element &&
            element.TryGetProperty("ids", out var idsProp) &&
            idsProp.ValueKind == JsonValueKind.Array)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var item in idsProp.EnumerateArray())
            {
                var s = item.GetString();
                if (s != null) list.Add(s);
            }
            ids = list.ToArray();
        }

        Models.SelectionState newState;
        if (ids != null && ids.Length > 0)
        {
            newState = sel.CollapseIds(ids);
        }
        else
        {
            newState = sel.CurrentState;
        }

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Selection, "update", newState,
            message.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private Task<IpcMessage?> HandleOpenInFileBrowser(IpcMessage message)
    {
        var nodeId = ParseNodeId(message.Payload);
        if (string.IsNullOrEmpty(nodeId))
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing id in openInFileBrowser request"));

        var projectHandler = _dispatcher.GetHandler<ProjectHandler>();
        if (projectHandler?.CurrentProject == null)
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "No project open"));

        // Strip file: or folder: prefix to get relative path
        string relativePath;
        if (nodeId.StartsWith("file:"))
            relativePath = nodeId.Substring(5);
        else if (nodeId.StartsWith("folder:"))
            relativePath = nodeId.Substring(7);
        else
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Invalid node id for file browser"));

        var absolutePath = Path.Combine(projectHandler.CurrentProject.Path, relativePath);

        _logger.Info("Opening in file browser: {Path}", absolutePath);
        OS.ShellShowInFileManager(absolutePath);

        return Task.FromResult<IpcMessage?>(null);
    }

    // -----------------------------------------------------------------
    // Utility methods
    // -----------------------------------------------------------------

    private static string? ParseNodeId(object? payload)
    {
        if (payload is JsonElement element)
        {
            if (element.TryGetProperty("nodeId", out var prop))
            {
                return prop.GetString();
            }
            if (element.TryGetProperty("id", out var idProp))
            {
                return idProp.GetString();
            }
        }
        return null;
    }

    private static string? ParseQuery(object? payload)
    {
        if (payload is JsonElement element && element.TryGetProperty("query", out var prop))
        {
            return prop.GetString();
        }
        return null;
    }

    private static IpcMessage CreateEmptyResponse(IpcMessage request, string action)
    {
        return new IpcMessage(
            MessageTypes.Tree,
            action,
            Array.Empty<TreeNode>(),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, string errorMessage)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "error",
            new ErrorResponse(ErrorCodes.InvalidRequest, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }
}
