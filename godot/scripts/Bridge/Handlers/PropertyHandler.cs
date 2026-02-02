// Property Handler - Stub for property editing
//
// Handles property-related IPC messages. Currently returns mock data.
// Will be replaced with real property editing by Asset Agent.

using System;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for property editing IPC messages.
/// Stub implementation that returns mock data.
/// </summary>
public sealed class PropertyHandler : IMessageHandler
{
    private readonly IAppLogger _logger;

    public string MessageType => MessageTypes.Property;

    public PropertyHandler(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(string action)
    {
        return action is "get" or "set" or "getForNode";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("PropertyHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "get" => HandleGet(message),
            "set" => HandleSet(message),
            "getForNode" => HandleGetForNode(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private Task<IpcMessage?> HandleGet(IpcMessage message)
    {
        _logger.Info("Property get requested (stub)");

        var response = new IpcMessage(
            MessageTypes.Property,
            "value",
            null, // TODO: Return actual property value
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleSet(IpcMessage message)
    {
        _logger.Info("Property set requested (stub)");

        var response = new IpcMessage(
            MessageTypes.Property,
            "updated",
            new { success = true },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }

    private Task<IpcMessage?> HandleGetForNode(IpcMessage message)
    {
        _logger.Info("Properties for node requested (stub)");

        // Return mock properties
        var mockProperties = new[]
        {
            new PropertyValue(
                Path: new[] { "export-0", "Properties", "Health" },
                Type: "IntProperty",
                Value: 100,
                Editable: true,
                DisplayName: "Health",
                Category: "Stats"
            ),
            new PropertyValue(
                Path: new[] { "export-0", "Properties", "MaxHealth" },
                Type: "IntProperty",
                Value: 100,
                Editable: true,
                DisplayName: "Max Health",
                Category: "Stats"
            ),
            new PropertyValue(
                Path: new[] { "export-0", "Properties", "CharacterName" },
                Type: "StrProperty",
                Value: "Hero",
                Editable: true,
                DisplayName: "Character Name",
                Category: "Info"
            ),
            new PropertyValue(
                Path: new[] { "export-0", "Properties", "IsActive" },
                Type: "BoolProperty",
                Value: true,
                Editable: true,
                DisplayName: "Is Active",
                Category: "State"
            ),
        };

        var response = new IpcMessage(
            MessageTypes.Property,
            "properties",
            mockProperties,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        return Task.FromResult<IpcMessage?>(response);
    }
}
