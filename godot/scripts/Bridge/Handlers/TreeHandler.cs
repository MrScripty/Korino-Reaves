// Tree Handler - Tree Navigation via AssetManager
//
// Handles tree-related IPC messages using AssetManager's tree building.
// Provides tree structure for the asset explorer UI.

using System;
using System.Text.Json;
using System.Threading.Tasks;
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

    public string MessageType => MessageTypes.Tree;

    public TreeHandler(IAppLogger logger, AssetManager assetManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
    }

    public bool CanHandle(string action)
    {
        return action is "getRoot" or "getChildren" or "search" or "getPath";
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
