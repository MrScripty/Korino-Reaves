// Asset Handler - Stub for asset operations
//
// Handles asset-related IPC messages. Currently returns mock data.
// Will be replaced with real UAssetAPI integration by Asset Agent.

using System;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for asset-related IPC messages.
/// Stub implementation that returns mock data.
/// </summary>
public sealed class AssetHandler : IMessageHandler
{
    private readonly IAppLogger _logger;

    public string MessageType => MessageTypes.Asset;

    public AssetHandler(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(string action)
    {
        return action is "open" or "save" or "close" or "getInfo";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("AssetHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "open" => HandleOpen(message),
            "save" => HandleSave(message),
            "close" => HandleClose(message),
            "getInfo" => HandleGetInfo(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private Task<IpcMessage?> HandleOpen(IpcMessage message)
    {
        // TODO: Implement with UAssetAPI
        _logger.Info("Asset open requested (stub)");

        var mockAsset = new AssetInfo(
            Path: "/Game/Characters/Hero/BP_Hero.uasset",
            FileName: "BP_Hero.uasset",
            EngineVersion: "5.3",
            AssetClass: "Blueprint",
            ExportCount: 5,
            ImportCount: 12,
            IsLoaded: true
        );

        var response = new IpcMessage(
            MessageTypes.Asset,
            "opened",
            mockAsset,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleSave(IpcMessage message)
    {
        // TODO: Implement with UAssetAPI
        _logger.Info("Asset save requested (stub)");

        var response = new IpcMessage(
            MessageTypes.Asset,
            "saved",
            new { success = true },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleClose(IpcMessage message)
    {
        _logger.Info("Asset close requested (stub)");

        var response = new IpcMessage(
            MessageTypes.Asset,
            "closed",
            new { success = true },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleGetInfo(IpcMessage message)
    {
        _logger.Info("Asset info requested (stub)");

        // Return null if no asset loaded
        var response = new IpcMessage(
            MessageTypes.Asset,
            "info",
            null,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }
}
