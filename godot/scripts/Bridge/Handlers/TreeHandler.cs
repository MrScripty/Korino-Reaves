// Tree Handler - Stub for tree navigation
//
// Handles tree-related IPC messages. Currently returns mock data.
// Will be replaced with real tree building by Asset Agent.

using System;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for tree navigation IPC messages.
/// Stub implementation that returns mock data.
/// </summary>
public sealed class TreeHandler : IMessageHandler
{
    private readonly IAppLogger _logger;

    public string MessageType => MessageTypes.Tree;

    public TreeHandler(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(string action)
    {
        return action is "getChildren" or "expand" or "collapse" or "getRoot";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("TreeHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "getRoot" => HandleGetRoot(message),
            "getChildren" => HandleGetChildren(message),
            "expand" => HandleExpand(message),
            "collapse" => HandleCollapse(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private Task<IpcMessage?> HandleGetRoot(IpcMessage message)
    {
        _logger.Info("Tree root requested (stub)");

        // Return mock tree structure
        var mockNodes = new[]
        {
            new TreeNode(
                Id: "root",
                Name: "BP_Hero",
                Type: "Asset",
                HasChildren: true,
                Children: null,
                IsExpanded: false,
                IconHint: "asset"
            ),
        };

        var response = new IpcMessage(
            MessageTypes.Tree,
            "root",
            mockNodes,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleGetChildren(IpcMessage message)
    {
        _logger.Info("Tree children requested (stub)");

        // Return mock children
        var mockChildren = new[]
        {
            new TreeNode("export-0", "Export[0]: BlueprintGeneratedClass", "Export", true, null, false, "export"),
            new TreeNode("export-1", "Export[1]: Default__BP_Hero_C", "Export", true, null, false, "export"),
            new TreeNode("import-0", "Import[0]: /Script/Engine.BlueprintGeneratedClass", "Import", false, null, false, "import"),
        };

        var response = new IpcMessage(
            MessageTypes.Tree,
            "children",
            mockChildren,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleExpand(IpcMessage message)
    {
        _logger.Info("Tree expand requested (stub)");

        var response = new IpcMessage(
            MessageTypes.Tree,
            "expanded",
            new { success = true },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleCollapse(IpcMessage message)
    {
        _logger.Info("Tree collapse requested (stub)");

        var response = new IpcMessage(
            MessageTypes.Tree,
            "collapsed",
            new { success = true },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }
}
